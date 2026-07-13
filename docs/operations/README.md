# Operações

Esta pasta contém a documentação operacional da solução.

Documentos:

- `arquitetura-operacional.md`
- `observabilidade-sli-slo-e-recuperacao.md`
- `estimativa-de-custos.md`
- `teste-de-carga-consolidado.md`
- `runbook-demonstracao-local.md`
- `evidencias-do-case.md`

A documentação operacional cobre implantação, execução local, health checks, escalabilidade, recuperação, retries, isolamento de mensagens, reprocessamento, reconstrução, observabilidade, SLIs, SLOs, custos e limites entre ambiente local e produção.

Para avaliação do desafio técnico, use primeiro:

| Objetivo | Documento |
|---|---|
| Executar a demonstração local completa | `runbook-demonstracao-local.md` |
| Mapear requisitos do case contra evidências | `evidencias-do-case.md` |
| Conferir evidência de 50 RPS local/container-first | `teste-de-carga-consolidado.md` |
| Entender limites de observabilidade e recuperação | `observabilidade-sli-slo-e-recuperacao.md` |

## Estado operacional atual

O estado atual disponibiliza validação container-first para Ledger e Consolidado, com PostgreSQL do Ledger, PostgreSQL do Consolidado, RabbitMQ, build, testes, CI e execução end-to-end local via Docker Compose.

Comandos locais existentes:

```powershell
docker compose up -d ledger-postgres consolidation-postgres rabbitmq
docker compose run --rm dotnet-sdk dotnet build
docker compose run --rm dotnet-sdk dotnet test
```

Aplicar migrations explicitamente:

```powershell
docker compose run --rm ledger-migrations
docker compose run --rm consolidation-migrations
```

Subir a solução local completa com visualização local de telemetria:

```powershell
docker compose up -d --build ledger-api ledger-outbox-publisher consolidation-worker consolidation-api aspire-dashboard
```

Serviços expostos localmente:

| Serviço | URL local |
|---|---|
| Ledger.Api | `http://localhost:8080` |
| Consolidation.Api | `http://localhost:8081` |
| RabbitMQ Management | `http://localhost:15672` |
| Aspire Dashboard | `http://localhost:18888` |

As APIs usam `8080` dentro dos containers. O Compose publica a `Consolidation.Api` em `localhost:8081` no host.
O RabbitMQ Management usa as credenciais locais `ledger` / `ledger`.
O Aspire Dashboard é usado apenas como backend local/dev para demonstrar logs, traces e métricas recebidos por OTLP. No host, `4317` encaminha para OTLP/gRPC e `4318` encaminha para OTLP/HTTP.

Fila operacional básica do Consolidado:

| Finalidade | Exchange/Fila |
|---|---|
| Exchange de eventos do Ledger | `ledger.events` |
| Fila principal do Consolidado | `consolidation.entry-created` |
| Dead-letter exchange do Consolidado | `consolidation.dlx` |
| Dead-letter queue do Consolidado | `consolidation.entry-created.dlq` |
| Routing key da DLQ | `consolidation.entry-created.dead` |
| Exchange de retry do Consolidado | `consolidation.retry` |
| Fila de retry do Consolidado | `consolidation.entry-created.retry` |
| Routing key de retry | `consolidation.entry-created.retry` |

JSON inválido e eventos `EntryCreated.v1` semanticamente inválidos são encaminhados para a DLQ básica. Erros desconhecidos ou transitórios do `Consolidation.Worker` são publicados na fila de retry com `x-retry-count` incrementado e retornam à exchange `ledger.events` após o TTL local; ao exceder `RabbitMq__MaxRetryAttempts`, a mensagem é encaminhada para a DLQ. As filas podem ser inspecionadas localmente em `http://localhost:15672/#/queues`.

Observabilidade local demonstrável:

```text
- padrão de instrumentação: OpenTelemetry
- logs estruturados: ILogger
- traces customizados: ActivitySource
- métricas customizadas: Meter
- exportação: OTLP configurável por OTEL_EXPORTER_OTLP_ENDPOINT e OTEL_EXPORTER_OTLP_PROTOCOL
- visualização local: Aspire Dashboard em http://localhost:18888
```

As aplicações no Compose usam `OTEL_EXPORTER_OTLP_ENDPOINT=http://aspire-dashboard:18889` e `OTEL_EXPORTER_OTLP_PROTOCOL=grpc`. Fora do Compose, se o endpoint OTLP não estiver configurado, as aplicações continuam executando sem exportar para backend remoto.

Fluxo local para gerar telemetria:

Exemplo Windows/PowerShell. Para Linux/macOS, use os comandos Bash/Zsh de `runbook-demonstracao-local.md`.

```powershell
$token = docker compose run --rm local-jwt --merchant-id merchant-001

curl.exe -i -X POST http://localhost:8080/entries `
  -H "Authorization: Bearer $token" `
  -H "Idempotency-Key: idem-otel-001" `
  -H "X-Correlation-Id: corr-otel-001" `
  -H "Content-Type: application/json" `
  --data "{""type"":""CREDIT"",""amount"":""150.75"",""currency"":""BRL"",""occurredAt"":""2026-07-12T13:45:00Z"",""description"":""Venda local""}"

Start-Sleep -Seconds 5

curl.exe -i http://localhost:8081/daily-balances/2026-07-12 `
  -H "Authorization: Bearer $token" `
  -H "X-Correlation-Id: corr-otel-001"
```

Logs também podem ser acompanhados por componente:

```powershell
docker compose logs -f ledger-api
docker compose logs -f ledger-outbox-publisher
docker compose logs -f consolidation-worker
docker compose logs -f consolidation-api
```

O teste de carga do Consolidado é executado separadamente e não faz parte do `dotnet test` padrão:

```powershell
docker compose run --rm dotnet-sdk dotnet run --project tests/Consolidation.LoadTests
```

O teste de carga local/container-first é executado separadamente do `dotnet test` contra a `Consolidation.Api` em `http://host.docker.internal:8081`. A evidência final da janela sustentada registrou 3000 requisições planejadas, 3000 executadas, execução conforme planejado, 3000 sucessos, 0 falhas, 50.02 req/s, p95 5.80 ms e p99 7.51 ms.

Essa evidência valida o baseline local/container-first do desafio e não substitui validação produtiva ou equivalente de capacidade.

As APIs HTTP possuem rate limiting básico local/in-memory nos endpoints de negócio:

```text
- POST /entries
- GET /daily-balances/{businessDate}
```

O excesso de requisições retorna `HTTP 429` no padrão de erro da API. Os endpoints `GET /health/live` e `GET /health/ready` não aplicam rate limit. Esse baseline local não substitui rate limiting distribuído/produtivo em API Gateway, WAF, ingress ou service mesh.

As APIs HTTP expõem sinais operacionais básicos:

| API | Liveness | Readiness | Dependência verificada no readiness |
|---|---|---|---|
| Ledger.Api | `GET /health/live` | `GET /health/ready` | Ledger PostgreSQL. |
| Consolidation.Api | `GET /health/live` | `GET /health/ready` | Consolidation PostgreSQL. |

`/health/live` indica que o processo HTTP responde. `/health/ready` indica que a API pode receber tráfego e retorna 503 quando a dependência PostgreSQL mínima está indisponível.

O workflow de CI está em:

```text
.github/workflows/ci.yml
```

`Consolidation.Worker`, `Consolidation.Api`, `DailyBalance` e `GET /daily-balances/{businessDate}` fazem parte do baseline local atual, com testes de integração para processador, consumer e API.

No baseline atual, `Ledger.OutboxPublisher` deve operar com uma réplica. Multi-publisher seguro depende de claim/lock transacional com `SKIP LOCKED` ou equivalente. `Consolidation.Worker` já atualiza `DailyBalance` por upsert atômico no PostgreSQL, mas múltiplos workers ainda dependem de validação produtiva de carga, backlog, lag, autoscaling, prefetch, contenção no banco e operação.

Também permanecem pendentes rate limiting distribuído/produtivo, observabilidade produtiva completa, dashboards produtivos, alertas produtivos, retenção centralizada de logs, plataforma final de observabilidade, backoff avançado, operação produtiva de mensagens isoladas, re-drive assistido da DLQ, reconstrução/reprocessamento operacional completo, hardening produtivo de autenticação/autorização, deploy produtivo/IaC e validação de capacidade em ambiente produtivo ou equivalente.
