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
- SQS Standard para EntryCreated.v1
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

As migrations devem ser executadas de forma controlada antes de liberar tráfego:

```text
- Ledger Database
- Consolidation Database
```

O procedimento deve registrar versão aplicada, saída e plano de rollback compatível com a política de dados.

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
