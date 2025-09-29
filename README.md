Cashflow Challenge (KISS)

Mission
- Minimal microservices system for a merchant’s daily cashflow: write ledger entries (CREDIT/DEBIT) and expose a consolidated daily balance. The write path uses HTTP idempotency + Outbox to RabbitMQ; the consolidator updates the report model; reads query daily or ranges via the balance-query service.
- Everything runs locally with Docker Compose.

Services
- api-gateway (port 8080): routes to services, in-memory rate limit 50 rps on /balances/*
- ledger-service (port 8081): accepts entries with idempotency, writes outbox and publishes to RabbitMQ
- consolidator-service (port 8082): consumes ledger events and maintains daily balances in report schema
- balance-query-service (port 8083): reads aggregated daily balances

Infra (local)
- Postgres 16, RabbitMQ 3-management, Prometheus, Grafana

How to run
- Ensure Docker and Docker Compose are installed
- From repo root: `docker compose up -d --build`
- Example commands are in this README under Examples and `ops/smoke.*` scripts

Examples
```
# Create a credit (R$ 100,00 = 10000 cents)
curl -s -X POST http://localhost:8080/ledger/entries \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: 11111111-1111-1111-1111-111111111111" \
  -d '{"occurredOn":"2025-01-10","amountCents":10000,"type":"CREDIT","description":"Sale #123"}'

# Create a debit (R$ 30,00)
curl -s -X POST http://localhost:8080/ledger/entries \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: 22222222-2222-2222-2222-222222222222" \
  -d '{"occurredOn":"2025-01-10","amountCents":3000,"type":"DEBIT","description":"Supplies"}'

# Query daily balance
curl -s "http://localhost:8080/balances/daily?date=2025-01-10"
```

DLQ Topology
- Exchange `ledger.events` routes `ledger.entry-recorded` to queue `report.ledger.entry-recorded.q`.
- The queue dead-letters to DLX `ledger.dlx` with routing key `ledger.entry-recorded.dlq` into queue `report.ledger.entry-recorded.dlq` after retry exhaustion.

AMQP Topology Ownership
- consolidator-service declares the RabbitMQ topology (exchanges, queues, bindings).
- ledger-service is publish-only: it does not declare any queues/exchanges/bindings.
- To reset stale DLQ arguments, delete the queue and let consolidator recreate it on startup:

```
docker exec -it banco-carrefour-rabbitmq-1 rabbitmqctl delete_queue report.ledger.entry-recorded.dlq
```

How to run
1) mvn -q clean
2) docker compose down -v && docker compose up -d --build
3) docker compose run --rm tester

Auth & headers
- Writes (POST /ledger/**) require `X-API-Key: admin` and `Idempotency-Key` (first write returns 201 + id + Location; duplicates return 409 with same id/Location).
- Reads remain open.

Endpoints / examples
- POST /ledger/entries — create entry (Idempotency-Key required)
- GET /balances/daily?date=YYYY-MM-DD — daily balance
- GET /balances/range?from=YYYY-MM-DD&to=YYYY-MM-DD — range
- Swagger UI (where present): /swagger

Rate limiting NFR
- `/balances/**` is throttled at `GATEWAY_RPS_LIMIT` (default 50 rps). Excess requests receive `429` with `Retry-After: 1`. Prometheus rule `BalanceQueriesRejectionsHigh` fires if 1‑minute rejection ratio exceeds 5%.

Observability
- All services expose `/actuator/health` and `/actuator/prometheus` with common tags (application, instance). Build info is available at `/actuator/info`.

Rate limiting NFR
- `/balances/**` endpoints are throttled at `GATEWAY_RPS_LIMIT` (default 50 rps) via the API Gateway. Requests beyond the limit receive `429 Too Many Requests` and `Retry-After: 1`.
- Prometheus alert `BalanceQueriesRejectionsHigh` fires when 1‑minute rejection ratio exceeds 5%, satisfying the “≤5% loss” target under default sizing.

Outbox Delivery Guarantees
- Ledger deletes outbox rows only when a broker ACK is received and no RabbitMQ RETURN was observed for that message.
- Each published message carries a `correlationId` equal to the outbox event UUID; basic.return events are tracked by this id.
- If a message is RETURNed (e.g., unroutable) but later ACKed, it is kept for retry; the periodic scheduler drains the outbox automatically.

Security
- Admin/backfill endpoint `/consolidator/rebuild` requires `X-API-Key` if `API_KEY` env var is set on the service.

Updated Acceptance
- Flyway V2 migrations applied; indexes exist on `ledger.entries(occurred_on)` and `report.daily_balances(day)`
- Consolidator listener retries with exponential backoff (max 5) then routes to DLQ; no infinite requeue
- Ledger outbox publisher uses separate counters and gentle backoff between scheduler runs
- All services propagate `X-Request-Id` correlation; gateway and services reflect it in responses
- Gateway exposes Prometheus counter for rate-limit rejections
- Load test at ~50 rps for 60s on `/balances/daily` yields ≤5% non-2xx (mostly 429) per instance

Architecture
- DDD-ish layering per service (domain, application, infrastructure, api)
- Outbox pattern for event publishing (ledger-service)
- Async consolidation using RabbitMQ
- OpenAPI per service, Actuator + Prometheus metrics

Docs
- See `docs/adr` for architectural decisions
- See `docs/diagrams` for C4 Mermaid diagrams

Acceptance Checklist
- Compose up: containers healthy (healthchecks green).
- POST /ledger/entries (with Idempotency-Key) → 201 + Location/id; repeating same body/key → 409 (no duplicates).
- GET /balances/daily shows the net balance; range returns ordered daily balances.
- Bombard /balances/** beyond 50 rps → 429 + Retry-After: 1; Prometheus counters increase; alert fires when >5%.
- Stop consolidator, create entries, start consolidator → balances eventually catch up (outbox drains).
- Prometheus scrapes all services; Grafana dashboards load.

How we run
```
mvn -q clean
docker compose down -v && docker compose up -d --build
docker compose run --rm tester
```

How to test
- Prereqs: Docker, Make, Java 17, Maven
- Copy `.env.example` → `.env` (optional: `make secrets.dev` to populate `./secrets/*`)
- One‑liner: `./ops/test-suite.sh` (Linux/macOS) or `pwsh ./ops/test-suite.ps1` (Windows)
- Expected: final `PASS: end-to-end suite completed`

Manual targets
- `make up` → compose up & wait healthy
- `make smoke` → quick idempotency + daily balance check
- `make load` → run load (`ops/load.*`) with defaults (50 rps, 15s)
- `make e2e` → full end‑to‑end suite (`ops/test-suite.*`)
- `make it` → run integration tests container (tester profile)
- `make down` → compose down -v

Endpoints & headers
- `POST /ledger/entries` → 201 Created (first) / 200 OK (replay); requires `X-API-Key` and recommends `Idempotency-Key`; echoes `X-Request-Id` and `Idempotency-Key`; `Retry-After: 1` appears only on 429s from rate limit
- `GET /balances/daily?date=YYYY-MM-DD` → 200 OK JSON `{day,balanceCents}`; echoes `X-Request-Id`, `Idempotency-Key` if provided; supports CORS for `http://localhost:3000`
- `POST /consolidator/rebuild?from=YYYY-MM-DD&to=YYYY-MM-DD` → 202 `{jobId}`; `GET /consolidator/rebuild/status/{jobId}` → 200 `{status}`

Troubleshooting
- Port conflicts (5432/8080..8083/19090): stop other services or change host ports via `.env` where applicable
- API key mismatch: ensure `API_KEY` is `admin` (or set secrets); all POSTs through gateway must include `X-API-Key`
- RabbitMQ not healthy: check compose logs for node/cookie/node name; we set `rabbit@rabbitmq` with a default cookie
- Prometheus: `http://localhost:${PROM_PORT:-19090}` reachable; service metrics exposed at `/actuator/prometheus`
Run locally (quickstart)
- Prereqs: Docker, Make, Java 17, Maven
- Copy `.env.example` → `.env` and tweak if needed
- Optional: `make secrets.dev` to materialize `./secrets/*` from `.env`
- Start: `docker compose up -d --build`

Configuration
- Gateway: `GATEWAY_RPS_LIMIT` (default 50), `GATEWAY_RPS_PATHS` (default `/balances/*,/ledger/*`)
- CORS: per-service `app.cors.allowed-origins` (env `APP_CORS_ALLOWED_ORIGINS`), default `http://localhost:3000`

Headers & error payload
- X-Request-Id: provided or generated per request; echoed in responses
- Idempotency-Key: echoed in responses when provided on request
- Error JSON (uniform):
  `{ "timestamp": "<ISO-8601>", "status": <int>, "error": "<message>", "path": "<path>", "requestId": "<X-Request-Id>", "idempotencyKey": "<Idempotency-Key>" }`

Idempotency (safe replay)
- POST /ledger/entries: first → 201 Created + Location/id; replay with same key → 200 OK with same id (Location unchanged)

Rebuild flow (consolidator)
- POST `/consolidator/rebuild?from=YYYY-MM-DD&to=YYYY-MM-DD` → 202 `{jobId}`
- Constraints: `from <= to`, span ≤ 366 days
- GET `/consolidator/rebuild/status/{jobId}` → status JSON

Balances API
- `/balances/daily?date=YYYY-MM-DD` (Cache-Control: public, max-age=30)
- `/balances/range?from=…&to=…[&page=&size=]` (span ≤ 366 days, returns `[ {day,balanceCents} ]`, cached 30s)

Troubleshooting
- If using secrets, run `make secrets.dev` or create files under `./secrets/` (API_KEY, SPRING_DATASOURCE_USERNAME/PASSWORD, SPRING_RABBITMQ_USERNAME/PASSWORD)
- Without `./secrets`, env vars from `.env` are used automatically
- Logs: `docker compose logs --no-color <service>`; Health: `/actuator/health`, Metrics: `/actuator/prometheus`

Create a credit (PowerShell)
```
$headers = @{ 'Content-Type'='application/json'; 'X-API-Key'='admin'; 'Idempotency-Key'=[guid]::NewGuid().Guid }
$body = @{ occurredOn = (Get-Date -Format 'yyyy-MM-dd'); type='CREDIT'; amountCents=10000; description='seed' } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri 'http://localhost:8080/ledger/entries' -Headers $headers -Body $body
```

Daily balance
```
$d = Get-Date -Format 'yyyy-MM-dd'
Invoke-RestMethod -Method Get -Uri "http://localhost:8080/balances/daily?date=$d"
```



# Cash Flow Challenge — Execução Local & Testes

## Pré-requisitos
- Docker Desktop ou Docker Engine (Linux/WSL2)
- 4GB+ RAM livre
- **Opcional:** `make` (Linux/macOS) para atalhos

## Subir o stack
```bash
# Linux/macOS
docker compose up -d --build
docker compose ps

# Windows (PowerShell)
docker compose up -d --build
docker compose ps
```

Prometheus: porta do host controlada por PROM_PORT (padrão 19090). O container segue em 9090.

## Testes automatizados (OS-agnóstico)
### Linux/macOS
```
chmod +x ops/test-suite.sh
./ops/test-suite.sh
```

### Windows (PowerShell)
```
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\ops\test-suite.ps1
```

Os testes executam:

- Idempotência do Ledger
  - POST /ledger/entries → 201 + Location
  - Replay com mesma Idempotency-Key → 200 + mesma Location
- Balance Diário
  - GET /balances/daily?date=YYYY-MM-DD → saldo reflete o lançamento (1000)
- Rebuild replace-only
  - POST /consolidator/rebuild?from=D&to=D → jobId
  - Poll do status até COMPLETED/DONE
  - GET diário permanece 1000 mesmo após rebuild repetidos

## Diagnóstico rápido
```
docker compose logs --no-color api-gateway | tail -n +1
docker compose logs --no-color ledger-service | tail -n +1
docker compose logs --no-color consolidator-service | tail -n +1
docker compose logs --no-color balance-query-service | tail -n +1
docker compose logs --no-color rabbitmq | tail -n +1
docker compose logs --no-color postgres | tail -n +1
```

## Variáveis úteis
- API_KEY (default admin) — usada pelos scripts de teste.
- PROM_PORT (default 19090) — porta do host para Prometheus.

## Observações
- Sem alterações de comportamento: rotas/JSON/headers/limites/CORS permanecem iguais.
- Os docker-entrypoint.sh dos serviços não foram modificados.
## Como testar (Linux, macOS e Windows)

Suba a stack:

```bash
docker compose up -d --build
docker compose ps
```

Rode a suíte de testes:

- Linux/macOS: `bash ops/test-suite.sh --load`
- Windows: `pwsh -ExecutionPolicy Bypass -File ops/test-suite.ps1 -Load`

Detalhes completos em `docs/TESTES.md`. Para testes manuais com VS Code, abra `ops/requests.http` (REST Client).
