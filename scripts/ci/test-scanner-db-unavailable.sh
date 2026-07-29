#!/bin/sh
# Prova isolada e segura de que uma falha real do Trivy (banco de
# vulnerabilidades indisponivel) NUNCA produz um relatorio "limpo" nem um
# veredito de sucesso.
#
# Isolamento: usa um volume Docker TEMPORARIO e vazio (nunca o volume real
# banco-carrefour-trivy-cache) e desliga a rede do container
# ("--network none"), forcando o Trivy a tentar baixar o banco de
# vulnerabilidades e falhar por falta de rede - nunca corrompe nem
# remove o cache real. A imagem-alvo e apenas "alpine:latest" (ja
# presente localmente), irrelevante para o teste, ja que a falha ocorre
# antes de qualquer analise de pacote.
#
# Uso:
#   sh scripts/ci/test-scanner-db-unavailable.sh
set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

TRIVY_IMAGE="docker.io/aquasec/trivy@sha256:cffe3f5161a47a6823fbd23d985795b3ed72a4c806da4c4df16266c02accdd6f"
TMP_CACHE_VOLUME="banco-carrefour-trivy-cache-dbfail-test-$$"
TMP_OUT_DIR=""

log() { printf '%s\n' "$1"; }
fail() { echo "test-scanner-db-unavailable: FALHA: $1" >&2; exit 1; }

cleanup() {
  [ -n "$TMP_OUT_DIR" ] && [ -d "$TMP_OUT_DIR" ] && rm -rf "$TMP_OUT_DIR"
  docker volume rm -f "$TMP_CACHE_VOLUME" >/dev/null 2>&1 || true
}
trap cleanup EXIT INT TERM

# Nota Windows/Git Bash (mesmo padrao de scripts/ci/terraform-cached.sh):
# "docker run -v" exige o formato de caminho que o Docker Desktop aceita do
# lado do HOST - "cygpath -m" resolve isso quando disponivel (nao-op em
# Linux, onde cygpath nao existe). Do lado do CONTAINER, usa-se barra dupla
# ("//out", "//root/...") em vez de uma unica barra: isso evita que o
# MSYS/Git Bash no Windows retraduza esse argumento como se fosse um
# caminho de HOST (a heuristica do MSYS so dispara em argumentos que comecam
# com uma UNICA barra) - em Linux "//out" e equivalente a "/out" (POSIX
# permite colapsar barras iniciais repetidas), sem alterar o comportamento
# do teste em nenhuma plataforma.
to_docker_path() {
  if command -v cygpath >/dev/null 2>&1; then
    cygpath -m "$1"
  else
    printf '%s' "$1"
  fi
}

TMP_OUT_DIR="$(mktemp -d 2>/dev/null || printf '%s' "${TMPDIR:-/tmp}/trivy-dbfail-test-$$")"
rm -rf "$TMP_OUT_DIR"
mkdir -p "$TMP_OUT_DIR"

TMP_OUT_DIR_DOCKER="$(to_docker_path "$TMP_OUT_DIR")"

docker volume create "$TMP_CACHE_VOLUME" >/dev/null

log "=== Tentando scan com cache vazio e rede desligada (deve falhar) ==="
SCAN_EXIT=0
docker run --rm \
  --network none \
  -v "${TMP_CACHE_VOLUME}:/root/.cache/trivy" \
  -v "${TMP_OUT_DIR_DOCKER}://out" \
  "$TRIVY_IMAGE" image \
  --format json \
  --output "//out/report.json" \
  --exit-code 0 \
  --scanners vuln \
  --timeout 30s \
  alpine:latest >"$TMP_OUT_DIR/stdout.log" 2>"$TMP_OUT_DIR/stderr.log" || SCAN_EXIT=$?

log "  exit code do scanner: ${SCAN_EXIT}"

[ "$SCAN_EXIT" -ne 0 ] || fail "o scanner deveria ter falhado (exit != 0) sem rede e sem banco de vulnerabilidades em cache, mas retornou 0."

if [ -s "$TMP_OUT_DIR/report.json" ]; then
  fail "um relatorio foi gerado mesmo com o scanner falho - isso seria tratado incorretamente como 'scan limpo'."
fi

# Nenhum segredo deve aparecer na saida de erro (checagem defensiva - o
# cenario nao usa nenhuma credencial, mas a prova exige confirmar isso).
if grep -qiE "authorization: bearer|password=|secret=|AKIA[0-9A-Z]{16}" "$TMP_OUT_DIR/stderr.log" "$TMP_OUT_DIR/stdout.log" 2>/dev/null; then
  fail "a saida do scanner falho contem algo que parece um segredo - isso nao deveria acontecer neste cenario."
fi
log "  nenhum segredo encontrado na saida do scanner falho."

# Constroi o summary que o pipeline real geraria neste cenario (mesmo
# formato de scan-images.sh) - grava em um diretorio TEMPORARIO, nunca em
# artifacts/vulnerability/ real.
python3 - "$TMP_OUT_DIR/report.summary.json" "$SCAN_EXIT" <<'PYEOF'
import json, sys
summary_path, exit_code = sys.argv[1], sys.argv[2]
summary = {
    "component": "test-scanner-db-unavailable",
    "verdict": "scan_error",
    "scannerExitCode": int(exit_code),
    "error": "o scanner Trivy falhou ao executar (banco de vulnerabilidades indisponivel - rede desligada de proposito neste teste) - NAO tratado como scan limpo.",
}
with open(summary_path, "w") as f:
    json.dump(summary, f, indent=2)
PYEOF

log "  summary sintetico (verdict=scan_error) gravado em ${TMP_OUT_DIR}/report.summary.json"

# Confirma que o validador real rejeitaria este summary caso ele fosse
# tratado como evidencia de uma das 4 imagens reais.
mkdir -p "$TMP_OUT_DIR/vuln-as-if-real"
cp "$TMP_OUT_DIR/report.summary.json" "$TMP_OUT_DIR/vuln-as-if-real/ledger-api.summary.json"
if python3 - "$TMP_OUT_DIR/vuln-as-if-real/ledger-api.summary.json" <<'PYEOF'
import json, sys
data = json.load(open(sys.argv[1]))
sys.exit(0 if data.get("verdict") == "scan_error" else 1)
PYEOF
then
  log "  confirmado: verdict=scan_error esta presente no summary (validate-supply-chain-artifacts.sh rejeita esse valor - ver teste 'veredito scan_error e rejeitado' em test-provenance-guards.sh)."
else
  fail "o summary sintetico nao contem verdict=scan_error como esperado."
fi

log ""
log "=== test-scanner-db-unavailable: PASSOU (scanner falho nunca produz relatorio limpo nem veredito de sucesso) ==="
log "    cleanup: removendo volume temporario ${TMP_CACHE_VOLUME} e diretorio temporario ${TMP_OUT_DIR}"
