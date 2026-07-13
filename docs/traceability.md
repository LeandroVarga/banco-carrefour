# Rastreabilidade de Implementação

Este documento resume o estado de implementação materializado pelos incrementos de Ledger write path e projeção do Consolidado, sem substituir a rastreabilidade arquitetural em `docs/architecture/07-rastreabilidade.md`.

## Estado por capacidade

| Capacidade | Estado atual | Observação |
|---|---|---|
| Registro de lançamentos | Implementado no write path inicial | `POST /entries` persiste Entry, InputIdempotency e Outbox em transação local. |
| Autenticação do Ledger | Implementada para teste/dev local | JWT local valida assinatura, expiração, issuer e audience, com `merchant_id` derivado do token autenticado. Hardening produtivo permanece pendente. |
| Idempotência de entrada | Implementada no Ledger | Escopo por `merchant_id + Idempotency-Key`, com fingerprint canônico e conflito para payload divergente. |
| Publicação assíncrona | Implementada no fluxo inicial | Outbox transacional e `Ledger.OutboxPublisher` com RabbitMQ, publish confirm e mandatory routing. |
| Independência do Consolidado | Materializada por Outbox/RabbitMQ | `POST /entries` não depende de chamada síncrona ao Consolidado; `Consolidation.Worker` consome `EntryCreated.v1`. |
| Projeção DailyBalance | Implementada | `DailyBalance` é atualizada por `EntryCreatedProjectionProcessor` com upsert atômico PostgreSQL para CREDIT/DEBIT e deduplicação por `eventId` em `ProcessedEvent`. |
| Consumo RabbitMQ do Consolidado | Implementado no fluxo inicial | Sucesso e duplicado recebem ack; erro de validação e JSON inválido são encaminhados para DLQ; erro desconhecido/transitório recebe retry local finito antes de DLQ. Republicação para retry/DLQ usa mandatory routing e publisher confirms antes do ack da original. |
| DLQ básica do Consolidado | Implementada localmente | `Consolidation.Worker` declara `consolidation.dlx` e `consolidation.entry-created.dlq`; mensagens inválidas, semanticamente irrecuperáveis ou com retries excedidos são isoladas para inspeção operacional após publicação confirmada e roteada. |
| Consulta do consolidado diário | Implementada | `GET /daily-balances/{businessDate}` consulta por `merchant_id` derivado do token e retorna 404 para projeção indisponível sem afirmar saldo zero. |
| Rebuild/reprocessamento operacional | Pendente/parcialmente documentado | Estratégia documentada, mas mecanismo operacional completo ainda não implementado. |
| Testes automatizados | Implementados no baseline local atual | Existem testes de contrato, persistência, Ledger write path, Outbox publisher, projeção, consumer, APIs, idempotência concorrente e validação runtime de evento. O teste de carga do Consolidado foi criado e executado localmente/container-first. |
| CI | Implementado para validação container-first | `.github/workflows/ci.yml` executa build, testes e `git diff --check` via Docker Compose. |
| Execução end-to-end local via Compose | Implementada | `docker-compose.yml` inclui APIs, workers, bancos, RabbitMQ e serviços efêmeros de migration para schema local. |
| Geração de JWT local container-first | Implementada | `docker compose run --rm local-jwt --merchant-id merchant-001` gera token HS256 local com `iss`, `aud`, `exp` e `merchant_id` compatível com as APIs sem exigir PowerShell 7, .NET SDK local, Python, Node, OpenSSL ou ferramenta externa de JWT. |
| 50 RPS do Consolidado | Validado localmente/container-first | Execução local usa JWT com issuer/audience e atingiu 3000 requisições planejadas/executadas na janela sustentada, 50.02 req/s, 0% falhas, p95 5.80 ms e p99 7.51 ms, validando throughput mínimo observado de 50 RPS. Validação produtiva permanece pendente. |
| Health/readiness/liveness das APIs HTTP | Implementado | `Ledger.Api` e `Consolidation.Api` expõem `GET /health/live` e `GET /health/ready`; readiness valida o PostgreSQL da respectiva API e retorna 503 quando indisponível. |
| Rate limiting básico das APIs HTTP | Implementado localmente | `POST /entries` e `GET /daily-balances/{businessDate}` usam rate limiting local/in-memory, retornam 429 no padrão de erro da API e preservam `correlationId` quando informado. Endpoints de health não aplicam rate limit. Rate limiting distribuído/produtivo permanece pendente. |
| Instrumentação OpenTelemetry | Implementada como baseline local | As quatro unidades implantáveis usam `ILogger`, `ActivitySource`, `Meter` e OTLP exporter configurável; `docker-compose.yml` inclui Aspire Dashboard para demonstração local. |
| Runbook final de demonstração local | Documentado | `docs/operations/runbook-demonstracao-local.md` consolida pré-requisitos, subida, health, fluxo end-to-end, idempotência, DLQ/retry, observabilidade, testes e limpeza local. |
| Evidências finais do case | Documentado | `docs/operations/evidencias-do-case.md` mapeia requisitos do desafio contra evidências do repositório, status e limitações sem afirmar prontidão produtiva. |
| DLQ e retry do Consolidado | Implementado localmente e documentado | `Consolidation.Worker` isola JSON inválido, evento semanticamente inválido e retries excedidos em DLQ local; erro desconhecido/transitório usa retry local finito com `x-retry-count`; ack da original fica condicionado à republicação confirmada e roteada. Reprocessamento assistido permanece pendente. |
| Observabilidade operacional completa | Pendente | Plataforma produtiva, dashboards produtivos, alertas, retenção centralizada, evidências operacionais completas e sinais aprofundados de workers, Outbox e broker ainda não estão prontos. |

## Pendências principais

```text
- validação de capacidade em ambiente produtivo ou equivalente declarado
- rate limiting distribuído/produtivo em API Gateway, WAF, ingress ou service mesh
- observabilidade produtiva completa
- dashboards produtivos, alertas produtivos e retenção centralizada de logs
- backoff avançado e operação produtiva de mensagens isoladas
- hardening produtivo de autenticação/autorização
- reconstrução/reprocessamento operacional completo
- re-drive assistido da DLQ
- multi-publisher seguro para Ledger.OutboxPublisher
- validação produtiva de múltiplos workers, backlog e autoscaling para Consolidation.Worker
- deploy produtivo/IaC
```
