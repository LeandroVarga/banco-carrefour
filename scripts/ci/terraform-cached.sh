#!/bin/sh
# Wrapper unico para invocar Terraform (via a mesma imagem Docker oficial
# "hashicorp/terraform" ja usada em todo este repositorio) com um cache de
# provider plugins COMPARTILHADO entre todos os ambientes
# (infra/terraform/environments/aws-reference,
# infra/terraform/environments/localstack-hobby, e qualquer ambiente
# futuro) - via TF_PLUGIN_CACHE_DIR (mecanismo oficial do Terraform).
#
# antes deste
# wrapper, cada ambiente rodava "terraform init" isoladamente e baixava sua
# PROPRIA copia do provider hashicorp/aws (~660MB cada), duplicando ~1.3GB
# de bytes identicos no host. Com TF_PLUGIN_CACHE_DIR apontando para um
# unico diretorio compartilhado, o segundo "terraform init" (de qualquer
# ambiente) reaproveita o binario ja baixado pelo primeiro - nenhuma
# credencial, nenhum plano, nenhum estado passa por este cache (so
# binarios de provider publicos).
#
# Nunca modifica .terraform.lock.hcl por si so - o lock file continua
# fixando as versoes/checksums normalmente; o cache so evita o download
# redundante do binario ja resolvido pelo lock.
#
# Uso:
#   sh scripts/ci/terraform-cached.sh <dir-do-ambiente-relativo-ao-repo> <argumentos-terraform...>
#
# Exemplos:
#   sh scripts/ci/terraform-cached.sh infra/terraform/environments/aws-reference fmt -check
#   sh scripts/ci/terraform-cached.sh infra/terraform/environments/aws-reference init -input=false
#   sh scripts/ci/terraform-cached.sh infra/terraform/environments/localstack-hobby validate
#
# Variaveis de ambiente opcionais:
#   TERRAFORM_VERSION       versao da imagem hashicorp/terraform (default: 1.9)
#   TF_PLUGIN_CACHE_DIR_HOST  caminho de host para o cache compartilhado
#                             (default: infra/terraform/.terraform-plugin-cache,
#                             gitignored - ver infra/terraform/.gitignore)
#
# ATENCAO (achado real, fechamento pre-publicacao ): apos usar
# este wrapper, "infra/terraform/environments/<ambiente>/.terraform/providers/.../linux_amd64"
# no HOST fica sendo um symlink para um caminho ABSOLUTO
# ("/root/.terraform.d/plugin-cache/...") que so existe DENTRO do container
# especifico desta execucao - visto de qualquer outro contexto (o host
# diretamente, ou outro container com mounts diferentes), esse symlink esta
# QUEBRADO. Isso ja quebrou "docker build" (corrigido excluindo infra/ do
# contexto de build, ver .dockerignore) e as fixtures de
# Security.IntegrationTests que fazem bind-mount + "cp -R" da arvore
# infra/terraform do host (corrigido removendo ".terraform" residual da
# copia antes do proprio "terraform init" dessas fixtures). Se voce
# encontrar um erro "Required plugins are not installed" em qualquer
# consumidor futuro de infra/terraform, rode
# "rm -rf infra/terraform/environments/*/.terraform" (gitignored, seguro
# remover) antes de investigar mais a fundo.
set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
TERRAFORM_VERSION="${TERRAFORM_VERSION:-1.9}"
CACHE_DIR="${TF_PLUGIN_CACHE_DIR_HOST:-$REPO_ROOT/infra/terraform/.terraform-plugin-cache}"

fail() { echo "terraform-cached: FALHA: $1" >&2; exit 1; }

[ $# -ge 1 ] || fail "uso: $0 <dir-do-ambiente-relativo-ao-repo> <argumentos-terraform...>"

ENV_DIR="$1"
shift

[ -d "$REPO_ROOT/$ENV_DIR" ] || fail "diretorio de ambiente nao encontrado: ${ENV_DIR}"

mkdir -p "$CACHE_DIR"

# Nota Windows/Git Bash: "docker run -v" exige o formato de caminho que o
# Docker Desktop aceita: cygpath -m resolve isso quando disponivel; em
# Linux (incluindo GitHub-hosted runners) cygpath nao existe e o caminho
# original ja e o formato correto.
to_docker_path() {
  if command -v cygpath >/dev/null 2>&1; then
    cygpath -m "$1"
  else
    printf '%s' "$1"
  fi
}

REPO_ROOT_DOCKER="$(to_docker_path "$REPO_ROOT")"
CACHE_DIR_DOCKER="$(to_docker_path "$CACHE_DIR")"

# "-w //workspace/..." (barra dupla) em vez de "/workspace/...": evita que
# o MSYS/Git Bash no Windows re-traduza esse argumento como se fosse um
# caminho de HOST (heuristica do MSYS so dispara em argumentos que comecam
# com uma unica barra) - sem efeito em Linux, onde "//workspace" e
# equivalente a "/workspace".
docker run --rm \
  -v "${REPO_ROOT_DOCKER}:/workspace" \
  -v "${CACHE_DIR_DOCKER}:/root/.terraform.d/plugin-cache" \
  -w "//workspace/${ENV_DIR}" \
  -e "TF_PLUGIN_CACHE_DIR=//root/.terraform.d/plugin-cache" \
  "hashicorp/terraform:${TERRAFORM_VERSION}" "$@"
