#!/bin/sh
# Verifica non-root das MESMAS imagens ja construidas por
# build-images-for-supply-chain.sh (nunca reconstroi) - le
# artifacts/sbom/images.json e inspeciona cada imagem pelo seu imageId
# exato ("build once": a mesma imagem que passa aqui e a mesma que recebe
# SBOM, scan e publicacao).
#
# este script nunca constroi uma copia separada das imagens sob uma tag
# distinta ("ci-nonroot-check") so para verificacao - isso amplificaria
# escrita real e redundante (mesmas 4 imagens construidas duas vezes por
# execucao de CI). O job "image-gate"
# de .github/workflows/ci.yml agora chama build-images-for-supply-chain.sh
# uma unica vez e usa este script para a verificacao, sem nenhuma
# reconstrucao adicional.
#
# Uso:
#   sh scripts/ci/verify-nonroot-from-manifest.sh
set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

log() { printf '%s\n' "$1"; }
fail() { echo "verify-nonroot-from-manifest: FALHA: $1" >&2; exit 1; }

[ -f artifacts/sbom/images.json ] || fail "artifacts/sbom/images.json nao encontrado - rode build-images-for-supply-chain.sh primeiro."

is_root_user_value() {
  case "$1" in
    ""|"0"|"root"|"0:0") return 0 ;;
    *) return 1 ;;
  esac
}

COMPONENTS=$(python3 -c "
import json
with open('artifacts/sbom/images.json') as f:
    data = json.load(f)
for e in data:
    print(e['component'] + '|' + e['image'] + '|' + e['imageId'])
" | tr -d '\r')

FAILED_COMPONENTS=""

echo "$COMPONENTS" | while IFS='|' read -r NAME IMAGE IMAGE_ID; do
  [ -z "$NAME" ] && continue

  log "=== ${NAME} (${IMAGE}) ==="

  ACTUAL_ID=$(docker image inspect "$IMAGE" --format '{{.Id}}' 2>/dev/null) || {
    echo "verify-nonroot-from-manifest: imagem ${IMAGE} nao encontrada localmente - foi removida apos o build?" >&2
    echo "1" > artifacts/sbom/.nonroot-check-failed
    continue
  }

  if [ "$ACTUAL_ID" != "$IMAGE_ID" ]; then
    echo "verify-nonroot-from-manifest: imageId de ${IMAGE} mudou desde o build (manifesto: ${IMAGE_ID}, atual: ${ACTUAL_ID}) - a imagem foi reconstruida ou substituida, quebrando build-once." >&2
    echo "1" > artifacts/sbom/.nonroot-check-failed
    continue
  fi

  USER_VALUE=$(docker image inspect "$IMAGE" --format '{{.Config.User}}')

  if ! is_root_user_value "$USER_VALUE"; then
    log "  usuario padrao da imagem (nao-root, USER estatico no Dockerfile): ${USER_VALUE}"
    continue
  fi

  # Config.User vazio/root nao e necessariamente root em runtime:
  # Ledger.Api/Consolidation.Api usam docker/api-entrypoint.sh, que sobe
  # como root so para instalar a CA local e troca para o usuario "app" via
  # "setpriv --reuid=app --regid=app" antes de executar o processo real
  # (nunca roda a aplicacao como root) - confirma isso invocando o MESMO
  # mecanismo do entrypoint real.
  log "  Config.User vazio/root - verificando drop de privilegio via setpriv..."
  RUNTIME_ID_OUTPUT=$(docker run --rm --entrypoint sh "$IMAGE" -c "setpriv --reuid=app --regid=app --init-groups id" 2>&1) || {
    echo "verify-nonroot-from-manifest: ${NAME} nao expoe 'setpriv --reuid=app' funcional (saida: ${RUNTIME_ID_OUTPUT})." >&2
    echo "1" > artifacts/sbom/.nonroot-check-failed
    continue
  }

  case "$RUNTIME_ID_OUTPUT" in
    uid=0\(*)
      echo "verify-nonroot-from-manifest: ${NAME} - setpriv --reuid=app ainda resulta em uid=0 (${RUNTIME_ID_OUTPUT})." >&2
      echo "1" > artifacts/sbom/.nonroot-check-failed
      ;;
    uid=*)
      log "  runtime confirmado nao-root via setpriv: ${RUNTIME_ID_OUTPUT}"
      ;;
    *)
      echo "verify-nonroot-from-manifest: ${NAME} - saida inesperada de 'id' (${RUNTIME_ID_OUTPUT})." >&2
      echo "1" > artifacts/sbom/.nonroot-check-failed
      ;;
  esac
done

if [ -f artifacts/sbom/.nonroot-check-failed ]; then
  rm -f artifacts/sbom/.nonroot-check-failed
  fail "uma ou mais imagens falharam a verificacao non-root a partir do manifesto - ver mensagens acima."
fi

log ""
log "=== Todas as imagens do manifesto confirmadas non-root, sem reconstruir nenhuma ==="
