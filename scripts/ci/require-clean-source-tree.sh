#!/bin/sh
# Falha se a arvore de trabalho nao estiver limpa o suficiente para que uma
# imagem buildada agora seja reproduzivel a partir do commit declarado
# (git rev-parse HEAD). Usado por build-images-for-supply-chain.sh antes de
# qualquer "docker build" - garante que a imagem buildada corresponde
# exatamente ao commit declarado.
#
# "git diff --quiet" sozinho nao cobre todos os estados relevantes - por
# isso a checagem combina:
#   - git diff (mudanca rastreada nao staged, inclui delecao)
#   - git diff --cached (mudanca staged, inclui delecao/adicao)
#   - git ls-files -u (conflito de merge nao resolvido)
#   - git status --porcelain (arquivo novo NAO rastreado e NAO ignorado -
#     "git status --porcelain" ja omite tudo coberto por .gitignore, entao
#     evidencia gerada em artifacts/ nunca causa falha aqui)
#
# Aceita um diretorio opcional como primeiro argumento (usado por
# scripts/ci/test-provenance-guards.sh para testar este script contra uma
# arvore de trabalho temporaria, sem tocar no repositorio real).
#
# Uso:
#   sh scripts/ci/require-clean-source-tree.sh [diretorio]
# Saida: 0 se limpo (imprime a HEAD resolvida); 1 com mensagem em stderr
# caso contrario.
set -eu

SCRIPT_REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
TARGET_DIR="${1:-$SCRIPT_REPO_ROOT}"
cd "$TARGET_DIR"

fail() { echo "require-clean-source-tree: FALHA: $1" >&2; exit 1; }

git rev-parse --verify HEAD >/dev/null 2>&1 \
  || fail "HEAD nao pode ser resolvido - repositorio Git invalido ou sem commits."

if ! git diff --quiet -- .; then
  fail "arquivo(s) rastreado(s) modificado(s) e nao commitado(s) (git diff nao vazio) - a imagem nao seria reproduzivel a partir do commit declarado."
fi

if ! git diff --cached --quiet -- .; then
  fail "arquivo(s) staged e nao commitado(s) (git diff --cached nao vazio) - a imagem nao seria reproduzivel a partir do commit declarado."
fi

CONFLICTS="$(git ls-files -u)"
[ -z "$CONFLICTS" ] || fail "conflito de merge nao resolvido presente no indice."

UNTRACKED="$(git status --porcelain=v1 --untracked-files=normal | grep '^??' | cut -c4- || true)"
if [ -n "$UNTRACKED" ]; then
  fail "arquivo(s) nao rastreado(s) presentes (fora de .gitignore) que podem influenciar o build: $(printf '%s' "$UNTRACKED" | tr '\n' ' ')"
fi

echo "require-clean-source-tree: arvore de trabalho limpa (HEAD=$(git rev-parse HEAD))."
