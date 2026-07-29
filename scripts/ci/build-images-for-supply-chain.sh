#!/bin/sh
# Builda as 4 imagens de negocio reais (Ledger.Api, Ledger.OutboxPublisher,
# Consolidation.Api, Consolidation.Worker) + 1 artefato operacional
# (migration-runner), nunca um 5o workload de negocio no
# modelo de dominio arquitetural, mas construido pela mesma cadeia) uma
# unica vez, com tag deterministica atrelada ao commit de origem, para
# reuso por SBOM, scan de vulnerabilidade e pelo gate non-root. Nunca publica (sem docker push).
#
# Uso:
#   sh scripts/ci/build-images-for-supply-chain.sh
#
# Gera artifacts/sbom/images.json (manifesto commit -> imagem -> image ID),
# consumido por generate-sboms.sh, scan-images.sh e
# validate-supply-chain-artifacts.sh.
#
# o script exige
# arvore de trabalho limpa ANTES de buildar (scripts/ci/require-clean-source-tree.sh,
# testado isoladamente por scripts/ci/test-provenance-guards.sh). Sem essa
# checagem, o "docker build -f ... ." copia o CONTEUDO ATUAL do diretorio
# (nao o commit registrado), entao uma arvore suja produziria uma imagem
# cujo conteudo real diverge do sourceCommit anotado no manifesto -
# exatamente o defeito de provenance identificado no audit (imagens do
# bloco 2 atribuidas ao ultimo commit do bloco 1).
set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

log() { printf '%s\n' "$1"; }
fail() { echo "build-images-for-supply-chain: FALHA: $1" >&2; exit 1; }

sh "$REPO_ROOT/scripts/ci/require-clean-source-tree.sh" "$REPO_ROOT" \
  || fail "arvore de trabalho nao esta limpa o suficiente para um build reproduzivel - ver mensagem acima."

SOURCE_COMMIT="$(git rev-parse HEAD)"
BUILD_TIMESTAMP="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
TARGET_PLATFORM="$(docker info --format '{{.OSType}}/{{.Architecture}}' 2>/dev/null || echo 'unknown/unknown')"
BUILD_CONTEXT="."

# Labels OCI padrao (org.opencontainers.image.*) gravados na propria
# imagem no momento do build - usados pelo manifesto de release (ECR)
# para verificar ociRevision por INSPECAO REAL da
# imagem (docker image inspect), nunca apenas por suposicao de que o
# commit anotado no manifesto de build corresponde ao que foi de fato
# gravado na imagem.
SOURCE_URL="$(git config --get remote.origin.url 2>/dev/null || true)"
case "$SOURCE_URL" in
  git@github.com:*) SOURCE_URL="https://github.com/$(printf '%s' "$SOURCE_URL" | sed -e 's#^git@github.com:##' -e 's#\.git$##')" ;;
  https://github.com/*.git) SOURCE_URL="${SOURCE_URL%.git}" ;;
  "") SOURCE_URL="https://github.com/LeandroVarga/banco-carrefour" ;;
esac

mkdir -p artifacts/sbom

MANIFEST=artifacts/sbom/images.json
echo "[" > "$MANIFEST"
FIRST=1

build_one() {
  NAME="$1"
  DOCKERFILE="$2"
  # Tag pela SHA COMPLETA (nao abreviada): uma SHA curta pode se tornar
  # ambigua conforme o repositorio cresce (colisao de prefixo) - o
  # identificador de origem tem que ser inequivoco.
  TAG="banco-carrefour-${NAME}:${SOURCE_COMMIT}"

  log "=== ${NAME} (${DOCKERFILE}) -> ${TAG} ==="
  docker build -f "$DOCKERFILE" -t "$TAG" \
    --label "org.opencontainers.image.source=${SOURCE_URL}" \
    --label "org.opencontainers.image.revision=${SOURCE_COMMIT}" \
    --label "org.opencontainers.image.title=banco-carrefour-${NAME}" \
    --label "org.opencontainers.image.version=sha-${SOURCE_COMMIT}" \
    "$BUILD_CONTEXT" >/dev/null
  IMAGE_ID=$(docker image inspect "$TAG" --format '{{.Id}}')
  BASE_IMAGE=$(grep -m1 "^FROM.*AS runtime" "$DOCKERFILE" | awk '{print $2}')
  BUILD_CMD="docker build -f ${DOCKERFILE} -t ${TAG} ${BUILD_CONTEXT}"

  log "  image ID: ${IMAGE_ID}"
  log "  base (runtime stage): ${BASE_IMAGE}"

  if [ "$FIRST" -eq 0 ]; then
    echo "," >> "$MANIFEST"
  fi
  FIRST=0
  cat >> "$MANIFEST" <<EOF
  {
    "component": "${NAME}",
    "dockerfile": "${DOCKERFILE}",
    "image": "${TAG}",
    "imageId": "${IMAGE_ID}",
    "baseImage": "${BASE_IMAGE}",
    "sourceCommit": "${SOURCE_COMMIT}",
    "sourceTreeClean": true,
    "buildContext": "${BUILD_CONTEXT}",
    "targetPlatform": "${TARGET_PLATFORM}",
    "buildTimestamp": "${BUILD_TIMESTAMP}",
    "buildCommand": "${BUILD_CMD}",
    "sbomPath": "artifacts/sbom/${NAME}.cyclonedx.json",
    "vulnerabilityReportPath": "artifacts/vulnerability/${NAME}.trivy.json",
    "published": false
  }
EOF
}

build_one ledger-api src/Ledger/Ledger.Api/Dockerfile
build_one ledger-outbox-publisher src/Ledger/Ledger.OutboxPublisher/Dockerfile
build_one consolidation-api src/Consolidation/Consolidation.Api/Dockerfile
build_one consolidation-worker src/Consolidation/Consolidation.Worker/Dockerfile
# migration-runner: artefato operacional - NUNCA um 5o workload de negocio no modelo de
# dominio arquitetural, mas construido do MESMO commit, pela MESMA cadeia
# build-once/SBOM/scan/manifesto/atestacao que os 4 componentes de
# negocio, exatamente pelo mesmo motivo (identidade de imagem qualificada
# por digest, nunca "latest").
build_one migration-runner src/Migrations/MigrationRunner/Dockerfile

echo "]" >> "$MANIFEST"

log ""
log "=== Manifesto escrito em ${MANIFEST} (sourceCommit=${SOURCE_COMMIT}, arvore limpa confirmada) ==="
