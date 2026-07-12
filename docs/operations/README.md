# Operações

Esta pasta contém a documentação operacional da solução.

Documentos:

- `arquitetura-operacional.md`
- `observabilidade-sli-slo-e-recuperacao.md`
- `estimativa-de-custos.md`
- `teste-de-carga-consolidado.md`

A documentação operacional cobre implantação, execução local, health checks, escalabilidade, recuperação, retries, isolamento de mensagens, reprocessamento, reconstrução, observabilidade, SLIs, SLOs, custos e limites entre ambiente local e produção.

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

Subir a solução local completa:

```powershell
docker compose up -d --build ledger-api ledger-outbox-publisher consolidation-worker consolidation-api
```

Serviços expostos localmente:

| Serviço | URL local |
|---|---|
| Ledger.Api | `http://localhost:8080` |
| Consolidation.Api | `http://localhost:8081` |
| RabbitMQ Management | `http://localhost:15672` |

As APIs usam `8080` dentro dos containers. O Compose publica a `Consolidation.Api` em `localhost:8081` no host.
O RabbitMQ Management usa as credenciais locais `ledger` / `ledger`.

Fila operacional básica do Consolidado:

| Finalidade | Exchange/Fila |
|---|---|
| Exchange de eventos do Ledger | `ledger.events` |
| Fila principal do Consolidado | `consolidation.entry-created` |
| Dead-letter exchange do Consolidado | `consolidation.dlx` |
| Dead-letter queue do Consolidado | `consolidation.entry-created.dlq` |
| Routing key da DLQ | `consolidation.entry-created.dead` |

JSON inválido e eventos `EntryCreated.v1` semanticamente inválidos são encaminhados para a DLQ básica. A DLQ pode ser inspecionada localmente em `http://localhost:15672/#/queues`.

O teste de carga do Consolidado é executado separadamente e não faz parte do `dotnet test` padrão:

```powershell
docker compose run --rm dotnet-sdk dotnet run --project tests/Consolidation.LoadTests
```

O teste de carga local/container-first foi executado contra a `Consolidation.Api` em `http://host.docker.internal:8081` e atendeu aos critérios na janela sustentada: 50.01 req/s, 0% falhas, p95 4.50 ms e p99 5.68 ms.

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

`Consolidation.Worker`, `Consolidation.Api`, `DailyBalance` e `GET /daily-balances/{businessDate}` já foram implementados no incremento do Consolidado, com testes de integração para processador, consumer e API.

Também permanecem pendentes observabilidade completa, retry/backoff avançado, operação produtiva de mensagens isoladas, reconstrução/reprocessamento operacional completo, hardening produtivo de autenticação/autorização, deploy produtivo/IaC e validação de capacidade em ambiente produtivo ou equivalente.
