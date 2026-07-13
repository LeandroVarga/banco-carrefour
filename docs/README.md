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

## Como navegar pela documentação

| Objetivo | Documentos recomendados |
|---|---|
| Visão geral | `README.md` → `docs/README.md` |
| Jornada arquitetural | `docs/architecture/00-jornada-arquitetural.md` |
| Contexto e requisitos | `docs/architecture/01-contexto-de-negocio.md` → `docs/architecture/02-requisitos-arquiteturais.md` |
| Blocos arquiteturais e solução | `docs/architecture/03-blocos-de-arquitetura.md` → `docs/architecture/04-blocos-de-solucao.md` |
| Arquitetura alvo | `docs/architecture/05-arquitetura-da-solucao.md` |
| Diagramas | `docs/architecture/06-diagramas.md` |
| Rastreabilidade | `docs/architecture/07-rastreabilidade.md` |
| Rastreabilidade de implementação | `docs/traceability.md` |
| Decisões arquiteturais | `docs/decisions/registro-de-decisoes.md` → ADRs relacionados |
| Segurança | `docs/security/arquitetura-de-seguranca.md` |
| Operação | `docs/operations/arquitetura-operacional.md` |
| Observabilidade e recuperação | `docs/operations/observabilidade-sli-slo-e-recuperacao.md` |
| Runbook de demonstração local | `docs/operations/runbook-demonstracao-local.md` |
| Evidências do case | `docs/operations/evidencias-do-case.md` |
| Teste de carga do Consolidado | `docs/operations/teste-de-carga-consolidado.md` |
| Custos | `docs/operations/estimativa-de-custos.md` |

## Status

| Frente | Status |
|---|---|
| Arquitetura | Documentada |
| Decisões arquiteturais | ADR-0000 até ADR-0014 criados |
| Segurança | Documentada |
| Operação e observabilidade | Documentadas, com runbook local e matriz de evidências do case |
| Estimativa de custos | Documentada |
| Implementação | Baseline local com Ledger.Api, Ledger.OutboxPublisher, Consolidation.Worker, Consolidation.Api, Outbox transacional, DailyBalance por upsert atômico, retry/DLQ confirmado antes do ack, JWT local, rate limiting básico, OpenTelemetry e Aspire Dashboard. |
| Testes | Testes de contrato e integração para Ledger, Outbox publisher, projeção, consumer, APIs, rate limiting, idempotência concorrente e validação runtime de evento; teste de carga executado separadamente. |
| Execução local | Build, testes e execução end-to-end local disponíveis via Docker Compose |
| CI | Workflow container-first criado em `.github/workflows/ci.yml` |

## Observação

Esta documentação está alinhada ao baseline local/container-first final da entrega. O estado atual implementa registro de lançamentos, Outbox transacional, publicação confiável, projeção do Consolidado, consumo idempotente, retry/DLQ com republicação confirmada antes do ack da original, consulta do `DailyBalance`, JWT local com issuer/audience/expiração, rate limiting básico local/in-memory, health checks, OpenTelemetry/Aspire local, testes automatizados e evidência de carga com 3000 requisições sustentadas planejadas e executadas, 50.02 req/s, 0% falhas, p95 5.80 ms e p99 7.51 ms.

Esse baseline está pronto para entrega do desafio técnico como execução local reproduzível. Ainda permanecem pendentes para produção real: rate limiting distribuído/produtivo, validação de capacidade em ambiente produtivo ou equivalente, validação produtiva de múltiplos workers/backlog/autoscaling, observabilidade produtiva completa, re-drive assistido da DLQ, hardening produtivo de autenticação/autorização, OIDC/TLS/mTLS/secret manager, deploy produtivo/IaC e reconstrução/reprocessamento operacional completo.

---

## Prontidão para implementação

A documentação arquitetural foi complementada com contratos e critérios de prontidão para implementação.

Itens adicionados:

```text
- contracts/openapi.yaml
- contracts/events/entry-created-v1.schema.json
- docs/architecture/08-implementation-readiness.md
- docs/decisions/ADR-0013-contratos-http-e-evento-entry-created-v1.md
- docs/decisions/ADR-0014-instrumentacao-de-observabilidade-com-opentelemetry.md
```

Esses documentos fecharam decisões necessárias antes da implementação funcional, incluindo contratos HTTP, evento assíncrono, businessDate, cutoff, idempotência, invariantes transacionais, concorrência, autenticação local testável e perfil inicial de validação de carga.

No estado atual da main, o baseline local já possui solução .NET, persistências PostgreSQL separadas, Outbox transacional, publisher RabbitMQ, worker de consolidação, APIs HTTP, testes automatizados, CI container-first, evidência local/container-first de 50 RPS, rate limiting básico local/in-memory e telemetria local com OpenTelemetry/Aspire. O Compose local sobe APIs, workers, bancos e RabbitMQ; as pendências operacionais e produtivas permanecem listadas acima.
