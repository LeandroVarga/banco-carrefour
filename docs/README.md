# Documentação

Esta pasta contém a documentação arquitetural do desafio técnico Banco Carrefour.

A estrutura segue a organização exigida pelo desafio:

```text
docs/
  architecture/
  security/
  decisions/
  operations/
```

## Linha de raciocínio

A documentação segue a seguinte cadeia:

```text
contexto de negócio
-> requisitos arquiteturais
-> ASRs
-> ABBs
-> ADRs
-> SBBs
-> AWS como referência do case
-> arquitetura alvo
-> segurança
-> operação
-> implementação
-> testes
```

## Pastas

| Pasta | Conteúdo |
|---|---|
| `architecture/` | Contexto, requisitos, ASRs, ABBs, SBBs, arquitetura alvo, diagramas e rastreabilidade. |
| `security/` | Autenticação, autorização, proteção de APIs, dados, secrets e comunicação segura. |
| `decisions/` | ADRs e registro consolidado de decisões arquiteturais. |
| `operations/` | Arquitetura operacional, observabilidade, SLIs, SLOs, recuperação e custos. |
| `../infra/` | Referência documental de IaC para AWS; não contém Terraform funcional aplicado. |

## Como navegar pela documentação

| Objetivo | Documentos recomendados |
|---|---|
| Visão geral | [README.md](../README.md) → [docs/README.md](README.md) |
| Jornada arquitetural | [00-jornada-arquitetural.md](architecture/00-jornada-arquitetural.md) |
| Contexto e requisitos | [01-contexto-de-negocio.md](architecture/01-contexto-de-negocio.md) → [02-requisitos-arquiteturais.md](architecture/02-requisitos-arquiteturais.md) |
| Blocos arquiteturais e solução | [03-blocos-de-arquitetura.md](architecture/03-blocos-de-arquitetura.md) → [04-blocos-de-solucao.md](architecture/04-blocos-de-solucao.md) |
| Arquitetura alvo | [05-arquitetura-da-solucao.md](architecture/05-arquitetura-da-solucao.md) |
| Diagramas | [06-diagramas.md](architecture/06-diagramas.md) |
| Rastreabilidade | [07-rastreabilidade.md](architecture/07-rastreabilidade.md) |
| Rastreabilidade de implementação | [traceability.md](traceability.md) |
| Decisões arquiteturais | [registro-de-decisoes.md](decisions/registro-de-decisoes.md) → ADRs relacionados |
| Segurança | [arquitetura-de-seguranca.md](security/arquitetura-de-seguranca.md) |
| Operação | [arquitetura-operacional.md](operations/arquitetura-operacional.md) |
| Observabilidade e recuperação | [observabilidade-sli-slo-e-recuperacao.md](operations/observabilidade-sli-slo-e-recuperacao.md) |
| Runbook de demonstração local | [runbook-demonstracao-local.md](operations/runbook-demonstracao-local.md) |
| Runbook de implantação AWS | [runbook-implantacao-aws.md](operations/runbook-implantacao-aws.md) |
| Evidências do case | [evidencias-do-case.md](operations/evidencias-do-case.md) |
| Teste de carga do Consolidado | [teste-de-carga-consolidado.md](operations/teste-de-carga-consolidado.md) |
| Custos | [estimativa-de-custos.md](operations/estimativa-de-custos.md) |

## Status

| Frente | Status |
|---|---|
| Arquitetura | Documentada |
| Decisões arquiteturais | ADR-0000 até ADR-0015 criados |
| Segurança | Documentada |
| Operação e observabilidade | Documentadas, com runbook local e matriz de evidências do case |
| Estimativa de custos | Documentada |
| Implementação | Baseline local com Ledger.Api, Ledger.OutboxPublisher, Consolidation.Worker, Consolidation.Api, Outbox transacional, DailyBalance por upsert atômico, retry/DLQ confirmado antes do ack, JWT local, rate limiting básico, OpenTelemetry e Aspire Dashboard. |
| Implantação AWS de referência | ADR-0010 e ADR-0015 documentam ECS Fargate, ECR, RDS PostgreSQL, SQS/DLQ, Secrets Manager/SSM, KMS, CloudWatch, X-Ray, ADOT, Terraform e GitHub Actions com OIDC. |
| Testes | Testes de contrato e integração para Ledger, Outbox publisher, projeção, consumer, APIs, rate limiting, idempotência concorrente e validação runtime de evento; teste de carga executado separadamente. |
| Execução local | Build, testes e execução end-to-end local disponíveis via Docker Compose |
| CI | Workflow container-first criado em `.github/workflows/ci.yml` |

## Observação

Esta documentação está alinhada ao baseline local/container-first final da entrega. O estado atual implementa registro de lançamentos, Outbox transacional, publicação confiável, projeção do Consolidado, consumo idempotente, retry/DLQ com republicação confirmada antes do ack da original, consulta do `DailyBalance`, JWT local com issuer/audience/expiração, rate limiting básico local/in-memory, health checks, OpenTelemetry/Aspire local, testes automatizados e evidência de carga com 3000 requisições sustentadas planejadas e executadas, 50.02 req/s, 0% falhas, p95 5.80 ms e p99 7.51 ms.

Esse baseline está pronto para entrega do desafio técnico como execução local reproduzível. Ainda permanecem pendentes para produção real: rate limiting distribuído/produtivo, validação de capacidade em ambiente produtivo ou equivalente, validação produtiva de múltiplos workers/backlog/autoscaling, observabilidade produtiva completa, re-drive assistido da DLQ, hardening produtivo de autenticação/autorização, OIDC/TLS/mTLS/secret manager, publicação de imagens, Terraform aplicado, deploy AWS, smoke tests AWS e reconstrução/reprocessamento operacional completo.

---

## Prontidão para implementação

A documentação arquitetural foi complementada com contratos e critérios de prontidão para implementação.

Itens adicionados:

- `contracts/openapi.yaml`
- `contracts/events/entry-created-v1.schema.json`
- [08-implementation-readiness.md](architecture/08-implementation-readiness.md)
- [ADR-0013-contratos-http-e-evento-entry-created-v1.md](decisions/ADR-0013-contratos-http-e-evento-entry-created-v1.md)
- [ADR-0014-instrumentacao-de-observabilidade-com-opentelemetry.md](decisions/ADR-0014-instrumentacao-de-observabilidade-com-opentelemetry.md)
- [ADR-0015-ci-cd-publicacao-imagens-e-terraform.md](decisions/ADR-0015-ci-cd-publicacao-imagens-e-terraform.md)

Esses documentos fecharam decisões necessárias antes da implementação funcional, incluindo contratos HTTP, evento assíncrono, businessDate, cutoff, idempotência, invariantes transacionais, concorrência, autenticação local testável e perfil inicial de validação de carga.

No estado atual da main, o baseline local já possui solução .NET, persistências PostgreSQL separadas, Outbox transacional, publisher RabbitMQ, worker de consolidação, APIs HTTP, testes automatizados, CI container-first, evidência local/container-first de 50 RPS, rate limiting básico local/in-memory e telemetria local com OpenTelemetry/Aspire. O Compose local sobe APIs, workers, bancos e RabbitMQ; a implantação AWS permanece como referência documental, não como execução realizada.
