# Runbook de Implantacao AWS

Este runbook descreve a implantacao AWS de referencia do case. Ele nao afirma que a implantacao foi executada. A execucao real depende de conta AWS, permissoes, backend Terraform, imagens publicadas e validacoes operacionais.

## 1. Pre-requisitos

```text
- conta AWS e regiao definidas
- GitHub Actions com OIDC federado para AWS
- roles IAM separadas para CI/CD, Terraform e deploy
- backend Terraform em S3 com lock em DynamoDB, se adotado
- ECR criado ou provisionado por Terraform
- VPC, subnets, security groups e rotas definidos
- dominios, certificados e desenho de API Gateway ou ALB definidos
- politica de secrets, KMS, logs e retencao aprovada
```

## 2. Terraform

O Terraform deve provisionar, no minimo:

```text
- rede: VPC, subnets, rotas, security groups e endpoints quando aplicavel
- ECR para imagens das quatro unidades
- ECS Fargate para Ledger.Api, Ledger.OutboxPublisher, Consolidation.Worker e Consolidation.Api
- RDS for PostgreSQL separado para Ledger e Consolidation
- SQS Standard para EntryCreated.v1
- DLQ e redrive policy
- IAM roles por componente
- Secrets Manager e/ou SSM Parameter Store
- KMS
- CloudWatch Logs, metricas, alarmes e dashboards
- X-Ray e ADOT quando aplicavel
- API Gateway ou ALB com AWS WAF
```

Fluxo esperado:

```text
terraform fmt -check
terraform validate
terraform plan
terraform apply
```

`apply` deve ser protegido por revisao ou aprovacao manual quando o ambiente exigir.

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

Cada componente deve receber apenas os secrets, parametros e permissoes necessarios.

## 5. Migrations

As migrations devem ser executadas de forma controlada antes de liberar trafego:

```text
- Ledger Database
- Consolidation Database
```

O procedimento deve registrar versao aplicada, saida e plano de rollback compativel com a politica de dados.

## 6. Smoke tests

Apos o deploy:

```text
- validar health/readiness das APIs
- registrar lancamento em Ledger.Api
- confirmar evento publicado
- confirmar consumo pelo Consolidation.Worker
- consultar DailyBalance na Consolidation.Api
- validar logs, traces e metricas
- validar alarmes de SQS/DLQ e backlog
```

## 7. Rollback

Rollback minimo:

```text
- reverter service ECS para task definition anterior
- usar imagem anterior registrada por digest/tag
- pausar consumo se houver falha de projecao
- preservar mensagens em SQS/DLQ para investigacao
- nao destruir bancos ou filas como rollback operacional
```

Mudancas Terraform devem ter plano de reversao especifico. `destroy` nao deve ser usado como rollback de producao.

## 8. Destroy controlado

Ambientes efemeros podem ser destruidos apenas quando:

```text
- nao houver dados necessarios
- snapshots e evidencias forem preservados quando exigido
- filas e DLQs tiverem sido avaliadas
- o estado Terraform estiver integro
- a acao tiver aprovacao explicita
```

## 9. Evidencia esperada

Para considerar a implantacao AWS evidenciada, registrar:

```text
- commit implantado
- tags/digests das imagens no ECR
- saida de terraform plan/apply
- services ECS saudaveis
- migrations aplicadas
- smoke tests executados
- metricas e traces visiveis
- alarmes configurados
- evidencia de rollback ou plano aprovado
```
