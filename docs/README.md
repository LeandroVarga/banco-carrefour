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
| Implementação | Ledger write path inicial e projeção do Consolidado implementados |
| Testes | Testes de contrato e integração para Ledger write path, Outbox publisher, projeção, consumer e API do Consolidado criados |
| Execução local | Build, testes e execução end-to-end local disponíveis via Docker Compose |
| CI | Workflow container-first criado em `.github/workflows/ci.yml` |

## Observação

Esta documentação segue em evolução até evidências operacionais completas e validação de capacidade em ambiente produtivo ou equivalente.

O PR #4 materializa o caminho inicial de escrita do Ledger. O incremento atual do Consolidado materializa `Consolidation.Persistence`, `DailyBalance`, `ProcessedEvent`, `EntryCreatedProjectionProcessor`, `Consolidation.Worker`, `Consolidation.Api` e `GET /daily-balances/{businessDate}`.

O teste de carga local/container-first do Consolidado validou 50 RPS na janela sustentada, health/readiness/liveness básicos estão implementados nas APIs HTTP e a execução end-to-end local via Docker Compose inclui APIs, workers, bancos e RabbitMQ. Ainda permanecem pendentes validação de capacidade em ambiente produtivo ou equivalente, observabilidade completa, DLQ completa, hardening produtivo de autenticação/autorização, deploy produtivo/IaC e reconstrução/reprocessamento operacional completo.

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

No estado atual, o Ledger write path inicial já possui baseline .NET, persistência PostgreSQL, Outbox transacional, publisher RabbitMQ, testes automatizados e CI container-first. O Consolidado já possui persistência separada, projeção materializada, consumo RabbitMQ, API de consulta, testes de integração e evidência local/container-first de 50 RPS. O Compose local já sobe as APIs, workers, bancos e RabbitMQ, mas a solução ainda depende das pendências operacionais e produtivas listadas acima.
