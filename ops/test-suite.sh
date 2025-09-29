#!/usr/bin/env bash
set -euo pipefail

# -------- Config --------
API="${API:-http://localhost:8080}"
API_KEY="${API_KEY:-admin}"
PROM_PORT="${PROM_PORT:-19090}"
DAY="$(date +%F)"
NO_TEARDOWN=false
VERBOSE=false
DO_LOAD=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-teardown) NO_TEARDOWN=true; shift ;;
    --verbose) VERBOSE=true; shift ;;
    --load) DO_LOAD=true; shift ;;
    *) shift ;;
  esac
done

# Compose command fallback
compose() {
  if docker compose version >/dev/null 2>&1; then docker compose "$@"
  elif docker-compose version >/dev/null 2>&1; then docker-compose "$@"
  else
    echo "ERROR: docker compose not found"; exit 1
  fi
}

uuid() {
  if command -v uuidgen >/dev/null 2>&1; then uuidgen
  else python3 - <<'PY'
import uuid; print(uuid.uuid4())
PY
  fi
}

header() { echo; echo "== $* =="; }

# Health wait using actuator endpoint
wait_health_url() {
  local url="$1"; local timeout="${2:-60}"; local end=$(( SECONDS + timeout ))
  while (( SECONDS < end )); do
    if out=$(curl -sf "$url" 2>/dev/null) && [[ "$out" == *'"status"'*'"UP"'* ]]; then
      return 0
    fi
    sleep 1
  done
  return 1
}

# Balance polling (eventual consistency)
wait_balance_at_least() {
  local api="$1"; local d="$2"; local min="$3"; local timeout="${4:-30}"; local end=$(( SECONDS + timeout ))
  while (( SECONDS < end )); do
    if out=$(curl -sf "$api/balances/daily?date=$d" 2>/dev/null); then
      bal=$(echo "$out" | sed -n 's/.*"balanceCents"[[:space:]]*:[[:space:]]*\([0-9]\+\).*/\1/p')
      [[ -z "$bal" ]] && bal=$(echo "$out" | awk '/balanceCents/ {print $1}' | tail -n1)
      if [[ -n "$bal" && "$bal" -ge "$min" ]]; then return 0; fi
    fi
    sleep 1
  done
  return 1
}

# Dump last logs on failure for quick diagnostics
dump_logs() {
  echo "\n== Últimos logs (10m, tail -n 200) =="
  compose ps || true
  for s in postgres rabbitmq ledger-service consolidator-service balance-query-service api-gateway; do
    echo "\n-- $s --"
    compose logs --no-color --since=10m "$s" 2>/dev/null | tail -n 500 || true
  done
}

# Trap any error to print logs and optionally teardown
on_err() {
  dump_logs
  $NO_TEARDOWN || compose down -v || true
  exit 1
}
trap on_err ERR

# -------- Bring up stack --------
header "Subindo stack (PROM_PORT=$PROM_PORT)"
export PROM_PORT
compose down -v
compose up -d --build

header "Serviços"
compose ps

header "Aguardando healthchecks (gateway e serviços)"
wait_health_url "http://localhost:8080/actuator/health" 60 || { echo "FAIL health gateway"; exit 1; }
wait_health_url "http://localhost:8081/actuator/health" 60 || { echo "FAIL health ledger"; exit 1; }
wait_health_url "http://localhost:8082/actuator/health" 60 || { echo "FAIL health consolidator"; exit 1; }
wait_health_url "http://localhost:8083/actuator/health" 60 || { echo "FAIL health balance"; exit 1; }

# -------- Smoke: ledger idempotency --------
IDEM="$(uuid)"
BODY="$(cat <<JSON
{"occurredOn":"$DAY","type":"CREDIT","amountCents":1000,"description":"smoke"}
JSON
)"

header "POST /ledger/entries (espera 201)"
mkdir -p out
echo "$BODY" > out/scenario-A-credit-req.json
H1=$(curl -sS -D - -o out/scenario-A-credit-resp.txt -X POST "$API/ledger/entries" \
  -H "Content-Type: application/json" -H "X-API-Key: $API_KEY" -H "Idempotency-Key: $IDEM" \
  --data "$BODY")
S1=$(printf "%s" "$H1" | awk 'NR==1{print $2}')
LOC1=$(printf "%s" "$H1" | awk 'BEGIN{IGNORECASE=1}/^location:/{sub(/\r$/,"\");print $2}')
echo "status=$S1 location=$LOC1"; [ "$S1" = "201" ] || { echo "FAIL 201"; exit 1; }
[[ "$VERBOSE" == true ]] && { echo "Request headers: Content-Type: application/json, X-API-Key: $API_KEY, Idempotency-Key: $IDEM"; printf '%s\n' "$H1"; cat out/scenario-A-credit-resp.txt; }

# Guard against eventual consistency: wait until balance reflects the write
AMOUNT=1000
header "Aguardando saldo diário refletir $AMOUNT (até 30s)"
wait_balance_at_least "$API" "$DAY" "$AMOUNT" 30 || { echo "FAIL: Daily balance did not reach expected value"; exit 1; }

header "Replay mesma chave (espera 200 e mesma Location)"
H2=$(curl -sS -D - -o out/scenario-A-credit-replay-resp.txt -X POST "$API/ledger/entries" \
  -H "Content-Type: application/json" -H "X-API-Key: $API_KEY" -H "Idempotency-Key: $IDEM" \
  --data "$BODY")
S2=$(printf "%s" "$H2" | awk 'NR==1{print $2}')
LOC2=$(printf "%s" "$H2" | awk 'BEGIN{IGNORECASE=1}/^location:/{sub(/\r$/,"\");print $2}')
echo "status=$S2 location=$LOC2"; [ "$S2" = "200" ] || { echo "FAIL 200"; $NO_TEARDOWN || compose down -v; exit 1; }
[ "$LOC1" = "$LOC2" ] || { echo "FAIL Location mismatch"; $NO_TEARDOWN || compose down -v; exit 1; }
[[ "$VERBOSE" == true ]] && { echo "Request headers: Content-Type: application/json, X-API-Key: $API_KEY, Idempotency-Key: $IDEM"; printf '%s\n' "$H2"; cat out/scenario-A-credit-replay-resp.txt; }

header "GET /balances/daily?date=$DAY (deve refletir 1000)"
B1=$(curl -sS "$API/balances/daily?date=$DAY")
echo "$B1" | sed 's/.*/&/' > out/scenario-A-daily-resp.json
echo "$B1"
BEFORE=$(printf "%s" "$B1" | sed -n 's/.*"balanceCents"[[:space:]]*:[[:space:]]*\([0-9-]*\).*/\1/p')

# -------- Rebuild replace-only --------
header "POST /consolidator/rebuild (D..D) -> jobId"
REQID="$(uuid)"
JOB=$(curl -sS -X POST "$API/consolidator/rebuild?from=$DAY&to=$DAY" -H "X-API-Key: $API_KEY" -H "X-Request-Id: $REQID")
echo "$JOB"
JID=$(printf "%s" "$JOB" | sed -n 's/.*"jobId"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')

header "Aguardando status COMPLETED/DONE"
for i in $(seq 1 60); do
  ST=$(curl -sS "$API/consolidator/rebuild/status/$JID")
  echo "$ST"
  if echo "$ST" | grep -Eiq '"status"\s*:\s*"(COMPLETED|DONE)"'; then break; fi
  if echo "$ST" | grep -Eiq '"status"\s*:\s*"FAILED"'; then echo "FAIL rebuild"; $NO_TEARDOWN || compose down -v; exit 1; fi
  sleep 1
done

header "GET /balances/daily?date=$DAY (deve permanecer 1000 mesmo após rebuild)"
B2=$(curl -sS "$API/balances/daily?date=$DAY")
echo "$B2"
AFTER=$(printf "%s" "$B2" | sed -n 's/.*"balanceCents"[[:space:]]*:[[:space:]]*\([0-9-]*\).*/\1/p')
if [[ "$AFTER" != "$BEFORE" ]]; then echo "FAIL: saldo mudou após rebuild ($BEFORE -> $AFTER)"; $NO_TEARDOWN || compose down -v; exit 1; fi

# -------- Scenario A: Basic DEBIT (same-day) --------
header "Scenario: BASIC DEBIT (same-day)"
TODAY_START=$AFTER
AMT_DEBIT=700
IDEM_DA=$(uuid)
BODY_DA="$(cat <<JSON
{"occurredOn":"$DAY","type":"DEBIT","amountCents":$AMT_DEBIT,"description":"debit-smoke"}
JSON
)"
echo "Request:"; echo "$BODY_DA"
HDA1=$(curl -sS -D - -o /dev/null -X POST "$API/ledger/entries" \
  -H "Content-Type: application/json" -H "X-API-Key: $API_KEY" -H "Idempotency-Key: $IDEM_DA" \
  --data "$BODY_DA")
SDA1=$(printf "%s" "$HDA1" | awk 'NR==1{print $2}')
LDA1=$(printf "%s" "$HDA1" | awk 'BEGIN{IGNORECASE=1}/^location:/{sub(/\r$/,"\");print $2}')
echo "status=$SDA1 location=$LDA1"; [ "$SDA1" = "201" ] || { echo "FAIL scenario A (201)"; exit 1; }
HDA2=$(curl -sS -D - -o /dev/null -X POST "$API/ledger/entries" \
  -H "Content-Type: application/json" -H "X-API-Key: $API_KEY" -H "Idempotency-Key: $IDEM_DA" \
  --data "$BODY_DA")
SDA2=$(printf "%s" "$HDA2" | awk 'NR==1{print $2}')
LDA2=$(printf "%s" "$HDA2" | awk 'BEGIN{IGNORECASE=1}/^location:/{sub(/\r$/,"\");print $2}')
echo "replay status=$SDA2 location=$LDA2"; [ "$SDA2" = "200" ] || { echo "FAIL scenario A (200 replay)"; exit 1; }
[ "$LDA1" = "$LDA2" ] || { echo "FAIL scenario A (Location mismatch)"; exit 1; }
EXP_TODAY=$(( TODAY_START - AMT_DEBIT ))
wait_balance_at_least "$API" "$DAY" "$EXP_TODAY" 30 || { echo "FAIL scenario A (did not reach expected balance)"; exit 1; }
TODAY_AFTER_A=$(curl -sS "$API/balances/daily?date=$DAY" | sed -n 's/.*"balanceCents"[[:space:]]*:[[:space:]]*\([0-9-]*\).*/\1/p')
[ "$TODAY_AFTER_A" = "$EXP_TODAY" ] || { echo "FAIL scenario A (expected $EXP_TODAY, got $TODAY_AFTER_A)"; exit 1; }
echo "PASS scenario A"

# -------- Scenario B: Mixed same-day (2x CREDIT, 1x DEBIT) --------
header "Scenario: MIXED same-day (+500,+400,-200)"
BASE_B=$TODAY_AFTER_A
post_entry() { local body="$1"; local key=$(uuid); curl -sS -o /dev/null -w "%{http_code}" -D /dev/null -X POST "$API/ledger/entries" -H "Content-Type: application/json" -H "X-API-Key: $API_KEY" -H "Idempotency-Key: $key" --data "$body"; }
BODY_C1='{"occurredOn":"'"$DAY"'","type":"CREDIT","amountCents":500,"description":"mix1"}'
BODY_C2='{"occurredOn":"'"$DAY"'","type":"CREDIT","amountCents":400,"description":"mix2"}'
BODY_D1='{"occurredOn":"'"$DAY"'","type":"DEBIT","amountCents":200,"description":"mix3"}'
post_entry "$BODY_C1" >/dev/null
post_entry "$BODY_C2" >/dev/null
post_entry "$BODY_D1" >/dev/null
EXP_B=$(( BASE_B + 700 ))
wait_balance_at_least "$API" "$DAY" "$EXP_B" 30 || { echo "FAIL scenario B (did not reach expected)"; exit 1; }
CUR_B=$(curl -sS "$API/balances/daily?date=$DAY" | sed -n 's/.*"balanceCents"[[:space:]]*:[[:space:]]*\([0-9-]*\).*/\1/p')
[ "$CUR_B" = "$EXP_B" ] || { echo "FAIL scenario B (expected $EXP_B got $CUR_B)"; exit 1; }
echo "PASS scenario B"

# -------- Scenario C: Multi-day (yesterday + today) --------
header "Scenario: MULTI-DAY (yesterday + today)"
YDAY=$(python3 - <<'PY'
import datetime;print((datetime.date.today()-datetime.timedelta(days=1)).strftime('%Y-%m-%d'))
PY
)
Y0=$(curl -sS "$API/balances/daily?date=$YDAY" | sed -n 's/.*"balanceCents"[[:space:]]*:[[:space:]]*\([0-9-]*\).*/\1/p'); Y0=${Y0:-0}
T0=$CUR_B
BODY_YC='{"occurredOn":"'"$YDAY"'","type":"CREDIT","amountCents":300,"description":"ycredit"}'
BODY_TD='{"occurredOn":"'"$DAY"'","type":"DEBIT","amountCents":100,"description":"tdebit"}'
post_entry "$BODY_YC" >/dev/null
post_entry "$BODY_TD" >/dev/null
EXP_Y=$(( Y0 + 300 ))
wait_balance_at_least "$API" "$YDAY" "$EXP_Y" 30 || { echo "FAIL scenario C (yesterday not reached)"; exit 1; }
EXP_T=$(( T0 - 100 ))
wait_balance_at_least "$API" "$DAY" "$EXP_T" 30 || { echo "FAIL scenario C (today not reached)"; exit 1; }
Y_NOW=$(curl -sS "$API/balances/daily?date=$YDAY" | sed -n 's/.*"balanceCents"[[:space:]]*:[[:space:]]*\([0-9-]*\).*/\1/p')
T_NOW=$(curl -sS "$API/balances/daily?date=$DAY" | sed -n 's/.*"balanceCents"[[:space:]]*:[[:space:]]*\([0-9-]*\).*/\1/p')
[ "$Y_NOW" = "$EXP_Y" ] && [ "$T_NOW" = "$EXP_T" ] || { echo "FAIL scenario C (expected Y=$EXP_Y,T=$EXP_T got Y=$Y_NOW,T=$T_NOW)"; exit 1; }
echo "PASS scenario C"

# -------- Scenario D: Rebuild invariance (yesterday..today) --------
header "Scenario: REBUILD invariance (yesterday..today)"
PRE_Y=$Y_NOW; PRE_T=$T_NOW
REQID2=$(uuid)
JOB2=$(curl -sS -X POST "$API/consolidator/rebuild?from=$YDAY&to=$DAY" -H "X-API-Key: $API_KEY" -H "X-Request-Id: $REQID2")
JID2=$(printf "%s" "$JOB2" | sed -n 's/.*"jobId"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')
for i in $(seq 1 60); do ST2=$(curl -sS "$API/consolidator/rebuild/status/$JID2"); echo "$ST2"; echo "$ST2" | grep -Eiq '"status"\s*:\s*"(COMPLETED|DONE)"' && break; sleep 1; done
Y_AFTER=$(curl -sS "$API/balances/daily?date=$YDAY" | sed -n 's/.*"balanceCents"[[:space:]]*:[[:space:]]*\([0-9-]*\).*/\1/p')
T_AFTER=$(curl -sS "$API/balances/daily?date=$DAY" | sed -n 's/.*"balanceCents"[[:space:]]*:[[:space:]]*\([0-9-]*\).*/\1/p')
[ "$Y_AFTER" = "$PRE_Y" ] && [ "$T_AFTER" = "$PRE_T" ] || { echo "FAIL scenario D (balances changed after rebuild)"; exit 1; }
echo "PASS scenario D"

# -------- Scenario E: Security 403 --------
header "Scenario: SECURITY 403 (missing API key)"
code=$(curl -sS -o /dev/null -w "%{http_code}" -X POST "$API/ledger/entries" -H "Content-Type: application/json" --data "$BODY")
[ "$VERBOSE" == true ] && echo "POST without key body: $BODY"
[ "$code" = "403" ] || { echo "FAIL scenario E (expected 403 got $code)"; exit 1; }
echo "PASS scenario E"

# -------- Scenario F: Validation 400 --------
header "Scenario: VALIDATION 400 (bad payload)"
BAD='{"occurredOn":"'"$DAY"'","type":"X","amountCents":100}'
echo "$BAD" > out/scenario-F-bad-req.json
code=$(curl -sS -o /dev/null -w "%{http_code}" -X POST "$API/ledger/entries" -H "Content-Type: application/json" -H "X-API-Key: $API_KEY" -H "Idempotency-Key: $(uuid)" --data "$BAD")
[ "$code" = "400" ] || { echo "FAIL scenario F (expected 400 got $code)"; exit 1; }
echo "PASS scenario F"

if [[ "$DO_LOAD" == true ]]; then
  header "Micro-load (50 rps, 15s, max-loss 5%)"
  bash ops/load.sh --rps 50 --duration 15s --max-loss 0.05 --date "$DAY"
fi

header "OK: testes concluídos"
$NO_TEARDOWN || compose down -v
echo "$BODY_DA" > out/scenario-A-debit-req.json
HDA1=$(curl -sS -D - -o out/scenario-A-debit-resp.txt -X POST "$API/ledger/entries" \
HDA2=$(curl -sS -D - -o out/scenario-A-debit-replay-resp.txt -X POST "$API/ledger/entries" \
echo "PASS scenario A"
echo "$BODY_C1" > out/scenario-B-1-req.json; echo "$BODY_C2" > out/scenario-B-2-req.json; echo "$BODY_D1" > out/scenario-B-3-req.json
curl -sS -D - -o out/scenario-B-1-resp.txt -X POST "$API/ledger/entries" -H "Content-Type: application/json" -H "X-API-Key: $API_KEY" -H "Idempotency-Key: $(uuid)" --data "$BODY_C1" >/dev/null
curl -sS -D - -o out/scenario-B-2-resp.txt -X POST "$API/ledger/entries" -H "Content-Type: application/json" -H "X-API-Key: $API_KEY" -H "Idempotency-Key: $(uuid)" --data "$BODY_C2" >/dev/null
curl -sS -D - -o out/scenario-B-3-resp.txt -X POST "$API/ledger/entries" -H "Content-Type: application/json" -H "X-API-Key: $API_KEY" -H "Idempotency-Key: $(uuid)" --data "$BODY_D1" >/dev/null
echo "PASS scenario B"
echo "$BODY_YC" > out/scenario-C-ycredit-req.json
echo "$BODY_TD" > out/scenario-C-tdebit-req.json
curl -sS -D - -o out/scenario-C-ycredit-resp.txt -X POST "$API/ledger/entries" -H "Content-Type: application/json" -H "X-API-Key: $API_KEY" -H "Idempotency-Key: $(uuid)" --data "$BODY_YC" >/dev/null
curl -sS -D - -o out/scenario-C-tdebit-resp.txt -X POST "$API/ledger/entries" -H "Content-Type: application/json" -H "X-API-Key: $API_KEY" -H "Idempotency-Key: $(uuid)" --data "$BODY_TD" >/dev/null
echo "$JOB2" | sed 's/.*/&/' > out/scenario-D-rebuild-resp.json
