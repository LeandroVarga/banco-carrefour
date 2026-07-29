# Infraestrutura

Este diretório contém a infraestrutura como código da plataforma AWS de referência do case (ADR-0011), materializada e validável, e o ambiente `localstack-hobby` usado para paridade comportamental local (ADR-0010).

## Ambientes

| Ambiente | Papel | Estado |
|---|---|---|
| `localstack-hobby` | Paridade local de SQS/DLQ, Secrets Manager, SSM e KMS via LocalStack. | Aplicado localmente contra LocalStack. |
| `aws-reference` | Conta Artifacts/Tooling: Amazon ECR (4 repositórios, um por workload de negócio), identidade OIDC do GitHub Actions, role de publicação de menor privilégio. | Validado com `fmt`/`init`/`validate`/`plan`; nunca aplicado contra uma conta AWS real. |
| `development` | Conta de workload isolada: rede, ECS/Fargate, RDS (Ledger e Consolidation), SQS/DLQ, borda, IAM, secrets/parâmetros/KMS, observabilidade. | Validado com `fmt`/`init`/`validate`/`plan`; nunca aplicado. |
| `staging` | Mesma estrutura de `development`, isolada como conta própria. | Validado com `fmt`/`init`/`validate`/`plan`; nunca aplicado. |
| `production` | Mesma estrutura, com Multi-AZ, proteção contra deleção e retenção de backup do RDS obrigatórios. | Validado com `fmt`/`init`/`validate`/`plan`; nunca aplicado. |

Nenhum `terraform apply` foi executado contra uma conta AWS real em nenhum ambiente; nenhum recurso AWS real foi provisionado por este repositório.

## Módulos (`infra/terraform/modules`)

```text
- network            VPC, subnets privadas, security groups por camada (ADR-0011)
- edge                WAF, API Gateway REST, VPC Link V2, ALB interno (ADR-0008)
- ecs-cluster          cluster ECS/Fargate compartilhado por ambiente
- ecs-service-api      Ledger.Api e Consolidation.Api, CANARY nativo do ECS (ADR-0014)
- ecs-service-worker   Consolidation.Worker, capacity canary de dois serviços (ADR-0014)
- ecs-service-publisher Ledger.OutboxPublisher, rolling controlado (ADR-0014)
- ecs-migration-task   task ECS one-off do MigrationRunner (ADR-0015)
- rds-postgresql       instância RDS, usada de forma independente para Ledger e Consolidation (ADR-0002)
- messaging            SQS Standard + DLQ (ADR-0004)
- iam                  roles de task/execution por workload, isolamento de migração (ADR-0009)
- ecr                  repositórios, tag imutável, scan-on-push, lifecycle policy (ADR-0013)
- secrets              Secrets Manager (metadados, sem valor no state) (ADR-0009)
- parameters           SSM Parameter Store, configuração não sensível (ADR-0009)
- kms                  chaves e aliases (ADR-0009)
- observability        log groups e dashboard por ambiente (ADR-0012)
- deployment-alarms    fábrica genérica de alarmes CloudWatch (ADR-0012, ADR-0014)
```

## Serviços AWS de referência

| Papel | Serviço |
|---|---|
| APIs e workers | Amazon ECS/Fargate. |
| Imagens | Amazon ECR. |
| Bancos | Amazon RDS for PostgreSQL, instâncias independentes para Ledger e Consolidation. |
| Mensageria | Amazon SQS Standard com DLQ. |
| Exposição HTTP | Amazon API Gateway REST com AWS WAF, VPC Link V2 e ALB interno — sem NLB intermediário. |
| Secrets | AWS Secrets Manager e AWS SSM Parameter Store. |
| Criptografia | AWS KMS. |
| Observabilidade | ADOT, Amazon CloudWatch Logs/Metrics/Alarms e AWS X-Ray. |
| Identidade de deploy | GitHub OIDC, uma role por ambiente. |

## Regras

```text
- não versionar secrets
- usar OIDC para GitHub Actions acessar AWS, nunca credencial estática
- revisar terraform plan antes de qualquer apply
- proteger apply/deploy por ambiente via GitHub Environment
- manter rollback por imagem anterior e task definition anterior, nunca reconstruindo o artefato
- não usar terraform destroy como rollback produtivo
- nenhum recurso de dado ou runtime compartilhado entre Development, Staging e Production
```

Decisões relacionadas: [ADR-0002](../docs/decisions/ADR-0002-persistencia-postgresql-independente-por-fronteira.md), [ADR-0008](../docs/decisions/ADR-0008-protecao-de-borda-e-conectividade-privada.md), [ADR-0009](../docs/decisions/ADR-0009-menor-privilegio-secrets-e-criptografia.md), [ADR-0010](../docs/decisions/ADR-0010-execucao-local-e-paridade-comportamental.md), [ADR-0011](../docs/decisions/ADR-0011-plataforma-aws-e-isolamento-de-ambientes.md), [ADR-0012](../docs/decisions/ADR-0012-observabilidade-e-objetivos-operacionais.md), [ADR-0013](../docs/decisions/ADR-0013-integridade-de-release-e-software-supply-chain.md), [ADR-0014](../docs/decisions/ADR-0014-promocao-deployment-e-rollback-por-workload.md), [ADR-0015](../docs/decisions/ADR-0015-governanca-de-migrations-de-banco-de-dados.md). O procedimento operacional está em [runbook-implantacao-aws.md](../docs/operations/runbook-implantacao-aws.md).
