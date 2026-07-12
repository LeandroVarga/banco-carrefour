# Rastreabilidade de Implementação

Este documento resume o estado de implementação materializado pelos incrementos de Ledger write path e projeção do Consolidado, sem substituir a rastreabilidade arquitetural em `docs/architecture/07-rastreabilidade.md`.

## Estado por capacidade

| Capacidade | Estado atual | Observação |
|---|---|---|
| Registro de lançamentos | Implementado no write path inicial | `POST /entries` persiste Entry, InputIdempotency e Outbox em transação local. |
| Autenticação do Ledger | Implementada para teste/dev local | JWT local, com `merchant_id` derivado do token autenticado. Hardening produtivo permanece pendente. |
| Idempotência de entrada | Implementada no Ledger | Escopo por `merchant_id + Idempotency-Key`, com fingerprint canônico e conflito para payload divergente. |
| Publicação assíncrona | Implementada no fluxo inicial | Outbox transacional e `Ledger.OutboxPublisher` com RabbitMQ, publish confirm e mandatory routing. |
| Independência do Consolidado | Materializada por Outbox/RabbitMQ | `POST /entries` não depende de chamada síncrona ao Consolidado; `Consolidation.Worker` consome `EntryCreated.v1`. |
| Projeção DailyBalance | Implementada | `DailyBalance` é atualizada por `EntryCreatedProjectionProcessor` com CREDIT/DEBIT e deduplicação por `eventId` em `ProcessedEvent`. |
| Consumo RabbitMQ do Consolidado | Implementado no fluxo inicial | Sucesso, duplicado, erro de validação e JSON inválido recebem ack; erro desconhecido/transitório recebe nack com requeue. |
| Consulta do consolidado diário | Implementada | `GET /daily-balances/{businessDate}` consulta por `merchant_id` derivado do token e retorna 404 para projeção indisponível sem afirmar saldo zero. |
| Rebuild/reprocessamento operacional | Pendente/parcialmente documentado | Estratégia documentada, mas mecanismo operacional completo ainda não implementado. |
| Testes automatizados | Implementados para o incremento atual | Existem testes de contrato, persistência, Ledger write path, Outbox publisher, projeção, consumer e API do Consolidado. Não há teste de carga do Consolidado. |
| CI | Implementado para validação container-first | `.github/workflows/ci.yml` executa build, testes e `git diff --check` via Docker Compose. |
| 50 RPS do Consolidado | Não validado | O endpoint de consulta existe, mas o requisito ainda depende de teste de carga específico. |
| Observabilidade operacional | Pendente | Health/readiness/liveness, métricas, tracing e evidências operacionais completas ainda não estão prontos. |

## Pendências principais

```text
- teste de carga e validação prática de 50 RPS
- observabilidade completa
- health/readiness/liveness
- DLQ ou política operacional equivalente
- hardening produtivo de autenticação/autorização
- execução end-to-end via Compose com serviços de aplicação
- reconstrução/reprocessamento operacional completo
- deploy produtivo/IaC
```
