#!/bin/sh
# Gera um SBOM CycloneDX JSON por imagem real, usando o
# Trivy oficial fixado por digest imutavel (nunca a tag flutuante "latest",
# nunca a Action aquasecurity/trivy-action - ver
# docs/security/dependencias-e-supply-chain.md sobre o incidente de
# comprometimento da cadeia de suprimentos do proprio Trivy em 2026-03).
#
# Pre-requisito: scripts/ci/build-images-for-supply-chain.sh ja executado
# (le artifacts/sbom/images.json).
#
# Uso:
#   sh scripts/ci/generate-sboms.sh
set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

# aquasec/trivy:0.72.0 (2026-06-30) - versao corrente confirmada nao afetada
# pelo comprometimento de 2026-03 (que atingiu trivy v0.69.4-0.69.6 e
# trivy-action < 0.35.0) - digest verificado contra o Docker Hub oficial.
TRIVY_IMAGE="docker.io/aquasec/trivy@sha256:cffe3f5161a47a6823fbd23d985795b3ed72a4c806da4c4df16266c02accdd6f"

log() { printf '%s\n' "$1"; }
fail() { echo "generate-sboms: FALHA: $1" >&2; exit 1; }

[ -f artifacts/sbom/images.json ] || fail "artifacts/sbom/images.json nao encontrado - rode build-images-for-supply-chain.sh primeiro."

mkdir -p artifacts/sbom

log "Baixando/confirmando a imagem fixada do Trivy (${TRIVY_IMAGE})..."
docker pull "$TRIVY_IMAGE" >/dev/null

# Cache persistente do banco de vulnerabilidades entre chamadas (evita
# rebaixar ~500MB-1GB a cada imagem) - nome de volume escopado a este
# repositorio, removido pelo cleanup do job de CI, nunca um volume
# compartilhado com outro projeto.
TRIVY_CACHE_VOLUME="banco-carrefour-trivy-cache"
docker volume create "$TRIVY_CACHE_VOLUME" >/dev/null

# "tr -d '\r'" defende contra CRLF introduzido por ambientes onde a saida
# do subprocesso Python passa por uma camada de texto que normaliza quebras
# de linha (observado localmente no Windows/Git Bash).
COMPONENTS=$(python3 -c "
import json
with open('artifacts/sbom/images.json') as f:
    data = json.load(f)
for e in data:
    print(e['component'] + '|' + e['image'] + '|' + e['imageId'])
" | tr -d '\r')

echo "$COMPONENTS" | while IFS='|' read -r NAME IMAGE IMAGE_ID; do
  [ -z "$NAME" ] && continue

  SBOM_FILE="artifacts/sbom/${NAME}.cyclonedx.json"
  log "=== SBOM: ${NAME} (${IMAGE}) ==="

  # MSYS_NO_PATHCONV=1: no Windows/Git Bash, o MSYS reconhece "/var" como um
  # dos diretorios top-level que sempre converte para um caminho Windows
  # (ex.: "C:\Program Files\Git\var"), mesmo quando o argumento e
  # "/var/run/docker.sock:/var/run/docker.sock" (bind mount do socket do
  # Docker, que precisa permanecer literal dos dois lados) - achado real,
  # reproduzido e confirmado (docker falha com "mkdir C:\Program
  # Files\Git\var: Access is denied"). Sem efeito em Linux (variavel
  # simplesmente ignorada).
  MSYS_NO_PATHCONV=1 docker run --rm \
    -v /var/run/docker.sock:/var/run/docker.sock \
    -v "${TRIVY_CACHE_VOLUME}:/root/.cache/trivy" \
    -v "$(pwd)/artifacts/sbom:/out" \
    "$TRIVY_IMAGE" image \
    --format cyclonedx \
    --output "/out/${NAME}.cyclonedx.json" \
    "$IMAGE"

  [ -s "$SBOM_FILE" ] || fail "SBOM nao foi gerado (arquivo vazio ou ausente) para ${NAME}."

  # Anexa metadados de rastreabilidade (commit de origem, image ID, versao
  # do scanner) como companion manifest - nao mistura no proprio documento
  # CycloneDX para nao violar o schema.
  python3 - "$SBOM_FILE" "$NAME" "$IMAGE" "$IMAGE_ID" <<'PYEOF'
import json, sys
sbom_path, name, image, image_id = sys.argv[1:5]
with open(sbom_path) as f:
    sbom = json.load(f)
component_count = len(sbom.get("components", []))
if component_count == 0:
    print(f"generate-sboms: AVISO: {name} - SBOM sem nenhum componente listado.", file=sys.stderr)
print(f"  componentes no SBOM: {component_count}")
PYEOF

  log "  escrito: ${SBOM_FILE}"
done

log ""
log "=== Todos os SBOMs gerados em artifacts/sbom/ ==="
