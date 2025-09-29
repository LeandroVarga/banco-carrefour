# Guia do Avaliador — Testes Funcionais e NFR

## Pré-requisitos
- Docker + Docker Compose
- Linux/macOS: bash, curl · Windows: PowerShell 7 (pwsh)
- API_KEY no .env (default: admin)

## Subir a stack
```bash
docker compose up -d --build
```

## Testes automatizados (cross-OS)
Linux/macOS:
```bash
bash ops/test-suite.sh --load
```
Windows:
```powershell
pwsh -ExecutionPolicy Bypass -File ops/test-suite.ps1 -Load
```

Os runners:
- aguardam health dos serviços (gateway 8080; apps 8081..8083);
- executam os cenários A..I abaixo;
- exibem o que foi enviado (método, URL, headers, body) e o que retornou (status, headers relevantes e body pretty JSON);
- salvam evidências em out/ (requests/responses por cenário);
- executam micro-carga somente após smoke com sucesso (50 rps por 15s, perda ≤ 5%).

## Cenários funcionais (com critérios claros)

Headers padrão em POSTs:
X-API-Key: admin · Content-Type: application/json · Idempotency-Key: <GUID>
Header recomendado em GETs: X-Request-Id: <GUID>

A — Crédito básico (201 → replay 200)
POST /ledger/entries
```json
{ "occurredOn": "YYYY-MM-DD", "type": "CREDIT", "amountCents": 1000, "description": "smoke" }
```
Esperado: 201 Location: /ledger/entries/<id>; replay com mesma Idempotency-Key → 200 mesma Location. GET /balances/daily?date=YYYY-MM-DD ≥ 1000.

B — Débito básico
POST type: "DEBIT", "amountCents": 700. GET /balances/daily?date=YYYY-MM-DD reduz em 700 vs. saldo anterior.

C — Mix no mesmo dia
Envie: +500, +400, −200 (ordem livre). GET → saldo do dia = +700.

D — Multi-dia (ontem + hoje)
Ontem: +500, −200; Hoje: +400. GET ontem = +300; GET hoje = +400.

E — Rebuild não altera saldo (invariância)
POST /consolidator/rebuild?from=<ontem>&to=<hoje> → { "jobId": "..." }; Poll até COMPLETED|DONE. GETs idênticos aos medidos antes do rebuild.

F — Segurança 403
POST sem X-API-Key → 403 Forbidden.

G — Múltiplos débitos no mesmo dia
Envie: −300 e −200 adicionais. GET → saldo do dia reduzido em −500 adicionais.

H — Mix reordenado
Envie +500, −200, +400 (ordem aleatória). GET → saldo final = +700.

I — Rebuild multi-dia (replace-only)
Refaça créditos/débitos em ontem e hoje, verifique GETs. POST rebuild from=ontem&to=hoje e re-verifique: valores idênticos.

### Aceitação (geral):
- POST inicial 201 + Location estável; replay com mesma Idempotency-Key 200 + mesma Location.
- GET /balances/daily reflete lançamentos; rebuild não altera saldos (replace-only).
- NFR: micro-carga (50 rps / 15s) com perda ≤ 5%.

## Micro-carga (NFR)
Linux/macOS: `bash ops/test-suite.sh --load` (ou `bash ops/load.sh --rps 50 --duration 15s --max-loss 0.05`)

Windows: `pwsh -ExecutionPolicy Bypass -File ops/test-suite.ps1 -Load` (ou `pwsh ops\load.ps1 -Rps 50 -Duration '15s' -MaxLoss 0.05`)

Somente leitura: GET /balances/daily. Falha se perda > limiar.

## Troubleshooting rápido
Manter a stack para inspeção: `--no-teardown` / `-NoTeardown`

Logs: `docker compose logs --no-color --since=10m`

Evidências salvas em out/ (requests/responses por cenário).
