#!/bin/sh
# Preflight de capacidade de disco - deve rodar ANTES de qualquer operacao
# pesada deste repositorio (build das 4 imagens, Testcontainers completo,
# scan de vulnerabilidade, geracao de SBOM, prova de OCI, terraform
# init/plan, simulacao completa de ambiente). Somente leitura - nunca
# apaga, builda, faz pull ou modifica nada.
#
# Motivo: a auditoria de
# capacidade encontrou o host Windows local em 0.82GB livres
# apos uma sequencia de builds/scans/Terraform sem nenhum preflight - o
# bloqueio so foi descoberto DEPOIS do trabalho pesado ja ter rodado. Este
# script torna essa checagem explicita e executavel a qualquer momento,
# local ou em CI.
#
# Modos:
#   sh scripts/ci/capacity-preflight.sh                (padrao: modo aviso - nunca falha por capacidade)
#   sh scripts/ci/capacity-preflight.sh --fail-below-threshold
#   sh scripts/ci/capacity-preflight.sh --json
#
# Variaveis de ambiente opcionais (nenhuma tem default especifico desta
# maquina - seguras para rodar em qualquer runner local ou hospedado):
#   CAPACITY_MIN_FREE_GIB        limite minimo (default: 30 local / 10 CI,
#                                 ver deteccao de ambiente abaixo)
#   CAPACITY_PREFERRED_FREE_GIB  limite preferido, so informativo (default: 50)
#   CAPACITY_TARGET_PATH         caminho cuja capacidade de filesystem sera
#                                 medida (default: raiz do repositorio)
#
# Uso tipico antes de trabalho pesado:
#   sh scripts/ci/capacity-preflight.sh --fail-below-threshold || exit 1
set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

fail() { echo "capacity-preflight: FALHA: $1" >&2; exit 1; }

MODE="warn"
JSON=0
for arg in "$@"; do
  case "$arg" in
    --fail-below-threshold) MODE="fail" ;;
    --json) JSON=1 ;;
    *) fail "argumento desconhecido: $arg" ;;
  esac
done

TARGET_PATH="${CAPACITY_TARGET_PATH:-$REPO_ROOT}"

# Deteccao de ambiente: GitHub-hosted runners exportam CI=true (convencao
# oficial do GitHub Actions) - o limiar padrao em CI e menor porque runners
# hospedados sao efemeros e tem seu proprio disco dedicado por execucao,
# diferente de uma estacao de desenvolvimento local compartilhada entre
# multiplos projetos.
if [ "${CI:-}" = "true" ]; then
  DEFAULT_MIN_GIB=10
else
  DEFAULT_MIN_GIB=30
fi

MIN_FREE_GIB="${CAPACITY_MIN_FREE_GIB:-$DEFAULT_MIN_GIB}"
PREFERRED_FREE_GIB="${CAPACITY_PREFERRED_FREE_GIB:-50}"

# --- Capacidade do filesystem (POSIX "df", funciona em Linux/CI e em
# Git Bash/MSYS no Windows, que expoe as unidades como filesystems) ---
FS_LINE="$(df -k "$TARGET_PATH" 2>/dev/null | tail -1)"
FS_TOTAL_KB="$(echo "$FS_LINE" | awk '{print $2}')"
FS_USED_KB="$(echo "$FS_LINE" | awk '{print $3}')"
FS_AVAIL_KB="$(echo "$FS_LINE" | awk '{print $4}')"

to_gib() {
  # $1 = kilobytes -> GiB com 2 casas, usando aritmetica inteira do POSIX sh
  # (sem bc/awk float para maxima portabilidade).
  awk -v kb="$1" 'BEGIN { printf "%.2f", kb/1048576 }'
}

FS_TOTAL_GIB="$(to_gib "${FS_TOTAL_KB:-0}")"
FS_USED_GIB="$(to_gib "${FS_USED_KB:-0}")"
FS_AVAIL_GIB="$(to_gib "${FS_AVAIL_KB:-0}")"

BELOW_MIN=0
if awk -v a="$FS_AVAIL_GIB" -v m="$MIN_FREE_GIB" 'BEGIN { exit !(a < m) }'; then
  BELOW_MIN=1
fi
BELOW_PREFERRED=0
if awk -v a="$FS_AVAIL_GIB" -v p="$PREFERRED_FREE_GIB" 'BEGIN { exit !(a < p) }'; then
  BELOW_PREFERRED=1
fi

# --- Docker (opcional - so reporta se o daemon estiver acessivel; nunca
# falha o preflight so por Docker estar indisponivel, ja que nem toda
# operacao pesada deste repositorio depende de Docker, ex.: terraform) ---
DOCKER_AVAILABLE=0
DOCKER_IMAGES="n/a"
DOCKER_IMAGES_SIZE="n/a"
DOCKER_VOLUMES="n/a"
DOCKER_VOLUMES_SIZE="n/a"
DOCKER_CONTAINERS="n/a"
DOCKER_BUILD_CACHE_SIZE="n/a"
ORPHAN_VOLUME_COUNT="n/a"

if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
  DOCKER_AVAILABLE=1
  DF_LINE_IMAGES="$(docker system df --format '{{.Type}}|{{.TotalCount}}|{{.Size}}' 2>/dev/null | grep '^Images|' || true)"
  DF_LINE_VOLUMES="$(docker system df --format '{{.Type}}|{{.TotalCount}}|{{.Size}}' 2>/dev/null | grep '^Local Volumes|' || true)"
  DF_LINE_CONTAINERS="$(docker system df --format '{{.Type}}|{{.TotalCount}}|{{.Size}}' 2>/dev/null | grep '^Containers|' || true)"
  DF_LINE_CACHE="$(docker system df --format '{{.Type}}|{{.TotalCount}}|{{.Size}}' 2>/dev/null | grep '^Build Cache|' || true)"

  DOCKER_IMAGES="$(echo "$DF_LINE_IMAGES" | cut -d'|' -f2)"
  DOCKER_IMAGES_SIZE="$(echo "$DF_LINE_IMAGES" | cut -d'|' -f3)"
  DOCKER_VOLUMES="$(echo "$DF_LINE_VOLUMES" | cut -d'|' -f2)"
  DOCKER_VOLUMES_SIZE="$(echo "$DF_LINE_VOLUMES" | cut -d'|' -f3)"
  DOCKER_CONTAINERS="$(echo "$DF_LINE_CONTAINERS" | cut -d'|' -f2)"
  DOCKER_BUILD_CACHE_SIZE="$(echo "$DF_LINE_CACHE" | cut -d'|' -f3)"

  if [ -x "$REPO_ROOT/scripts/ci/detect-testcontainers-orphans.sh" ]; then
    ORPHAN_VOLUME_COUNT="$(sh "$REPO_ROOT/scripts/ci/detect-testcontainers-orphans.sh" --json 2>/dev/null | python3 -c "import json,sys; print(json.load(sys.stdin).get('candidateCount','n/a'))" 2>/dev/null || echo "n/a")"
  fi
fi

# --- Retencao de imagens do projeto (contagem por familia de tag, sem
# nenhuma acao destrutiva - so contagem informativa) ---
PROJECT_HEAD_IMAGES="n/a"
PROJECT_LOCAL_IMAGES="n/a"
PROJECT_OTHER_SHA_IMAGES="n/a"
if [ "$DOCKER_AVAILABLE" -eq 1 ]; then
  CURRENT_HEAD="$(git rev-parse HEAD 2>/dev/null || echo "")"
  ALL_PROJECT_TAGS="$(docker image ls --format '{{.Repository}}:{{.Tag}}' 2>/dev/null | grep -E '^banco-carrefour-(ledger-api|ledger-outbox-publisher|consolidation-api|consolidation-worker):' || true)"
  PROJECT_LOCAL_IMAGES="$(echo "$ALL_PROJECT_TAGS" | grep -c ':local$' || true)"
  if [ -n "$CURRENT_HEAD" ]; then
    PROJECT_HEAD_IMAGES="$(echo "$ALL_PROJECT_TAGS" | grep -c ":${CURRENT_HEAD}\$" || true)"
    PROJECT_OTHER_SHA_IMAGES="$(echo "$ALL_PROJECT_TAGS" | grep -Ev ":(local|${CURRENT_HEAD})\$" | grep -cE ':[0-9a-f]{40}$' || true)"
  fi
fi

# --- Cache compartilhado de provider Terraform ---
TF_CACHE_SIZE="not present"
if [ -d "$REPO_ROOT/infra/terraform/.terraform-plugin-cache" ]; then
  TF_CACHE_SIZE="$(du -sh "$REPO_ROOT/infra/terraform/.terraform-plugin-cache" 2>/dev/null | cut -f1)"
fi

if [ "$JSON" -eq 1 ]; then
  cat <<EOF
{
  "filesystem": {
    "targetPath": "${TARGET_PATH}",
    "totalGiB": ${FS_TOTAL_GIB},
    "usedGiB": ${FS_USED_GIB},
    "freeGiB": ${FS_AVAIL_GIB},
    "minimumFreeGiB": ${MIN_FREE_GIB},
    "preferredFreeGiB": ${PREFERRED_FREE_GIB},
    "belowMinimum": $( [ "$BELOW_MIN" -eq 1 ] && echo true || echo false ),
    "belowPreferred": $( [ "$BELOW_PREFERRED" -eq 1 ] && echo true || echo false )
  },
  "docker": {
    "available": $( [ "$DOCKER_AVAILABLE" -eq 1 ] && echo true || echo false ),
    "images": "${DOCKER_IMAGES}",
    "imagesSize": "${DOCKER_IMAGES_SIZE}",
    "volumes": "${DOCKER_VOLUMES}",
    "volumesSize": "${DOCKER_VOLUMES_SIZE}",
    "containers": "${DOCKER_CONTAINERS}",
    "buildCacheSize": "${DOCKER_BUILD_CACHE_SIZE}",
    "probableOrphanVolumes": "${ORPHAN_VOLUME_COUNT}"
  },
  "projectImageRetention": {
    "currentHeadImages": "${PROJECT_HEAD_IMAGES}",
    "localDevImages": "${PROJECT_LOCAL_IMAGES}",
    "otherShaTaggedImages": "${PROJECT_OTHER_SHA_IMAGES}"
  },
  "terraformProviderCache": "${TF_CACHE_SIZE}",
  "mode": "${MODE}"
}
EOF
else
  echo "=== Capacity Preflight ==="
  echo "Caminho medido: ${TARGET_PATH}"
  echo "Filesystem: total=${FS_TOTAL_GIB}GiB usado=${FS_USED_GIB}GiB livre=${FS_AVAIL_GIB}GiB"
  echo "Limiar minimo: ${MIN_FREE_GIB}GiB | preferido: ${PREFERRED_FREE_GIB}GiB"
  if [ "$BELOW_MIN" -eq 1 ]; then
    echo "STATUS: ABAIXO DO MINIMO"
  elif [ "$BELOW_PREFERRED" -eq 1 ]; then
    echo "STATUS: acima do minimo, abaixo do preferido"
  else
    echo "STATUS: OK (acima do preferido)"
  fi
  echo ""
  echo "Docker disponivel: $( [ "$DOCKER_AVAILABLE" -eq 1 ] && echo sim || echo nao )"
  if [ "$DOCKER_AVAILABLE" -eq 1 ]; then
    echo "  Imagens: ${DOCKER_IMAGES} (${DOCKER_IMAGES_SIZE})"
    echo "  Volumes: ${DOCKER_VOLUMES} (${DOCKER_VOLUMES_SIZE})"
    echo "  Containers: ${DOCKER_CONTAINERS}"
    echo "  Build cache: ${DOCKER_BUILD_CACHE_SIZE}"
    echo "  Candidatos a residuo Testcontainers (anonimos, 0 links): ${ORPHAN_VOLUME_COUNT}"
    echo ""
    echo "  Imagens Banco Carrefour do HEAD atual: ${PROJECT_HEAD_IMAGES}"
    echo "  Imagens Banco Carrefour :local: ${PROJECT_LOCAL_IMAGES}"
    echo "  Imagens Banco Carrefour de outras SHA: ${PROJECT_OTHER_SHA_IMAGES}"
  fi
  echo ""
  echo "Cache compartilhado de provider Terraform: ${TF_CACHE_SIZE}"
fi

if [ "$MODE" = "fail" ] && [ "$BELOW_MIN" -eq 1 ]; then
  fail "espaco livre (${FS_AVAIL_GIB}GiB) abaixo do minimo exigido (${MIN_FREE_GIB}GiB) - trabalho pesado bloqueado."
fi

exit 0
