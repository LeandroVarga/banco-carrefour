# Runbook de Implantação AWS

Este runbook descreve a implantação AWS de referência do case. Ele não afirma que a implantação foi executada. A execução real depende de conta AWS, permissões, backend Terraform, imagens publicadas e validações operacionais.

Decisões relacionadas: [ADR-0010](../decisions/ADR-0010-execucao-local-portabilidade-cloud-e-padroes-corporativos.md) e [ADR-0015](../decisions/ADR-0015-ci-cd-publicacao-imagens-e-terraform.md). A referência de infraestrutura está em [infra/README.md](../../infra/README.md).

## 1. Pré-requisitos

```text
- conta AWS e região definidas
- GitHub Actions com OIDC federado para AWS
- roles IAM separadas para CI/CD, Terraform e deploy
- backend Terraform em S3 com lock em DynamoDB, se adotado
- ECR criado ou provisionado por Terraform
- VPC, subnets, security groups e rotas definidos
- domínios, certificados, API Gateway com WAF, VPC Link/private integration e ALB interno definidos
- política de secrets, KMS, logs e retenção aprovada
```

## 2. Terraform

O Terraform deve provisionar, no mínimo:

```text
- rede: VPC, subnets, rotas, security groups e endpoints quando aplicável
- ECR para imagens das quatro unidades
- ECS Fargate para Ledger.Api, Ledger.OutboxPublisher, Consolidation.Worker e Consolidation.Api
- RDS for PostgreSQL separado para Ledger e Consolidation
- SQS Standard para FinancialEntryRegistered.v1
- DLQ e redrive policy
- IAM roles por componente
- Secrets Manager e/ou SSM Parameter Store
- KMS
- CloudWatch Logs, métricas, alarmes e dashboards
- X-Ray e ADOT quando aplicável
- API Gateway com AWS WAF, VPC Link/private integration e ALB interno
```

Fluxo esperado:

```text
terraform fmt -check
terraform validate
terraform plan
terraform apply
```

`apply` deve ser protegido por revisão ou aprovação manual quando o ambiente exigir.

## 3. Imagens

O pipeline deve:

```text
- executar build e testes
- gerar tags por commit SHA e release
- autenticar no ECR por OIDC/role AWS
- publicar imagens das APIs e workers
- registrar digest das imagens usadas no deploy
```

## 4. Deploy ECS

O deploy deve atualizar task definitions e services ECS para:

```text
- Ledger.Api
- Ledger.OutboxPublisher
- Consolidation.Worker
- Consolidation.Api
```

Cada componente deve receber apenas os secrets, parâmetros e permissões necessários.

## 5. Migrations

EF Core 8.0.11 (versão efetivamente usada neste repositório) **não** tem o
locking automático de migração introduzido apenas no EF Core 9 - o
`__EFMigrationsHistory` sozinho não é um lock distribuído, e grupos de
concorrência do GitHub Actions são apenas orquestração, nunca uma garantia
de exclusão mútua em nível de banco. Por isso a exclusão mútua real é
obtida via **advisory lock de sessão do PostgreSQL**
(`pg_try_advisory_lock`/`pg_advisory_unlock`, chaves distintas por
fronteira - `src/Migrations/MigrationRunner/PostgresMigrationLock.cs`),
mantido pela MESMA conexão (`Pooling=false`, para que o fechamento da
conexão realmente libere o lock no backend) que também executa a
migração via `UseNpgsql(connection, contextOwnsConnection: false)`.

Execução real (nunca automática nas APIs/Publisher/Worker no startup):

```text
- artefato dedicado: src/Migrations/MigrationRunner (console app, imagem
  própria, publicado no ECR como componente operacional distinto)
- executado como task ECS/Fargate one-off ("aws ecs run-task"), nunca
  como aws_ecs_service - ver infra/terraform/modules/ecs-migration-task
- uma fronteira por invocação: Ledger ou Consolidation (--boundary),
  cada uma com seu próprio banco, secret e advisory lock key
- comandos: "migrate" (automático, só aplica migrations
  MigrationPhase.Expand pendentes) e "contract" (manual, protegido,
  exige --approved-by e --compatibility-window-closed - nunca invocado
  por nenhum workflow automatizado)
- ordem real nos 3 workflows de deploy: validar manifesto -> registrar a
  task definition de migração -> "migrate" Ledger -> aguardar exitCode=0
  -> "migrate" Consolidation -> aguardar exitCode=0 -> só então
  atualizar os 4 serviços de aplicação (ver scripts/ci/run-migration-task.sh)
- qualquer exitCode != 0 bloqueia o deploy/promoção imediatamente - os
  serviços de aplicação nunca são atualizados com uma migração
  pendente/falha
```

Classificação EXPAND/BACKFILL/CONTRACT é machine-checkable via o atributo
real `[MigrationPhase(...)]` (`src/BancoCarrefour.Contracts/Migrations`),
nunca inferida do nome do arquivo - ver
`scripts/ci/validate-migration-governance.sh`. CONTRACT é sempre uma
operação separada e explicitamente aprovada, nunca parte do caminho
automático de deploy.

### 5.1 Observabilidade da migração

Duas fontes reais de sinal (`infra/terraform/modules/ecs-migration-task/observability.tf`),
nenhuma métrica fabricada:

```text
- falha dentro da aplicação (comando "migrate"): um metric filter no log
  group de aplicação transforma os eventos StructuredLog reais que
  representam falha (migration.failed, migration.configuration_error,
  migration.refused_unapproved_phase, migration.unhandled_error,
  migration.usage_error - nunca contract_failed/backfill_failed, que só
  um operador manual observa diretamente) num métrico customizado
  (BancoCarrefour/MigrationRunner.MigrationRunnerFailures), com um
  alarme CloudWatch sobre ele
- falha ANTES da aplicação rodar (imagem inválida, capacidade Fargate
  insuficiente, rede): nenhum log da aplicação existe nesse caso - uma
  regra EventBridge captura o evento nativo "ECS Task State Change" do
  próprio ECS com stopCode=TaskFailedToStart, escopada a
  group=family:<família desta task definition> (nunca a outros
  workloads do mesmo cluster), com um alarme CloudWatch sobre a métrica
  nativa AWS/Events.MatchedEvents dessa regra
```

Ambos os alarmes usam a mesma fábrica genérica `deployment-alarms` já
usada pelos 4 workloads de negócio - nenhum mecanismo de alarme paralelo.
`alarm_actions` (ARNs de notificação, ex.: tópico SNS) é opcional e vazio
por padrão, a mesma convenção já usada nos demais alarmes deste
repositório: o alarme existe como recurso CloudWatch real e consultável,
a decisão de para onde notificar fica a critério do operador do
ambiente.

## 6. Smoke tests

Após o deploy:

```text
- validar health/readiness das APIs
- registrar lançamento em Ledger.Api
- confirmar evento publicado
- confirmar consumo pelo Consolidation.Worker
- consultar DailyBalance na Consolidation.Api
- validar logs, traces e métricas
- validar alarmes de SQS/DLQ e backlog
```

## 7. Rollback

Rollback mínimo:

```text
- reverter service ECS para task definition anterior
- usar imagem anterior registrada por digest/tag
- pausar consumo se houver falha de projeção
- preservar mensagens em SQS/DLQ para investigação
- não destruir bancos ou filas como rollback operacional
```

Mudanças Terraform devem ter plano de reversão específico. `destroy` não deve ser usado como rollback de produção.

### 7.1 Os 3 cenários reais de rollback

O rollback de **aplicação** (imagem/task definition) e o de **banco de
dados** (migration) nunca são a mesma operação neste repositório. Existem
exatamente 3 cenários, cada um com um comportamento exigido distinto:

**(A) Rollback de aplicação após EXPAND bem-sucedido.**
A migração `MigrationPhase.Expand` da release atual já foi aplicada com
sucesso (aditiva, retrocompatível) e o problema está no código da
aplicação, não no schema. `rollback-production.yml` restaura os 4 digests
de imagem da release anterior via `terraform apply` (nunca
`aws ecs update-service` fora do Terraform, para não divergir do state) e
**nunca toca o banco de dados** - nenhuma migração é revertida, nenhum
`contract`/`migrate` é invocado. Isso só é seguro porque a versão anterior
do app continua funcionando contra o schema atual (mais novo): a garantia
vem inteiramente da disciplina EXPAND-primeiro, não de uma lógica de
rollback de schema. Pré-condição operacional: nenhum `contract` pode ter
sido executado manualmente entre a release anterior e a atual removendo
algo que a versão anterior do app ainda referencia - se isso ocorreu, o
rollback de aplicação para essa release deixa de ser seguro e exige
avaliação manual antes de qualquer `terraform apply`.

**(B) Falha de migração antes do deploy de aplicação.**
A task ECS one-off de migração (`migrate`) falha - por timeout de
advisory lock, erro de SQL ou qualquer outra causa - antes que os 4
serviços de aplicação sejam atualizados. `scripts/ci/run-migration-task.sh`
já bloqueia o workflow nesse ponto (`exitCode != 0` → `exit 1`), então o
`terraform apply` dos serviços de aplicação nunca chega a rodar - os
serviços continuam na release anterior, servindo contra o estado de
schema que existia antes da tentativa (ou um estado intermediário, porém
consistente, já que cada migração do EF Core roda em sua própria
transação). **Não há papel para `rollback-production.yml` neste
cenário** - não existe nada para reverter no lado da aplicação. A
recuperação é corrigir a causa raiz e reexecutar `migrate`, que é
idempotente (`__EFMigrationsHistory` garante que apenas as migrações
pendentes restantes são aplicadas).

**(C) Falha de CONTRACT.**
`contract` é um procedimento manual, separado e protegido (exige
`--approved-by` e `--compatibility-window-closed`), nunca invocado por
nenhum dos 3 workflows automatizados de deploy nem por
`rollback-production.yml`. Uma falha de `contract` (por definição, uma
operação potencialmente destrutiva - `DropColumn`/`DropTable`/etc.) pode
deixar o schema em um estado intermediário que nem o contrato antigo nem
o novo satisfazem completamente. **Isso nunca deve ser "corrigido" via
`rollback-production.yml`**, que só troca digests de imagem e não tem
qualquer mecanismo de reversão de schema. A recuperação exige avaliação
manual de DBA (forward-fix ou restauração a partir de snapshot, conforme
o caso), fora do escopo de qualquer workflow automatizado deste
repositório.

## 8. Destroy controlado

Ambientes efêmeros podem ser destruídos apenas quando:

```text
- não houver dados necessários
- snapshots e evidências forem preservados quando exigido
- filas e DLQs tiverem sido avaliadas
- o estado Terraform estiver íntegro
- a ação tiver aprovação explícita
```

## 9. Evidência esperada

Para considerar a implantação AWS evidenciada, registrar:

```text
- commit implantado
- tags/digests das imagens no ECR
- saída de terraform plan/apply
- services ECS saudáveis
- migrations aplicadas
- smoke tests executados
- métricas e traces visíveis
- alarmes configurados
- evidência de rollback ou plano aprovado
```
