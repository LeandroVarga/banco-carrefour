# deploy/ecs

Este diretório documenta o modelo de contas AWS e as estratégias de
deployment por workload do alvo definitivo (ver ADR-0011 e ADR-0014, que
materializam o Terraform descrito aqui). `infra/terraform/modules/` e
`infra/terraform/environments/{development,staging,production}` **agora
contêm Terraform de rede/ECS/RDS/SQS/edge/IAM/observabilidade real e
validável** (`terraform fmt`/`validate` limpos) - **nunca aplicado**
(IaC-materializado, não provisionado; nenhuma conta AWS real foi usada).

## O que já existe no repositório (ADR-0013, materializado pelo ADR-0011/ADR-0014)

- `infra/terraform/modules/ecr` e `infra/terraform/environments/aws-reference`
  - 4 repositórios ECR, tag imutável, OIDC, IAM de publicador com menor
    privilégio. Validado com `terraform fmt`/`init`/`validate`/`plan` -
    **nunca aplicado** (nenhum recurso AWS real foi criado).
- `.github/workflows/publish-images.yml` - build once → release-qualification
  → publicação real no Amazon ECR (único registry oficial) via OIDC →
  atestação real de proveniência/SBOM associada ao digest ECR. Configurado,
  nunca executado em runner hospedado real.
- `deploy/environments/release-qualification/` - única capacidade de
  qualificação pré-publicação (Docker Compose efêmero, imagens locais
  build-once, nunca publica, nunca valida comportamento AWS real).
- `infra/terraform/modules/{network,ecs-cluster,ecs-service-api,ecs-service-worker,ecs-service-publisher,rds-postgresql,edge,observability,deployment-alarms}`
  e `infra/terraform/environments/{development,staging,production}` - ver
  ADR-0011 para o modelo de plataforma e ADR-0014 para as estratégias de
  deployment. `modules/iam` estendido com suporte a ECR/logs/SQS;
  `modules/messaging` (fila+DLQ) e `modules/secrets` reaproveitados sem
  alteração estrutural; `modules/ecr` estendido com `repository_policy`
  cross-account.

## Modelo de contas AWS (materializado, não provisionado)

Preferencialmente multi-conta: contas compartilhadas de Tooling/Artifacts
(inclui o Amazon ECR), contas de workload `development`/`staging`/`production`,
Security, Log Archive, e Network se justificado por requisitos de
conectividade. Este repositório **não cria** AWS Organizations/Control
Tower nem as contas em si - documenta as premissas e expõe account
ID/região como inputs Terraform, para que a materialização real (próximo
ciclo) apenas preencha esses inputs contra as contas reais.

Nomes de GitHub Environment aprovados: `development`, `staging`,
`production` - representam essas contas/ambientes AWS reais. Nenhum outro
nome é válido.

## Estrutura Terraform (materializada - ver ADR-0011)

```
infra/terraform/
  modules/
    network/              # VPC, subnets públicas/privadas, NAT, VPC endpoints, security groups
    ecr/                  # já existe (ADR-0013) - estendido com repository_policy cross-account
    ecs-cluster/          # cluster Fargate + Fargate Spot compartilhado
    ecs-service-api/      # Ledger.Api e Consolidation.Api (CANARY nativo do ECS)
    ecs-service-worker/   # Consolidation.Worker (capacity canary - 2 serviços)
    ecs-service-publisher/# Ledger.OutboxPublisher (rolling controlado)
    rds-postgresql/       # instância genérica, usada 2x (Ledger, Consolidation)
    edge/                 # ALB interno + VPC Link V2 (sem NLB) + API Gateway REST + WAF
    iam/                  # estendido (ecr/logs/sqs) para task+execution roles
    secrets/              # já existia (ADR-0009) - reaproveitado sem alteração
    messaging/            # já existia - fila+DLQ (cumpre a responsabilidade "sqs")
    observability/        # log groups + dashboard
    deployment-alarms/    # fábrica genérica de alarmes CloudWatch
  environments/
    aws-reference/        # já existe (ADR-0013) - ECR + OIDC, estendido com cross-account pull
    development/
    staging/
    production/
```

`terraform fmt -recursive` e `terraform validate` passam limpos nos 4
ambientes reais - nenhum `terraform apply` foi executado.

## Estratégias de deployment por workload (materializadas - ver ADR-0014)

- **Ledger.Api / Consolidation.Api** - `deployment_configuration.strategy = "CANARY"`
  nativo do ECS (confirmado suportado pelo provider `hashicorp/aws` atual
  antes de implementar - ver ADR-0014), alarmes CloudWatch (5xx,
  latência p99) com rollback automático, qualificação por digest ECR
  exato (nunca `latest`), readiness/health check antes de receber
  tráfego.
- **Consolidation.Worker** - capacity canary: serviço primário + um
  serviço canário de baixa capacidade consumindo a MESMA fila SQS,
  dimensões de métrica por versão/release (nunca weighted routing de
  fila - a fração de mensagens processadas pelo canário é aproximada e
  dirigida por capacidade relativa, nunca uma porcentagem exata como no
  ALB). Gates: profundidade de DLQ, idade da mensagem mais antiga.
  Promoção/remoção do canário via `terraform apply` conduzido pelo
  workflow de deploy.
- **Ledger.OutboxPublisher** - rolling deployment controlado
  (`deployment_minimum_healthy_percent = 100`), sem canary artificial de
  HTTP. `deployment_circuit_breaker` + alarmes de negócio (falhas de
  publicação). `stopTimeout` configurável para graceful shutdown real da
  aplicação (parar de aceitar novos claims, concluir/liberar trabalho já
  reivindicado, publicação sempre fora da transação de claim - ADR-0004).

Requisitos de compatibilidade que qualquer uma dessas estratégias exige
(migrações expand-and-contract, contratos de evento retrocompatíveis,
contratos externos versionados, evolução de schema aditiva, nenhuma
mudança destrutiva de banco com a versão antiga ainda rodando,
consumidores idempotentes, registros de Outbox compatíveis entre versões,
rollback sem exigir rollback de banco) permanecem um contrato documental -
nenhuma migração real foi executada.

## Por que a identidade de deployment é sempre qualificada pelo registry

Um digest OCI é calculado sobre o **conteúdo do manifesto do registry**,
que inclui referências de camada específicas daquele registry - publicar a
MESMA imagem em dois registries diferentes produz, tipicamente, digests
**diferentes** em cada um (confirmado empiricamente neste repositório:
`scripts/ci/test-generic-oci-publish-proof.sh` já demonstrou que a
identidade de conteúdo portável entre registries é `RootFS.Layers`, não o
digest do manifesto). Por isso a identidade de deployment é sempre
**qualificada pelo registry** (`registry/repo@digest`), nunca apenas pelo
digest isolado - um registry alternativo nunca poderia ter sido um estágio
intermediário rumo ao Amazon ECR (único registry oficial, ver ADR-0013).

## Status

Modelo de contas, Terraform e estratégias de deployment materializados e
validados (`terraform fmt`/`validate` limpos - ver ADR-0011, ADR-0014). Nenhum
`terraform apply` foi executado, nenhum recurso AWS real foi provisionado.
Execução real, workflows de deploy hospedados, canary/rollback reais e
dashboards com métricas reais permanecem para uma execução externa
futura - documentado para que essa fronteira nunca seja confundida ou
implicitamente reivindicada.
