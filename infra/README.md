# Infraestrutura

Este diretório documenta a infraestrutura como código prevista para a AWS como plataforma de referência do case.

Não há módulos Terraform funcionais neste estado do repositório. Esta pasta existe para registrar o escopo esperado sem criar Terraform incompleto que pareça pronto para produção.

Decisões relacionadas: [ADR-0010](../docs/decisions/ADR-0010-execucao-local-portabilidade-cloud-e-padroes-corporativos.md) e [ADR-0015](../docs/decisions/ADR-0015-ci-cd-publicacao-imagens-e-terraform.md). O procedimento operacional está em [runbook-implantacao-aws.md](../docs/operations/runbook-implantacao-aws.md).

## Escopo previsto

Quando implementado, o Terraform deve ser organizado por domínios como:

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

## Serviços AWS de referência

| Papel | Serviço |
|---|---|
| APIs e workers | ECS Fargate. |
| Imagens | ECR. |
| Bancos | RDS for PostgreSQL, separado para Ledger e Consolidation. |
| Mensageria | SQS Standard com DLQ. |
| Exposição HTTP | API Gateway com AWS WAF, VPC Link/private integration e ALB interno. |
| Secrets | Secrets Manager e/ou SSM Parameter Store. |
| Criptografia | KMS. |
| Observabilidade | ADOT, CloudWatch Logs/Metrics/Alarms e X-Ray. |
| Estado Terraform | S3 backend com lock em DynamoDB, se adotado. |

## Regras

```text
- não versionar secrets
- usar OIDC para GitHub Actions acessar AWS
- revisar terraform plan antes de apply
- proteger apply/deploy por ambiente quando aplicável
- manter rollback por imagem anterior e task definition anterior
- não usar terraform destroy como rollback produtivo
```
