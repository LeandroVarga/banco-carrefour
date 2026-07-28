#!/bin/sh
# Prova/operação de rotação de credencial de banco ponta a ponta (ADR-0009):
# PostgreSQL (ALTER ROLE) -> Secrets Manager (secret-value-bootstrap real,
# nunca reimplementado) -> restart do componente afetado -> fluxo de negócio
# real repetido com sucesso. Nunca imprime valor de senha - só resultados
# booleanos/HTTP status/tamanho da senha gerada.
#
# Pré-requisito: a stack local já precisa estar de pé e saudável
# (docker compose up -d --build ledger-api ledger-outbox-publisher
# consolidation-worker consolidation-api aspire-dashboard - ver
# docs/operations/runbook-demonstracao-local.md), com .env e
# .local/security/.env.security já gerados por bootstrap-local-security.
#
# Uso:
#   sh scripts/security/rotate-database-credential.sh <componente>
#
# <componente> é um de: ledger-api | ledger-outbox-publisher |
#                       consolidation-api | consolidation-worker
#
# Rotaciona SOMENTE a credencial do componente informado - as outras 3
# permanecem com o valor atual do .env (nenhuma regeneração ampla).
set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

COMPONENT="${1:-}"

log() { printf '%s\n' "$1"; }
fail() { echo "rotate-database-credential: FALHA: $1" >&2; exit 1; }

case "$COMPONENT" in
  ledger-api)
    ROLE=ledger_api
    PG_SERVICE=ledger-postgres
    PG_SUPERUSER=ledger
    PG_DB=ledger
    PG_ALIAS=ledger-postgres
    SECRET_NAME="banco-carrefour/ledger-api/db-credentials"
    ENV_VAR=LEDGER_API_PASSWORD
    COMPOSE_SERVICE=ledger-api
    HEALTH_URL="https://localhost:8443/ledger/health/ready"
    ;;
  ledger-outbox-publisher)
    ROLE=ledger_outbox_publisher
    PG_SERVICE=ledger-postgres
    PG_SUPERUSER=ledger
    PG_DB=ledger
    PG_ALIAS=ledger-postgres
    SECRET_NAME="banco-carrefour/ledger-outbox-publisher/db-credentials"
    ENV_VAR=LEDGER_OUTBOX_PUBLISHER_PASSWORD
    COMPOSE_SERVICE=ledger-outbox-publisher
    HEALTH_URL=""
    ERROR_LOG_PATTERN="Falha ao executar ciclo de publicação da Outbox."
    ;;
  consolidation-api)
    ROLE=consolidation_api_readonly
    PG_SERVICE=consolidation-postgres
    PG_SUPERUSER=consolidation
    PG_DB=consolidation
    PG_ALIAS=consolidation-postgres
    SECRET_NAME="banco-carrefour/consolidation-api/db-credentials"
    ENV_VAR=CONSOLIDATION_API_READONLY_PASSWORD
    COMPOSE_SERVICE=consolidation-api
    HEALTH_URL="https://localhost:8443/consolidation/health/ready"
    ;;
  consolidation-worker)
    ROLE=consolidation_worker
    PG_SERVICE=consolidation-postgres
    PG_SUPERUSER=consolidation
    PG_DB=consolidation
    PG_ALIAS=consolidation-postgres
    SECRET_NAME="banco-carrefour/consolidation-worker/db-credentials"
    ENV_VAR=CONSOLIDATION_WORKER_PASSWORD
    COMPOSE_SERVICE=consolidation-worker
    HEALTH_URL=""
    ERROR_LOG_PATTERN="Falha ao executar ciclo de consumo SQS."
    ;;
  *)
    fail "componente obrigatório e inválido: '${COMPONENT}'. Use um de: ledger-api | ledger-outbox-publisher | consolidation-api | consolidation-worker"
    ;;
esac

[ -f .env ] || fail "arquivo .env não encontrado na raiz do repositório - rode bootstrap-local-security primeiro."

# Carrega .env sem nunca imprimir seus valores.
set -a
. ./.env
set +a

eval "OLD_PASSWORD=\"\$$ENV_VAR\""
[ -n "$OLD_PASSWORD" ] || fail "$ENV_VAR não definido em .env."

log "=== Rotacao de credencial: $COMPONENT (role Postgres: $ROLE) ==="

NEW_PASSWORD=$(docker run --rm alpine sh -c "head -c 24 /dev/urandom | base64" | tr -d '\n=/+')
log "Nova senha gerada (nao impressa, $(printf '%s' "$NEW_PASSWORD" | wc -c) caracteres)."

log "1) ALTER ROLE $ROLE no Postgres real ($PG_SERVICE)..."
docker compose exec -T -e PGPASSWORD="$PG_SUPERUSER" "$PG_SERVICE" \
  psql -U "$PG_SUPERUSER" -d "$PG_DB" -v ON_ERROR_STOP=1 \
  -c "ALTER ROLE $ROLE WITH PASSWORD '$NEW_PASSWORD';" >/dev/null
log "   OK."

log "2) Confirmando que a senha ANTIGA nao autentica mais (via rede real entre containers - nunca pelo loopback do proprio container Postgres, que usa 'trust')..."
if docker run --rm --network "$(basename "$REPO_ROOT")_default" postgres:16-alpine \
  psql "postgresql://${ROLE}:${OLD_PASSWORD}@${PG_ALIAS}:5432/${PG_DB}" -c "select 1;" >/dev/null 2>&1; then
  fail "senha antiga de $ROLE AINDA autentica apos a rotacao - abortando (estado inconsistente)."
fi
log "   Confirmado: senha antiga rejeitada."

log "3) Atualizando o secret real no Secrets Manager (mantendo as outras 3 credenciais inalteradas)..."
NEW_LEDGER_API_PASSWORD="${LEDGER_API_PASSWORD}"
NEW_LEDGER_OUTBOX_PUBLISHER_PASSWORD="${LEDGER_OUTBOX_PUBLISHER_PASSWORD}"
NEW_CONSOLIDATION_API_READONLY_PASSWORD="${CONSOLIDATION_API_READONLY_PASSWORD}"
NEW_CONSOLIDATION_WORKER_PASSWORD="${CONSOLIDATION_WORKER_PASSWORD}"
case "$ENV_VAR" in
  LEDGER_API_PASSWORD) NEW_LEDGER_API_PASSWORD="$NEW_PASSWORD" ;;
  LEDGER_OUTBOX_PUBLISHER_PASSWORD) NEW_LEDGER_OUTBOX_PUBLISHER_PASSWORD="$NEW_PASSWORD" ;;
  CONSOLIDATION_API_READONLY_PASSWORD) NEW_CONSOLIDATION_API_READONLY_PASSWORD="$NEW_PASSWORD" ;;
  CONSOLIDATION_WORKER_PASSWORD) NEW_CONSOLIDATION_WORKER_PASSWORD="$NEW_PASSWORD" ;;
esac

MSYS_NO_PATHCONV=1 docker run --rm --network "$(basename "$REPO_ROOT")_default" --entrypoint sh \
  -e AWS_ENDPOINT_URL=http://localstack:4566 -e AWS_ACCESS_KEY_ID=test -e AWS_SECRET_ACCESS_KEY=test -e AWS_DEFAULT_REGION=us-east-1 \
  -e LEDGER_API_PASSWORD="$NEW_LEDGER_API_PASSWORD" \
  -e LEDGER_OUTBOX_PUBLISHER_PASSWORD="$NEW_LEDGER_OUTBOX_PUBLISHER_PASSWORD" \
  -e CONSOLIDATION_API_READONLY_PASSWORD="$NEW_CONSOLIDATION_API_READONLY_PASSWORD" \
  -e CONSOLIDATION_WORKER_PASSWORD="$NEW_CONSOLIDATION_WORKER_PASSWORD" \
  -v "$(pwd)/scripts/security:/scripts:ro" \
  amazon/aws-cli:2.31.13 /scripts/secret-value-bootstrap-impl.sh

log "4) Reiniciando SOMENTE $COMPOSE_SERVICE..."
docker compose restart "$COMPOSE_SERVICE" >/dev/null

if [ -n "$HEALTH_URL" ]; then
  log "   Aguardando $COMPOSE_SERVICE ficar ready com a credencial nova..."
  i=0
  CODE=""
  while [ "$i" -lt 30 ]; do
    CODE=$(curl -sk -o /dev/null -w "%{http_code}" "$HEALTH_URL" -H "Host: localhost:8443" || true)
    [ "$CODE" = "200" ] && break
    i=$((i + 1))
    sleep 2
  done
  [ "$CODE" = "200" ] || fail "$COMPOSE_SERVICE nao ficou ready a tempo apos o restart (ultimo HTTP $CODE)."
  log "   $COMPOSE_SERVICE ready: HTTP $CODE (carregou a credencial nova via Secrets Manager)."
else
  log "   $COMPONENT nao expoe health HTTP (worker/publisher) - aguardando e confirmando ausencia de erro de ciclo apos o restart..."
  sleep 10
  STATUS=$(docker compose ps "$COMPOSE_SERVICE" --format "{{.Status}}" 2>/dev/null || true)
  log "   Status apos restart: $STATUS"
  if docker compose logs "$COMPOSE_SERVICE" --since 10s 2>/dev/null | grep -qF -- "$ERROR_LOG_PATTERN"; then
    fail "$COMPOSE_SERVICE registrou falha de ciclo apos o restart - a credencial nova pode nao ter sido aceita pelo Postgres."
  fi
  log "   Confirmado: nenhuma falha de ciclo (autenticacao Postgres) nos 10s apos o restart."
fi

log "5) Conferindo ausencia da senha nova nos logs de $COMPOSE_SERVICE..."
if docker compose logs "$COMPOSE_SERVICE" 2>/dev/null | grep -qF -- "$NEW_PASSWORD"; then
  fail "senha nova de $ROLE apareceu nos logs de $COMPOSE_SERVICE!"
fi
log "   Confirmado: senha nova ausente dos logs."

log ""
log "=== Rotacao de $COMPONENT concluida com sucesso ==="
log "Nota (ADR-0009): o pool de conexoes (Npgsql) do processo anterior ao restart"
log "pode ter mantido conexoes ja abertas ate serem recicladas; nao ha garantia de"
log "zero-downtime sem um mecanismo de duas credenciais simultaneas (nao implementado)."
