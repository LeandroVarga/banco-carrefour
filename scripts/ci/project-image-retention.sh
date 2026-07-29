#!/bin/sh
# Inventario e retencao segura de imagens locais Banco Carrefour
#. Padrao: somente relatorio
# (dry-run) - nenhuma imagem e removida a menos que --remove seja usado com
# nomes exatos.
#
# Preserva SEMPRE (nunca aparecem como candidatos):
#   - todas as 5 tags ":local" (4 workloads de negocio + o artefato
#     operacional migration-runner, usadas pelo docker-compose.yml de dev);
#   - todas as tags do commit HEAD atual (git rev-parse HEAD);
#   - qualquer imagem referenciada por um container (rodando ou parado);
#   - qualquer imagem que nao seja um dos 5 nomes deste repositorio (4
#     workloads de negocio + migration-runner - banco-carrefour-ledger-api,
#     banco-carrefour-ledger-outbox-publisher, banco-carrefour-consolidation-api,
#     banco-carrefour-consolidation-worker, banco-carrefour-migration-runner).
#
# Candidatos possiveis (somente estes, nunca por idade sozinha):
#   - tags dos 5 nomes cuja tag e uma SHA completa de 40 hex DIFERENTE do
#     HEAD atual - evidencia de origem (SHA de commit real deste
#     repositorio), reproduzivel via build-images-for-supply-chain.sh
#     a partir do commit correspondente.
#
# Nunca usa:
#   - docker system prune / docker image prune -a;
#   - grep amplo de nome de repositorio seguido de remocao direta;
#   - remocao baseada apenas em idade/timestamp.
#
# Modos:
#   sh scripts/ci/project-image-retention.sh                (padrao: relatorio, dry-run)
#   sh scripts/ci/project-image-retention.sh --json
#   sh scripts/ci/project-image-retention.sh --remove <repo:tag-exato> [<repo:tag-exato> ...]
#     Remove SOMENTE as referencias nomeadas explicitamente, e somente apos
#     revalidar no momento da remocao que cada uma: (a) e um dos 4
#     componentes; (b) a tag e uma SHA completa de 40 hex; (c) a SHA e
#     DIFERENTE do HEAD atual; (d) nenhum container a referencia. Recusa
#     qualquer outra entrada sem interromper as demais.
set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

fail() { echo "project-image-retention: FALHA: $1" >&2; exit 1; }

COMPONENTS="ledger-api ledger-outbox-publisher consolidation-api consolidation-worker migration-runner"

command -v docker >/dev/null 2>&1 || fail "docker nao encontrado no PATH."
docker info >/dev/null 2>&1 || fail "Docker nao esta acessivel (daemon parado ou sem permissao)."

CURRENT_HEAD="$(git rev-parse HEAD 2>/dev/null)" || fail "nao foi possivel resolver o HEAD atual (git rev-parse HEAD)."

MODE="report"
JSON=0
REMOVE_REFS=""

while [ $# -gt 0 ]; do
  case "$1" in
    --json) JSON=1; shift ;;
    --remove)
      MODE="remove"
      shift
      while [ $# -gt 0 ]; do
        case "$1" in
          --*) break ;;
          *) REMOVE_REFS="${REMOVE_REFS}
$1"; shift ;;
        esac
      done
      ;;
    *) fail "argumento desconhecido: $1" ;;
  esac
done

is_component() {
  comp="$1"
  for c in $COMPONENTS; do
    [ "$c" = "$comp" ] && return 0
  done
  return 1
}

is_full_sha() {
  case "$1" in
    [0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f])
      [ "${#1}" -eq 40 ] && return 0 ;;
  esac
  return 1
}

# --- Inventario completo dos 5 nomes conhecidos (4 negocio + 1 operacional) ---
ALL_TAGS="$(docker image ls --no-trunc --format '{{.Repository}}:{{.Tag}}|{{.ID}}' 2>/dev/null | grep -E '^banco-carrefour-(ledger-api|ledger-outbox-publisher|consolidation-api|consolidation-worker|migration-runner):')"

CONTAINER_IMAGES="$(docker ps -a --format '{{.Image}}' 2>/dev/null)"

PRESERVED_FILE="$(mktemp 2>/dev/null || echo /tmp/retention-preserved-$$)"
CANDIDATES_FILE="$(mktemp 2>/dev/null || echo /tmp/retention-candidates-$$)"
: > "$PRESERVED_FILE"
: > "$CANDIDATES_FILE"

echo "$ALL_TAGS" | while IFS='|' read -r ref id; do
  [ -z "$ref" ] && continue
  repo="${ref%%:*}"
  tag="${ref#*:}"
  comp="${repo#banco-carrefour-}"

  is_component "$comp" || continue

  if [ "$tag" = "local" ]; then
    echo "${ref}|${id}|local-dev" >> "$PRESERVED_FILE"
    continue
  fi

  if [ "$tag" = "$CURRENT_HEAD" ]; then
    echo "${ref}|${id}|current-head" >> "$PRESERVED_FILE"
    continue
  fi

  if echo "$CONTAINER_IMAGES" | grep -qxF "$ref"; then
    echo "${ref}|${id}|referenced-by-container" >> "$PRESERVED_FILE"
    continue
  fi

  if is_full_sha "$tag"; then
    echo "${ref}|${id}|older-sha-candidate" >> "$CANDIDATES_FILE"
  else
    echo "${ref}|${id}|unrecognized-tag-preserved-conservatively" >> "$PRESERVED_FILE"
  fi
done

CANDIDATE_COUNT="$(wc -l < "$CANDIDATES_FILE" | tr -d ' ')"

if [ "$MODE" = "report" ]; then
  if [ "$JSON" -eq 1 ]; then
    printf '{"currentHead": "%s", "candidateCount": %s, "candidates": [' "$CURRENT_HEAD" "$CANDIDATE_COUNT"
    first=1
    while IFS='|' read -r ref id reason; do
      [ -z "$ref" ] && continue
      [ "$first" -eq 0 ] && printf ','
      first=0
      printf '{"ref": "%s", "id": "%s"}' "$ref" "$id"
    done < "$CANDIDATES_FILE"
    printf '], "preservedCount": %s}\n' "$(wc -l < "$PRESERVED_FILE" | tr -d ' ')"
  else
    echo "=== Retencao de imagens Banco Carrefour (HEAD atual: ${CURRENT_HEAD}) ==="
    echo ""
    echo "--- Preservadas ---"
    while IFS='|' read -r ref id reason; do
      [ -z "$ref" ] && continue
      printf '%-70s %s\n' "$ref" "$reason"
    done < "$PRESERVED_FILE"
    echo ""
    echo "--- Candidatas a remocao (SHA mais antiga que o HEAD atual, sem container) ---"
    echo "Total: ${CANDIDATE_COUNT}"
    while IFS='|' read -r ref id reason; do
      [ -z "$ref" ] && continue
      printf '%-70s %s\n' "$ref" "$id"
    done < "$CANDIDATES_FILE"
    echo ""
    echo "Nenhuma imagem foi removida (modo relatorio). Para remover, use:"
    echo "  --remove <repo:tag-exato> [<repo:tag-exato> ...]"
  fi
  rm -f "$PRESERVED_FILE" "$CANDIDATES_FILE"
  exit 0
fi

# --- Modo remove: escopo estrito, revalidado no momento da remocao ---
# Le de um arquivo temporario via REDIRECT (nao PIPE): um "while read" do
# lado direito de um pipe roda em subshell no POSIX sh, e as variaveis
# REMOVED/REFUSED modificadas dentro dele seriam perdidas ao sair do loop
# (mesma classe de bug ja encontrada e corrigida em test-release-guards.sh
# nesta auditoria). Redirect de arquivo nao cria subshell - as variaveis
# sobrevivem normalmente.
REMOVE_REFS_FILE="$(mktemp 2>/dev/null || echo /tmp/retention-remove-refs-$$)"
printf '%s\n' "$REMOVE_REFS" > "$REMOVE_REFS_FILE"

REMOVED=0
REFUSED=0
while IFS= read -r target; do
  [ -z "$target" ] && continue

  repo="${target%%:*}"
  tag="${target#*:}"
  comp="${repo#banco-carrefour-}"

  if ! is_component "$comp"; then
    echo "project-image-retention: RECUSADO (nao e um dos 5 nomes conhecidos): ${target}" >&2
    REFUSED=$((REFUSED + 1))
    continue
  fi
  if [ "$tag" = "local" ] || [ "$tag" = "$CURRENT_HEAD" ]; then
    echo "project-image-retention: RECUSADO (protegido - local ou HEAD atual): ${target}" >&2
    REFUSED=$((REFUSED + 1))
    continue
  fi
  if ! is_full_sha "$tag"; then
    echo "project-image-retention: RECUSADO (tag nao e uma SHA completa de 40 hex): ${target}" >&2
    REFUSED=$((REFUSED + 1))
    continue
  fi
  if docker ps -a --format '{{.Image}}' 2>/dev/null | grep -qxF "$target"; then
    echo "project-image-retention: RECUSADO (referenciado por um container): ${target}" >&2
    REFUSED=$((REFUSED + 1))
    continue
  fi
  # "grep -qF "^${target}|"" NUNCA teria casado com nada - o
  # modo -F (fixed-string) do GNU grep trata "^" como um caractere LITERAL,
  # nao como ancora de inicio de linha (confirmado empiricamente). Isso
  # fazia todo alvo ser recusado como "fora da lista de candidatos", mesmo
  # quando genuinamente presente - um bug silencioso que NUNCA foi
  # exercitado pela verificacao original (que so testava caminhos de
  # recusa anteriores no if/elif, nunca uma remocao bem-sucedida real).
  # Corrigido com comparacao exata campo-a-campo via leitura de linha, sem
  # depender de nenhuma sintaxe de ancora de grep.
  IS_CANDIDATE=0
  while IFS='|' read -r candidate_ref candidate_id candidate_reason; do
    [ "$candidate_ref" = "$target" ] && IS_CANDIDATE=1 && break
  done < "$CANDIDATES_FILE"
  if [ "$IS_CANDIDATE" -ne 1 ]; then
    echo "project-image-retention: RECUSADO (nao esta na lista de candidatos detectados nesta execucao): ${target}" >&2
    REFUSED=$((REFUSED + 1))
    continue
  fi

  if docker rmi "$target" >/dev/null 2>&1; then
    echo "project-image-retention: removido: ${target}"
    REMOVED=$((REMOVED + 1))
  else
    echo "project-image-retention: RECUSADO (docker rmi falhou): ${target}" >&2
    REFUSED=$((REFUSED + 1))
  fi
done < "$REMOVE_REFS_FILE"

rm -f "$PRESERVED_FILE" "$CANDIDATES_FILE" "$REMOVE_REFS_FILE"
echo ""
echo "=== ${REMOVED} removido(s), ${REFUSED} recusado(s) ==="
[ "$REFUSED" -eq 0 ]
