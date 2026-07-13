# Infraestrutura

Este diretorio documenta a infraestrutura como codigo prevista para a AWS como plataforma de referencia do case.

Nao ha modulos Terraform funcionais neste estado do repositorio. Esta pasta existe para registrar o escopo esperado sem criar Terraform incompleto que pareca pronto para producao.

## Escopo previsto

Quando implementado, o Terraform deve ser organizado por dominios como:

```text
- networking
- ecr
- ecs
- rds
- sqs
- iam
- secrets
- kms
- observability
- alarms
- parameters
```

## Servicos AWS de referencia

| Papel | Servico |
|---|---|
| APIs e workers | ECS Fargate. |
| Imagens | ECR. |
| Bancos | RDS for PostgreSQL, separado para Ledger e Consolidation. |
| Mensageria | SQS Standard com DLQ. |
| Exposicao HTTP | API Gateway ou ALB com AWS WAF. |
| Secrets | Secrets Manager e/ou SSM Parameter Store. |
| Criptografia | KMS. |
| Observabilidade | ADOT, CloudWatch Logs/Metrics/Alarms e X-Ray. |
| Estado Terraform | S3 backend com lock em DynamoDB, se adotado. |

## Regras

```text
- nao versionar secrets
- usar OIDC para GitHub Actions acessar AWS
- revisar terraform plan antes de apply
- proteger apply/deploy por ambiente quando aplicavel
- manter rollback por imagem anterior e task definition anterior
- nao usar terraform destroy como rollback produtivo
```

O runbook esta em `../docs/operations/runbook-implantacao-aws.md`.
