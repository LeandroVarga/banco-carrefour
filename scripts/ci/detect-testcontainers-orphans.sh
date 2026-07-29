#!/bin/sh
# Detecta, de forma somente-leitura por padrao, volumes Docker anonimos
# provavelmente deixados por execucoes anteriores do Testcontainers .NET
# (Ledger/Consolidation/Security IntegrationTests) apos a correcao que
# passou a usar WithTmpfsMount("/var/lib/postgresql/data") - ver
# LedgerIntegrationCollection.cs e demais fixtures.
#
# Origem do residuo: a imagem "postgres:16-alpine" declara
# "/var/lib/postgresql/data" como VOLUME no proprio Dockerfile (confirmado
# via "docker image inspect --format {{json .Config.Volumes}}") - sem um
# mount explicito, Docker cria um volume anonimo por container, mesmo com
# WithCleanUp(true)/DisposeAsync corretos, porque a remocao do container
# nao implica remocao automatica do volume anonimo associado.
#
# IMPORTANTE (limitacao honesta): um volume anonimo com 0 links nao prova,
# por si so, que pertence a este repositorio - qualquer outro projeto local
# que use Testcontainers/Postgres produz volumes anonimos indistinguiveis
# por rotulo. Este script reporta "candidato provavel" (anonimo + 0 links),
# nunca "confirmado", e exige selecao explicita e nominal para qualquer
# remocao - nunca uma remocao em lote por padrao de nome ou idade.
#
# Modos:
#   sh scripts/ci/detect-testcontainers-orphans.sh                (padrao: somente relatorio, nada e removido)
#   sh scripts/ci/detect-testcontainers-orphans.sh --json          (relatorio em JSON compacto)
#   sh scripts/ci/detect-testcontainers-orphans.sh --remove <nome-exato-do-volume> [<nome> ...]
#     Remove SOMENTE os volumes nomeados explicitamente na linha de comando,
#     e somente apos revalidar, no momento da remocao, que cada um: (a) tem
#     rotulo com.docker.volume.anonymous; (b) tem 0 links; (c) nao e um dos
#     volumes nomeados protegidos conhecidos deste repositorio. Recusa
#     qualquer nome fora dessas condicoes, sem interromper os demais.
set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

fail() { echo "detect-testcontainers-orphans: FALHA: $1" >&2; exit 1; }

# Volumes nomeados deste repositorio que NUNCA devem ser tratados como
# candidatos, mesmo que por algum motivo aparecam com o rotulo anonimo (defesa
# em profundidade - nao deveria acontecer, mas a checagem e barata).
PROTECTED_NAMES="banco-carrefour-trivy-cache banco-carrefour_dotnet-nuget-cache banco-carrefour_postgres-data"

command -v docker >/dev/null 2>&1 || fail "docker nao encontrado no PATH."
docker info >/dev/null 2>&1 || fail "Docker nao esta acessivel (daemon parado ou sem permissao)."

is_protected() {
  name="$1"
  for p in $PROTECTED_NAMES; do
    [ "$name" = "$p" ] && return 0
  done
  return 1
}

MODE="report"
JSON=0
REMOVE_NAMES=""

while [ $# -gt 0 ]; do
  case "$1" in
    --json) JSON=1; shift ;;
    --remove)
      MODE="remove"
      shift
      while [ $# -gt 0 ]; do
        case "$1" in
          --*) break ;;
          *) REMOVE_NAMES="${REMOVE_NAMES} $1"; shift ;;
        esac
      done
      ;;
    *) fail "argumento desconhecido: $1" ;;
  esac
done

if [ "$MODE" = "remove" ] && [ -z "$(echo "$REMOVE_NAMES" | tr -d '[:space:]')" ]; then
  fail "--remove exige ao menos um nome exato de volume."
fi

# --- Coleta ---
ALL_VOLUMES="$(docker volume ls -q)"
CANDIDATES_FILE="$(mktemp 2>/dev/null || echo /tmp/orphan-candidates-$$)"
: > "$CANDIDATES_FILE"

for name in $ALL_VOLUMES; do
  info="$(docker volume inspect "$name" 2>/dev/null)" || continue
  is_anon="$(printf '%s' "$info" | grep -c '"com.docker.volume.anonymous"' || true)"
  [ "$is_anon" -eq 0 ] && continue
  is_protected "$name" && continue

  links="$(docker system df -v --format '{{json .Volumes}}' 2>/dev/null | python3 -c "
import json, sys
try:
    vols = json.load(sys.stdin)
except Exception:
    vols = []
for v in vols:
    if v.get('Name') == '$name':
        print(v.get('Links', v.get('RefCount', '0')))
        break
else:
    print('0')
" 2>/dev/null || echo "0")"

  created="$(printf '%s' "$info" | python3 -c "import json,sys; print(json.load(sys.stdin)[0].get('CreatedAt',''))" 2>/dev/null || echo "unknown")"
  mountpoint="$(printf '%s' "$info" | python3 -c "import json,sys; print(json.load(sys.stdin)[0].get('Mountpoint',''))" 2>/dev/null || echo "unknown")"

  if [ "${links:-0}" = "0" ]; then
    echo "${name}|${created}|${mountpoint}" >> "$CANDIDATES_FILE"
  fi
done

CANDIDATE_COUNT="$(wc -l < "$CANDIDATES_FILE" | tr -d ' ')"

if [ "$MODE" = "report" ]; then
  if [ "$JSON" -eq 1 ]; then
    printf '{"candidateCount": %s, "candidates": [' "$CANDIDATE_COUNT"
    first=1
    while IFS='|' read -r name created mountpoint; do
      [ -z "$name" ] && continue
      [ "$first" -eq 0 ] && printf ','
      first=0
      printf '{"name": "%s", "createdAt": "%s", "mountpoint": "%s", "anonymous": true, "links": 0}' "$name" "$created" "$mountpoint"
    done < "$CANDIDATES_FILE"
    printf ']}\n'
  else
    echo "=== Candidatos a residuo de Testcontainers (anonimos, 0 links) ==="
    echo "Total: ${CANDIDATE_COUNT}"
    echo ""
    if [ "$CANDIDATE_COUNT" -gt 0 ]; then
      printf '%-70s %-25s\n' "NOME" "CRIADO EM"
      while IFS='|' read -r name created mountpoint; do
        [ -z "$name" ] && continue
        printf '%-70s %-25s\n' "$name" "$created"
      done < "$CANDIDATES_FILE"
      echo ""
      echo "NOTA: 'candidato provavel', nunca 'confirmado' - anonimo + 0 links nao"
      echo "prova ownership deste repositorio. Nenhuma remocao foi executada."
      echo "Para remover, use: --remove <nome-exato> [<nome-exato> ...]"
    fi
  fi
  rm -f "$CANDIDATES_FILE"
  exit 0
fi

# --- Modo remove: escopo estrito, revalidado no momento da remocao ---
REMOVED=0
REFUSED=0
for target in $REMOVE_NAMES; do
  if is_protected "$target"; then
    echo "detect-testcontainers-orphans: RECUSADO (volume protegido): ${target}" >&2
    REFUSED=$((REFUSED + 1))
    continue
  fi
  if ! grep -q "^${target}|" "$CANDIDATES_FILE"; then
    echo "detect-testcontainers-orphans: RECUSADO (nao e um candidato anonimo com 0 links neste momento): ${target}" >&2
    REFUSED=$((REFUSED + 1))
    continue
  fi
  if docker volume rm "$target" >/dev/null 2>&1; then
    echo "detect-testcontainers-orphans: removido: ${target}"
    REMOVED=$((REMOVED + 1))
  else
    echo "detect-testcontainers-orphans: RECUSADO (docker volume rm falhou - pode ter sido referenciado entre a deteccao e a remocao): ${target}" >&2
    REFUSED=$((REFUSED + 1))
  fi
done

rm -f "$CANDIDATES_FILE"
echo ""
echo "=== ${REMOVED} removido(s), ${REFUSED} recusado(s) ==="
[ "$REFUSED" -eq 0 ]
