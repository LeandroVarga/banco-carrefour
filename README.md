# Cashflow Challenge (KISS)

## 📌 Visão geral
Sistema mínimo de **microserviços** para controle de caixa diário de um comerciante:
- **Escrita** de lançamentos (CREDIT/DEBIT) com **idempotência HTTP** e **Outbox** → publica eventos no **RabbitMQ**.
- **Consolidador** consome os eventos e mantém o **saldo diário** em um modelo de relatório.
- **Leituras** expõem saldo diário e por intervalo via serviço **balance-query**.
- Execução **100% local** via **Docker Compose**.

Serviços (via gateway):
- **api-gateway** (`:8080`) — roteia para os serviços e aplica rate limit.
- **ledger-service** (`:8081`) — recebe lançamentos, persiste e publica no RabbitMQ.
- **consolidator-service** (`:8082`) — consome eventos e atualiza os saldos diários.
- **balance-query-service** (`:8083`) — expõe os saldos consolidados.

Infra (local):
- **PostgreSQL 16**, **RabbitMQ** (plugin de management), **Prometheus** e **Grafana**.

## 🧭 Endpoints principais (via gateway)
- `POST /ledger/entries` — cria lançamento. **Headers obrigatórios**: `Idempotency-Key`; se **API_KEY** estiver configurada, também `X-API-Key`.
- `GET /balances/daily?date=YYYY-MM-DD` — saldo diário (retorna `{day,balanceCents}`).
- `GET /balances/range?from=YYYY-MM-DD&to=YYYY-MM-DD` — saldos em intervalo (lista `{day,balanceCents}`).
- **Opcional** (onde presente): Swagger em `/swagger`.

### Cache das leituras
- `GET /balances/daily` e `GET /balances/range` retornam `Cache-Control: public, max-age=30` (30s).

## 🔒 Segurança (escritas)
- O gateway valida `X-API-Key` **apenas se** a variável `API_KEY` estiver definida (ex.: via `./secrets/API_KEY`). Caso não esteja, o filtro é bypassado.
- Todas as escritas devem enviar **`Idempotency-Key`** (replay devolve o mesmo recurso).

## ⛔ Rate limiting (NFR)
- Padrão: **50 rps** para caminhos configuráveis (ex.: `/balances/*,/ledger/*`). Excedentes recebem **HTTP 429** com `Retry-After: 1`.
- Há regras no Prometheus para alertar quando o rejeitado > 5% (1 min).

## 📨 Topologia AMQP
- **Exchange**: `ledger.events`
- **Routing key**: `ledger.entry-recorded`
- **Fila principal**: `report.ledger.entry-recorded.q`
- **DLX**: `ledger.dlx` → **DLQ** `report.ledger.entry-recorded.dlq` (routing `ledger.entry-recorded.dlq`)
- A **declaração da topologia** (exchanges/queues/bindings) é de **consolidator-service**; o **ledger** só publica.
- Para recriar a DLQ com argumentos atualizados, apague a fila e reinicie o consolidator.

## 🧪 Como rodar e testar

### Pré‑requisitos
- **Docker** e **Docker Compose** já instalados.

### Subir a stack
```bash
docker compose up -d --build
docker compose ps
```

### Testes de integração (em container)
- Linux/macOS:
  ```bash
  bash ops/test-suite.sh
  ```
- Windows (PowerShell):
  ```powershell
  pwsh -ExecutionPolicy Bypass -File ops/test-suite.ps1
  ```

### Exemplos rápidos (curl)
```bash
# Crédito de R$ 100,00 (10000 cents)
curl -s -X POST http://localhost:8080/ledger/entries   -H "Content-Type: application/json"   -H "Idempotency-Key: 11111111-1111-1111-1111-111111111111"   -d '{"occurredOn":"2025-01-10","amountCents":10000,"type":"CREDIT","description":"Sale #123"}'

# Débito de R$ 30,00 (3000 cents)
curl -s -X POST http://localhost:8080/ledger/entries   -H "Content-Type: application/json"   -H "Idempotency-Key: 22222222-2222-2222-2222-222222222222"   -d '{"occurredOn":"2025-01-10","amountCents":3000,"type":"DEBIT","description":"Supplies"}'

# Saldo diário
curl -s "http://localhost:8080/balances/daily?date=2025-01-10"
```

## ⚙️ Configuração (principais)
- **Gateway**
  - `gateway.rps.limit` (env `GATEWAY_RPS_LIMIT`) — limite por segundo (padrão 50).
  - `gateway.rps.paths` (env `GATEWAY_RPS_PATHS`) — caminhos alvo (padrão `/balances/*,/ledger/*`).
  - CORS: `app.cors.allowed-origins` (env `APP_CORS_ALLOWED_ORIGINS`, padrão `http://localhost:3000`).
- **Balance query**
  - Porta `8083`; endpoints com cache de 30s.
- **Segredos opcionais** (`./secrets/`): `API_KEY`, `SPRING_DATASOURCE_*`, `SPRING_RABBITMQ_*`.
- **Prometheus/Grafana**
  - Prometheus: `http://localhost:19090`
  - Grafana: `http://localhost:3000` (provisionado no `ops/grafana`).

## 🔍 Observabilidade
- Todos os serviços expõem **`/actuator/health`** e **`/actuator/prometheus`**; build info em **`/actuator/info`**.
- Prometheus coleta métricas de: gateway (`:8080`), ledger (`:8081`), consolidator (`:8082`), balance-query (`:8083`) e RabbitMQ (`:15692`).
- Regras de alerta incluem **OutboxStuck**, **ConsolidatorDuplicatesSpike**, **BalanceQueriesRejectionsHigh**.

## 🗂️ Arquitetura (resumo)
- Camadas por serviço (domain, application, infrastructure, api).
- **Outbox** no ledger para entrega confiável; correlaciona por `correlationId`.
- Consolidation **assíncrona** via RabbitMQ.
- OpenAPI onde configurado; Actuator + Micrometer/Prometheus.

## 🧰 Dicas de diagnose
```bash
docker compose logs --no-color api-gateway | tail -n +1
docker compose logs --no-color ledger-service | tail -n +1
docker compose logs --no-color consolidator-service | tail -n +1
docker compose logs --no-color balance-query-service | tail -n +1
docker compose logs --no-color rabbitmq | tail -n +1
docker compose logs --no-color postgres | tail -n +1
```