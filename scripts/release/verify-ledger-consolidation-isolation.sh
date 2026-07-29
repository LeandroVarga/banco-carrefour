#!/bin/sh
# Prova de isolamento Ledger vs Consolidation contra a stack de
# RELEASE-QUALIFICATION (docker-compose.release-qualification.yml),
# rodando via HTTP contra o edge-proxy - mesma propriedade ja comprovada
# em processo por
# tests/Consolidation.IntegrationTests/SystemFlowIntegrationTests.cs
# (Consolidation_indisponivel_nao_bloqueia_Ledger_e_converge_apos_recuperacao_sem_duplicar_saldo),
# mas aqui contra containers reais das imagens build-once, nao contra
# fixtures in-process - os dois nunca sao o mesmo mecanismo de execucao,
# entao esta prova precisa existir separadamente (ver ADR-0013).
#
# Usado por release-qualification (scripts/ci/run-release-qualification.sh)
# apos a stack estar de pe e saudavel. Obtem seu
# proprio token via client_credentials (mesmo client "merchant-a-test-client"
# do realm importado em infra/keycloak/realm, mesma tecnica de
# scripts/security/keycloak-bootstrap.sh) - nunca recebe um token
# pre-obtido, para nao depender de um mecanismo externo de emissao.
#
# Uso:
#   sh scripts/release/verify-ledger-consolidation-isolation.sh <base-url-https-do-edge-proxy> <client-secret-de-merchant-a-test-client>
set -eu

fail() { echo "verify-ledger-consolidation-isolation: FALHA: $1" >&2; exit 1; }
log() { printf '%s\n' "$1"; }

[ $# -ge 2 ] || fail "uso: $0 <base-url> <merchant-a-client-secret>"
BASE_URL="$1"
CLIENT_SECRET="$2"
REALM="banco-carrefour"
CLIENT_ID="merchant-a-test-client"

# O edge-proxy roteia por vhost (infra/edge-proxy/default.conf.template):
# "server_name localhost" só conhece /ledger/ e /consolidation/ (qualquer
# outro path cai em "location / { return 404; }"); Keycloak só é
# alcançável pelo vhost dedicado "server_name keycloak.localhost" - achado
# real, reproduzido (curl 404 ao pedir o token em ${BASE_URL}/realms/...
# quando BASE_URL usa o host "localhost"). O token PRECISA ser obtido via
# esse segundo vhost, mesma porta.
KEYCLOAK_BASE_URL=$(printf '%s' "$BASE_URL" | sed 's#//localhost#//keycloak.localhost#')

log "=== Obtendo token via client_credentials (${CLIENT_ID}) ==="
# "scope=ledger.write" e obrigatorio: merchant-a-test-client so declara
# ledger.write/consolidation.read como optionalClientScopes (nunca
# defaultClientScopes) no realm importado (infra/keycloak/realm) - sem
# pedir o scope explicitamente, o token nunca ganha o audience mapper
# "ledger-api-audience", e Ledger.Api rejeita com 401 (achado real,
# reproduzido). Mesmo padrao ja documentado em
# docs/operations/runbook-demonstracao-local.md.
TOKEN=$(curl -k -fsS -X POST "${KEYCLOAK_BASE_URL}/realms/${REALM}/protocol/openid-connect/token" \
  -d "grant_type=client_credentials" -d "client_id=${CLIENT_ID}" -d "client_secret=${CLIENT_SECRET}" \
  -d "scope=ledger.write" \
  | python3 -c "import json,sys; print(json.load(sys.stdin)['access_token'])")
[ -n "$TOKEN" ] || fail "nao foi possivel obter token de acesso."

# "pause"/"unpause" (congela os processos via cgroups, sem parar/remover
# o container) em vez de "stop"/"start": achado real, reproduzido -
# "docker compose start" reavalia o grafo de dependencias (depends_on
# service_completed_successfully) dos 2 servicos, o que re-executa jobs
# one-off ja concluidos (terraform-provisioner, *-migrations,
# *-grants-bootstrap) - terraform-provisioner nao e idempotente entre
# execucoes (estado Terraform sempre efemero, "terraform apply" tenta
# recriar recursos do LocalStack que ja existem e falha). "pause"/"unpause"
# nunca aciona esse grafo (documentacao oficial do Docker Compose nao
# associa nenhum dos dois a depends_on), e ainda torna os 2 servicos
# genuinamente inalcancaveis, cumprindo o mesmo objetivo do teste.
log "=== MARCO 1: pausando Consolidation.Api e Consolidation.Worker (Ledger deve continuar disponivel) ==="
docker compose -f deploy/compose/docker-compose.release-qualification.yml pause consolidation-api consolidation-worker

log "=== MARCO 2: registrando lancamento no Ledger com Consolidation indisponivel ==="
# Corpo exato de CreateEntryRequest (contracts/openapi.yaml,
# additionalProperties:false): "type"/"amount" (string decimal)/"currency"/
# "occurredAt" - NUNCA "merchantId" (derivado exclusivamente do token,
# rejeitado como propriedade desconhecida se enviado). "Idempotency-Key" e
# cabecalho obrigatorio (min 8 chars) - achado real, reproduzido (400 sem
# essas correcoes).
HTTP_STATUS=$(curl -k -s -o /dev/null -w '%{http_code}' \
  -X POST "${BASE_URL}/ledger/entries" \
  -H "Authorization: Bearer ${TOKEN}" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: release-qualification-isolation-check-001" \
  -d '{"type":"CREDIT","amount":"100.00","currency":"BRL","occurredAt":"2026-01-01T00:00:00Z"}')

[ "$HTTP_STATUS" = "201" ] || fail "Ledger deveria aceitar o lancamento (201) mesmo com Consolidation indisponivel - obtido ${HTTP_STATUS}."
log "  Ledger aceitou o lancamento (201) com Consolidation indisponivel - isolamento confirmado."

log "=== MARCO 3: despausando Consolidation.Api e Consolidation.Worker ==="
docker compose -f deploy/compose/docker-compose.release-qualification.yml unpause consolidation-api consolidation-worker

log "=== verify-ledger-consolidation-isolation: PASSOU ==="
