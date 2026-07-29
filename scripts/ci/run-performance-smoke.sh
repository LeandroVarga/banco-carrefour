#!/bin/sh
# Gate de smoke de desempenho: sobe a stack real via
# Docker Compose (mesmos 5 componentes do runbook de demonstracao local),
# aguarda prontidao, garante secrets reais do Keycloak via
# keycloak-bootstrap.sh (nunca bypass de Secrets Manager/SSM/Keycloak) e
# executa tests/Consolidation.LoadTests contra o Consolidation.Api real na
# rede interna do Compose (nao pelo edge-proxy - ver ADR-0012: a RNF de 50
# RPS mede a capacidade do servico, nao o rate limit do WAF na borda).
#
# Reutilizavel localmente e pelo job "performance-smoke-gate" do workflow -
# nenhuma logica de orquestracao duplicada la.
#
# Uso:
#   sh scripts/ci/run-performance-smoke.sh
#
# Variaveis de ambiente aceitas (todas opcionais, com o mesmo padrao de
# tests/Consolidation.LoadTests, ver docs/operations/teste-de-carga-consolidado.md):
#   LOADTEST_RPS, LOADTEST_RAMP_SECONDS, LOADTEST_DURATION_SECONDS,
#   LOADTEST_MIN_OBSERVED_RPS, LOADTEST_MAX_FAILURE_RATE.
#
# Pre-requisito: scripts/security/bootstrap-local-security.sh ja executado
# ao menos uma vez (gera .env e os certificados TLS locais). NUNCA gera
# .local/security/.env.security - esse arquivo so existe depois que a
# stack sobe e scripts/security/keycloak-bootstrap.sh reconcilia o realm
# real (mais abaixo, passo 3); exigi-lo antes disso tornaria a primeira
# execucao num checkout limpo impossivel (achado real do CI hospedado).
set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

log() { printf '%s\n' "$1"; }
fail() { echo "run-performance-smoke: FALHA: $1" >&2; exit 1; }

# Nunca imprime senhas/segredos/tokens - redige qualquer coisa que pareça
# com um valor sensivel antes de exibir logs de container.
sanitize_log() {
  sed -E 's/([Pp]assword=)[^;[:space:]]*/\1[REDACTED]/g; s/([Ss]ecret[A-Za-z]*=)[^;[:space:]&"]*/\1[REDACTED]/g; s/([Tt]oken=)[^;[:space:]&"]*/\1[REDACTED]/g'
}

# Diagnostico de prontidao - roda SOMENTE quando a espera por prontidao
# falha (nunca em execucao bem-sucedida, para manter o log conciso), e
# SEMPRE antes do cleanup (chamado diretamente do branch de falha, antes
# de "fail" encerrar o processo - o cleanup so roda depois, via o trap de
# EXIT). Nunca altera o exit code final: so imprime evidencia. Achado
# real do CI hospedado: a mensagem generica "nao ficou pronto a tempo"
# nao dizia QUAL camada (Consolidation.Api direto, edge-proxy, banco,
# Secrets Manager/SSM) estava falhando, nem deixava nenhum log de
# container disponivel para diagnostico - esta funcao existe para
# corrigir essa lacuna permanentemente, independente da causa
# comportamental de uma falha futura.
diagnose_readiness_failure() {
  log ""
  log "=== Diagnostico de prontidao (falha de infraestrutura/bootstrap) ==="

  log ""
  log "-- Consolidation.Api direto (rede interna do Compose, nunca exposto em porta de host) --"
  MSYS_NO_PATHCONV=1 docker compose run --rm --no-deps dotnet-sdk sh -c '
    live_response=$(curl -s -w "\nHTTPSTATUS:%{http_code}" http://consolidation-api:8080/health/live 2>/dev/null || printf "HTTPSTATUS:ERR")
    live_code=$(printf "%s" "$live_response" | sed -n "s/.*HTTPSTATUS://p" | tail -1)
    ready_response=$(curl -s -w "\nHTTPSTATUS:%{http_code}" http://consolidation-api:8080/health/ready 2>/dev/null || printf "HTTPSTATUS:ERR")
    ready_code=$(printf "%s" "$ready_response" | sed -n "s/.*HTTPSTATUS://p" | tail -1)
    ready_body=$(printf "%s" "$ready_response" | sed "s/HTTPSTATUS:[A-Z0-9]*$//" | head -c 500 | tr -d "\n")
    printf "  live=%s ready=%s\n" "$live_code" "$ready_code"
    printf "  ready body: %s\n" "$ready_body"
  ' 2>&1 || log "  (nao foi possivel executar a checagem direta - Consolidation.Api pode nao estar acessivel na rede do Compose)"

  log ""
  log "-- edge-proxy (borda TLS, https://localhost:8443) --"
  edge_live_response=$(curl -sk -w '\nHTTPSTATUS:%{http_code}' https://localhost:8443/consolidation/health/live -H "Host: localhost:8443" 2>/dev/null || printf 'HTTPSTATUS:ERR')
  edge_live_code=$(printf '%s' "$edge_live_response" | sed -n 's/.*HTTPSTATUS://p' | tail -1)
  edge_ready_response=$(curl -sk -w '\nHTTPSTATUS:%{http_code}' https://localhost:8443/consolidation/health/ready -H "Host: localhost:8443" 2>/dev/null || printf 'HTTPSTATUS:ERR')
  edge_ready_code=$(printf '%s' "$edge_ready_response" | sed -n 's/.*HTTPSTATUS://p' | tail -1)
  edge_ready_body=$(printf '%s' "$edge_ready_response" | sed 's/HTTPSTATUS:[A-Z0-9]*$//' | head -c 500 | tr -d '\n')
  log "  live=${edge_live_code} ready=${edge_ready_code}"
  log "  ready body: ${edge_ready_body}"

  log ""
  log "-- docker compose ps --all --"
  docker compose ps -a || true

  log ""
  log "-- Logs relevantes (sanitizados - sem senhas/segredos/tokens/.env) --"
  for svc in consolidation-api consolidation-postgres consolidation-migrations consolidation-db-grants-bootstrap secret-value-bootstrap terraform-provisioner edge-proxy; do
    log "  ---- ${svc} (ultimas 40 linhas) ----"
    docker compose logs --no-color --tail=40 "$svc" 2>/dev/null | sanitize_log || true
  done

  log ""
  log "=== Fim do diagnostico de prontidao ==="
}

[ -f .env ] || fail "arquivo .env nao encontrado - rode scripts/security/bootstrap-local-security.sh primeiro."

mkdir -p artifacts

CLEANED_UP=0
cleanup() {
  if [ "$CLEANED_UP" -eq 1 ]; then
    return 0
  fi
  CLEANED_UP=1
  log "Limpando a stack (docker compose down, escopado a este projeto - sem -v, sem afetar outros projetos/containers)..."
  docker compose down || true
}
trap cleanup EXIT INT TERM

log "1) Subindo a stack real (mesmos componentes do runbook de demonstracao local)..."
docker compose up -d --build ledger-api ledger-outbox-publisher consolidation-worker consolidation-api edge-proxy

log "2) Aguardando o edge-proxy/Consolidation.Api ficarem prontos..."
READY=0
i=0
while [ "$i" -lt 60 ]; do
  CODE=$(curl -sk -o /dev/null -w "%{http_code}" https://localhost:8443/consolidation/health/ready -H "Host: localhost:8443" || true)
  if [ "$CODE" = "200" ]; then
    READY=1
    break
  fi
  i=$((i + 1))
  sleep 2
done
if [ "$READY" -ne 1 ]; then
  diagnose_readiness_failure
  fail "consolidation-api nao ficou pronto a tempo (falha de infraestrutura/bootstrap - nao deve ser contada como falha elegivel do smoke)."
fi
log "   Pronto."

log "3) Garantindo secrets reais e atuais do Keycloak (keycloak-bootstrap - nunca bypass)..."
# HOST_UID/HOST_GID: keycloak-bootstrap roda como root num container Alpine
# contra o bind mount ./.local/security - sem isso, .env.security ficaria
# root:root no runner hospedado (achado real: "permission denied" na
# limpeza subsequente). O proprio script aplica o chown ao usuario que
# invocou este processo.
HOST_UID="$(id -u 2>/dev/null || echo '')"
HOST_GID="$(id -g 2>/dev/null || echo '')"
export HOST_UID HOST_GID
docker compose up keycloak-bootstrap

[ -f .local/security/.env.security ] || fail ".local/security/.env.security nao foi criado por keycloak-bootstrap - ver logs do servico keycloak-bootstrap acima."

# shellcheck disable=SC1091
set -a
. ./.local/security/.env.security
set +a
[ -n "${MERCHANT_A_TEST_CLIENT_SECRET:-}" ] || fail "MERCHANT_A_TEST_CLIENT_SECRET ausente apos keycloak-bootstrap."

log "4) Executando o smoke de desempenho (Consolidation.LoadTests, alvo direto do servico)..."
GITHUB_SHA_LOCAL="${GITHUB_SHA:-$(git rev-parse HEAD)}"

set +e
MSYS_NO_PATHCONV=1 docker compose run --rm --no-deps \
  -e LOADTEST_RPS="${LOADTEST_RPS:-50}" \
  -e LOADTEST_RAMP_SECONDS="${LOADTEST_RAMP_SECONDS:-10}" \
  -e LOADTEST_DURATION_SECONDS="${LOADTEST_DURATION_SECONDS:-60}" \
  -e LOADTEST_MIN_OBSERVED_RPS="${LOADTEST_MIN_OBSERVED_RPS:-50}" \
  -e LOADTEST_MAX_FAILURE_RATE="${LOADTEST_MAX_FAILURE_RATE:-0.05}" \
  -e GITHUB_SHA="$GITHUB_SHA_LOCAL" \
  -e LOADTEST_RESULT_JSON_PATH=/workspace/artifacts/performance-smoke.json \
  -e LOADTEST_RESULT_MARKDOWN_PATH=/workspace/artifacts/performance-smoke.md \
  -e MERCHANT_A_TEST_CLIENT_SECRET="$MERCHANT_A_TEST_CLIENT_SECRET" \
  -e MERCHANT_B_TEST_CLIENT_SECRET="${MERCHANT_B_TEST_CLIENT_SECRET:-}" \
  dotnet-sdk dotnet run --project tests/Consolidation.LoadTests
LOADTEST_EXIT=$?
set -e

[ -f artifacts/performance-smoke.json ] || fail "evidencia JSON nao foi gerada - tratando como falha de infraestrutura do smoke."

log ""
log "5) Verificando o veredito do gate de CI (RPS agendado + falhas elegiveis <= limite, sem trava de latencia)..."
VERDICT=$(grep -o '"Verdict": *"[a-z]*"' artifacts/performance-smoke.json | sed 's/.*"\([a-z]*\)"$/\1/')
log "   Veredito: ${VERDICT}"

if [ "$VERDICT" != "pass" ]; then
  fail "smoke de desempenho nao atingiu o criterio do gate (ver artifacts/performance-smoke.json)."
fi

log ""
log "=== Smoke de desempenho concluido com sucesso (dotnet exit ${LOADTEST_EXIT}) ==="
