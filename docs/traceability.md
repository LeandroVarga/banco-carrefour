# Rastreabilidade de Implementação

Este documento resume o estado de implementação materializado no PR #4, sem substituir a rastreabilidade arquitetural em `docs/architecture/07-rastreabilidade.md`.

## Estado por capacidade

| Capacidade | Estado atual | Observação |
|---|---|---|
| Registro de lançamentos | Implementado no write path inicial | `POST /entries` persiste Entry, InputIdempotency e Outbox em transação local. |
| Autenticação do Ledger | Implementada para teste/dev local | JWT local, com `merchant_id` derivado do token autenticado. Hardening produtivo permanece pendente. |
| Idempotência de entrada | Implementada no Ledger | Escopo por `merchant_id + Idempotency-Key`, com fingerprint canônico e conflito para payload divergente. |
| Publicação assíncrona | Parcialmente implementada | Outbox transacional e `Ledger.OutboxPublisher` com RabbitMQ, publish confirm e mandatory routing. |
| Independência do Consolidado | Materializada no write path | `POST /entries` não depende de chamada síncrona ao Consolidado. O consumidor `Consolidation.Worker` ainda não existe. |
| Consulta do consolidado diário | Pendente | `GET /daily-balances/{businessDate}` ainda não foi implementado. |
| Projeção DailyBalance | Pendente | Ainda não há materialização, consumo idempotente por `eventId` ou `ProcessedEvents`. |
| Rebuild/reprocessamento operacional | Pendente | Estratégia documentada, mas mecanismo completo ainda não implementado. |
| Testes automatizados | Parcialmente implementados | Existem testes de contrato e integração para Ledger write path e Outbox publisher. Não há teste de carga do Consolidado. |
| CI | Implementado para validação container-first | `.github/workflows/ci.yml` executa build, testes e `git diff --check` via Docker Compose. |
| 50 RPS do Consolidado | Não validado | Requisito depende do endpoint de consulta e de teste de carga específico. |
| Observabilidade operacional | Pendente | Health/readiness/liveness, métricas, tracing e evidências operacionais completas ainda não estão prontos. |

## Pendências principais

```text
- Consolidation.Worker
- Consolidation.Api
- GET /daily-balances/{businessDate}
- DailyBalance materializada
- consumo idempotente por eventId
- reconstrução/reprocessamento operacional completo
- teste de carga e validação prática de 50 RPS
- observabilidade completa
- health/readiness/liveness
- DLQ ou política operacional equivalente
- hardening produtivo de autenticação/autorização
- deploy produtivo/IaC
```
