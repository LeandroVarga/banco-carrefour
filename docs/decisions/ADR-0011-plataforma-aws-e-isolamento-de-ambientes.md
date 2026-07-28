---
adr_id: ADR-0011
titulo: Plataforma AWS e isolamento de ambientes
status: Aceita
categoria: Plataforma
altitude_decisoria: Estrutural
data: 2026-07-28
responsavel: Arquitetura de Soluções
---

# ADR-0011 — Plataforma AWS e isolamento de ambientes

## 1. Contexto

A referência AWS precisa suportar múltiplos ambientes de workload isolados, um ponto central de distribuição de imagens, e um modelo de contas compatível com uma organização regulada — sem que este repositório crie a landing zone corporativa nem provisione recursos reais.

## 2. Pergunta arquitetural

Como a plataforma AWS de referência é organizada, isolada, provisionada e operada entre contas e ambientes?

## 3. Decisão

O modelo de contas separa: uma conta Artifacts/Tooling (`infra/terraform/environments/aws-reference`), com o Amazon ECR como registry único, o provedor OIDC do GitHub Actions e a role de publicação; três contas de workload (`development`, `staging`, `production`), cada uma com sua própria VPC, subnets privadas, ECS/Fargate, RDS, SQS, borda e IAM — nenhum recurso de dado ou runtime compartilhado entre ambientes. Contas de Security, Log Archive e Network são assumidas como responsabilidade da landing zone corporativa, não criadas por este Terraform.

O ECR central usa uma resource policy cross-account que permite às contas de workload apenas `BatchGetImage`/`GetDownloadUrlForLayer`/`BatchCheckLayerAvailability` — nunca push, nunca gerenciamento. Cada ambiente de workload tem: rede própria (módulo `network`, com o security group do ALB restrito ao security group do VPC Link, nunca CIDR amplo), cluster ECS/Fargate (`ecs-cluster`) e os quatro workloads de negócio (`ecs-service-api`, `ecs-service-worker`, `ecs-service-publisher`), duas instâncias RDS PostgreSQL independentes por fronteira (`rds-postgresql`, Multi-AZ, proteção contra deleção e retenção de backup obrigatórios em Production, opcionais/reduzidos em Staging, mínimos em Development), SQS com DLQ (`messaging`), a cadeia de borda completa (`edge`, ADR-0008), IAM por workload (`iam`, ADR-0009), Secrets Manager/SSM/KMS (`secrets`, `parameters`, `kms`, ADR-0009) e observabilidade (`observability`, `deployment-alarms`, ADR-0012). Uma role de deploy por ambiente usa GitHub OIDC condicionado ao ambiente correspondente (`token.actions.githubusercontent.com:sub = repo:<repo>:environment:<development|staging|production>`), nunca um branch ou ref genérico.

`terraform fmt`/`validate` são executados e passam limpos nos cinco ambientes reais (`aws-reference`, `development`, `staging`, `production`, `localstack-hobby`). Nenhum `terraform apply` foi executado contra uma conta AWS real — toda a plataforma está materializada como código validável, não provisionada.

## 4. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar |
|---|---|---|
| Uma única conta AWS para todos os ambientes | Development, Staging e Production na mesma conta. | Concentra o raio de impacto de qualquer falha ou comprometimento em um único blast radius. |
| Um registry de imagens compartilhado por conta de workload | Cada conta com seu próprio ECR. | Fragmenta a governança de imagem e duplica a superfície de publicação sem necessidade. |
| Recursos de dado (RDS, SQS) compartilhados entre ambientes | Um único RDS ou uma única fila usada por Development/Staging/Production. | Elimina o isolamento de falha e de dado entre ambientes. |
| Modelo multi-conta com Artifacts/Tooling central e contas de workload isoladas | ECR e OIDC centrais; rede, ECS, RDS, SQS, borda e IAM próprios por ambiente. | Alternativa adotada. Compatível com uma organização regulada, sem exigir landing zone própria deste repositório. |

## 5. Trade-offs

O modelo multi-conta exige mais Terraform e mais coordenação entre contas do que uma única conta compartilhada, em troca de isolamento real de blast radius e de conformidade com um modelo corporativo de contas segregadas.

## 6. Consequências

Uma release normal nunca precisa reaplicar a stack completa: os módulos de rede, RDS e borda são estáveis entre releases — apenas os módulos `ecs-service-*` (imagem/task definition) mudam a cada promoção (ver ADR-0014).

## 7. Guardrails

- Nenhum recurso de dado ou runtime é compartilhado entre Development, Staging e Production.
- O ECR central nunca concede push às contas de workload — apenas pull.
- Account IDs, ARNs de role e certificados são sempre `variable` sem `default`, nunca hardcoded.
- Nenhum `terraform apply` é executado contra uma conta AWS real por este repositório.

## 8. Risco arquitetural evitado

Uma implementação futura não deve colapsar todos os ambientes e workloads em um único blast radius, compartilhar recursos de dado entre contas de workload, ou apresentar Terraform validado como já provisionado em uma conta AWS real.

## 9. ASRs relacionados

ASR-002/ASR-003 (50 RPS, ≤5% de falha), ASR-009 (acesso autenticado e autorizado por comerciante), ASR-010 (fluxo observável).

## 10. ABBs e SBBs relacionados

ABB-013 (Observabilidade do Fluxo), ABB-015 (Segurança de Acesso), ABB-016 (Controle de Comunicação entre Serviços); SBB-002/SBB-009 (Ledger/Consolidation Database), SBB-019 (Configuration and Secrets).

## 11. Evidências de implementação

`infra/terraform/environments/{aws-reference,development,staging,production,localstack-hobby}`, `infra/terraform/modules/{network,ecs-cluster,edge,iam,ecr,rds-postgresql,messaging,secrets,parameters,kms,observability,deployment-alarms}`, `tests/Architecture.Tests/AwsPlatformGovernanceArchitectureTests.cs`.

## 12. ADRs relacionados

ADR-0002 (persistência PostgreSQL independente por fronteira), ADR-0008 (proteção de borda e conectividade privada), ADR-0009 (menor privilégio, secrets e criptografia), ADR-0012 (observabilidade e objetivos operacionais), ADR-0013 (integridade de release e software supply chain), ADR-0014 (promoção, deployment e rollback por workload).
