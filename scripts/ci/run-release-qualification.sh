#!/bin/sh
# Orquestra a unica capacidade de "release-qualification" (ver ADR-0013):
# sobe deploy/compose/docker-compose.release-qualification.yml com as
# MESMAS 4 imagens build-once ja validadas (artifacts/sbom/images.json,
# nunca reconstroi, nunca faz pull de nenhum registry), roda as validacoes
# funcionais/seguranca/carga, e SEMPRE derruba a stack ao final (sucesso
# ou falha) - nunca deixa uma stack de qualificacao orfa para tras.
#
# O que esta capacidade PROVA: build-once, non-root (ja provado antes
# desta stack subir - scripts/ci/verify-nonroot-from-manifest.sh), health
# checks, inicializacao de banco, autenticacao/autorizacao via Keycloak,
# isolamento por merchant_id, caminho de escrita do Ledger, Outbox
# transacional, entrega compativel com SQS via LocalStack, projecao do
# Consolidation, isolamento Consolidation-indisponivel-vs-Ledger-disponivel,
# smoke de 50 RPS/<=5% de falha.
#
# O que esta capacidade NUNCA prova: comportamento real de ECS, RDS
# Multi-AZ, IAM de producao, ALB, WAF ou qualquer outro servico AWS -
# release-qualification e Docker Compose local, nunca uma execucao AWS
# real (essa distincao nunca deve ser confundida na documentacao nem no
# relatorio de evidencias).
#
# Pre-requisitos (build-once ja concluido nesta mesma sessao/job):
#   1) scripts/ci/build-images-for-supply-chain.sh
#   2) scripts/ci/verify-nonroot-from-manifest.sh, generate-sboms.sh,
#      scan-images.sh, validate-supply-chain-artifacts.sh
#
# Uso:
#   sh scripts/ci/run-release-qualification.sh
#
# Variaveis de ambiente opcionais:
#   RELEASE_IMAGES_FILE (default: artifacts/sbom/images.json)
#   QUALIFICATION_SKIP_LOAD_SMOKE=1 (pula o smoke de 50 RPS - uso exclusivo
#     de ambientes sem .NET SDK disponivel, nunca em uma execucao real)
set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

log() { printf '%s\n' "$1"; }
fail() { echo "run-release-qualification: FALHA: $1" >&2; exit 1; }

COMPOSE_FILE="deploy/compose/docker-compose.release-qualification.yml"
IMAGES_FILE="${RELEASE_IMAGES_FILE:-artifacts/sbom/images.json}"

[ -f "$IMAGES_FILE" ] || fail "${IMAGES_FILE} nao encontrado - rode build-images-for-supply-chain.sh primeiro."

mkdir -p artifacts

# As 4 variaveis *_IMAGE_REF apontam para a imagem LOCAL build-once (nunca
# um registry remoto) - lidas do MESMO images.json que build-once produziu.
LEDGER_API_IMAGE_REF=$(python3 -c "import json; d=json.load(open('$IMAGES_FILE')); print(next(e['image'] for e in d if e['component']=='ledger-api'))")
LEDGER_OUTBOX_PUBLISHER_IMAGE_REF=$(python3 -c "import json; d=json.load(open('$IMAGES_FILE')); print(next(e['image'] for e in d if e['component']=='ledger-outbox-publisher'))")
CONSOLIDATION_API_IMAGE_REF=$(python3 -c "import json; d=json.load(open('$IMAGES_FILE')); print(next(e['image'] for e in d if e['component']=='consolidation-api'))")
CONSOLIDATION_WORKER_IMAGE_REF=$(python3 -c "import json; d=json.load(open('$IMAGES_FILE')); print(next(e['image'] for e in d if e['component']=='consolidation-worker'))")
export LEDGER_API_IMAGE_REF LEDGER_OUTBOX_PUBLISHER_IMAGE_REF CONSOLIDATION_API_IMAGE_REF CONSOLIDATION_WORKER_IMAGE_REF

log "=== Gerando credenciais efemeras (nunca reaproveitadas entre execucoes) ==="
eval "$(sh scripts/release/generate-ephemeral-environment-secrets.sh)"
export LEDGER_MIGRATION_PASSWORD LEDGER_API_PASSWORD LEDGER_OUTBOX_PUBLISHER_PASSWORD \
  CONSOLIDATION_MIGRATION_PASSWORD CONSOLIDATION_WORKER_PASSWORD CONSOLIDATION_API_READONLY_PASSWORD \
  KEYCLOAK_DB_PASSWORD KC_BOOTSTRAP_ADMIN_CLIENT_ID KC_BOOTSTRAP_ADMIN_CLIENT_SECRET

TEARDOWN_DONE=0
teardown() {
  if [ "$TEARDOWN_DONE" -eq 1 ]; then
    return 0
  fi
  TEARDOWN_DONE=1
  log "=== Coletando evidencia (logs dos servicos) ==="
  docker compose -f "$COMPOSE_FILE" logs --no-color > artifacts/release-qualification-evidence.log 2>&1 || true
  log "=== Encerrando a stack de release-qualification (sempre, mesmo em falha) ==="
  # "--profile manual-tools": achado real, reproduzido - "docker compose
  # down" (sem "--profile") trata servicos por tras de profile
  # (keycloak-bootstrap, dotnet-sdk) como "desabilitados", NUNCA como
  # residuo/orfao (mesmo com --remove-orphans, cujo predicado exclui
  # explicitamente servicos desabilitados por profile) - ficam para tras
  # sem essa flag, mesmo depois de terem sido executados nesta mesma
  # sessao via "up keycloak-bootstrap"/"run dotnet-sdk".
  docker compose -f "$COMPOSE_FILE" --profile manual-tools down -v || true
}
trap teardown EXIT INT TERM

log "=== Subindo a stack de release-qualification (imagens locais build-once, nunca build/pull) ==="
docker compose -f "$COMPOSE_FILE" up -d --wait

log "=== Rodando keycloak-bootstrap (emite os client secrets reais de teste) ==="
# HOST_UID/HOST_GID: keycloak-bootstrap roda como root num container Alpine
# contra o bind mount ./.local/security - sem isso, .env.security ficaria
# root:root no host (mesmo achado real de scripts/ci/run-performance-smoke.sh).
HOST_UID="$(id -u 2>/dev/null || echo '')"
HOST_GID="$(id -g 2>/dev/null || echo '')"
export HOST_UID HOST_GID
docker compose -f "$COMPOSE_FILE" up keycloak-bootstrap
[ -f .local/security/.env.security ] || fail ".local/security/.env.security nao foi criado por keycloak-bootstrap - ver logs do servico acima."
# "set -a" exporta automaticamente toda variavel atribuida durante o source
# (mesmo padrao ja usado por scripts/ci/run-performance-smoke.sh) - sem
# isso, "VAR=valor" simples (sem "export") em .env.security fica local a
# este shell e nunca chegaria a processos filhos (dotnet run, abaixo).
set -a
# shellcheck disable=SC1091
. ./.local/security/.env.security
set +a
[ -n "${MERCHANT_A_TEST_CLIENT_SECRET:-}" ] || fail "MERCHANT_A_TEST_CLIENT_SECRET ausente apos keycloak-bootstrap."

log "=== Validacao: isolamento Ledger vs Consolidation ==="
sh scripts/release/verify-ledger-consolidation-isolation.sh https://localhost:8443 "${MERCHANT_A_TEST_CLIENT_SECRET}"

if [ "${QUALIFICATION_SKIP_LOAD_SMOKE:-0}" != "1" ]; then
  log "=== Validacao: contrato, seguranca e smoke de 50 RPS/<=5% falha ==="
  # Consolidation.LoadTests roda DENTRO do servico "dotnet-sdk" do proprio
  # compose (mesma tecnica ja provada por
  # scripts/ci/run-performance-smoke.sh contra docker-compose.yml) - nunca
  # como processo solto no host: os defaults de Program.cs (ex.:
  # "http://consolidation-api:8080", "Host=consolidation-postgres;...")
  # sao nomes de servico DNS internos do compose, inalcancaveis de fora da
  # rede (achado real, reproduzido: SocketException/"No such host is
  # known" ao rodar via "dotnet run" direto no host). "--no-deps": a
  # stack ja esta de pe e saudavel, nunca precisa reavaliar depends_on.
  # Alvo DIRETO do servico (nunca o edge-proxy): a RNF de 50 RPS mede a
  # capacidade do servico, nao o rate limit do WAF na borda (20 r/s) - ver
  # ADR-0012.
  MSYS_NO_PATHCONV=1 docker compose -f "$COMPOSE_FILE" run --rm --no-deps \
    -e LOADTEST_RESULT_JSON_PATH=/workspace/artifacts/release-qualification-performance-smoke.json \
    -e LOADTEST_RESULT_MARKDOWN_PATH=/workspace/artifacts/release-qualification-performance-smoke.md \
    -e MERCHANT_A_TEST_CLIENT_SECRET="$MERCHANT_A_TEST_CLIENT_SECRET" \
    -e MERCHANT_B_TEST_CLIENT_SECRET="${MERCHANT_B_TEST_CLIENT_SECRET:-}" \
    dotnet-sdk dotnet run --project tests/Consolidation.LoadTests
else
  log "=== Smoke de 50 RPS pulado (QUALIFICATION_SKIP_LOAD_SMOKE=1) ==="
fi

log ""
log "=== release-qualification: PASSOU ==="
