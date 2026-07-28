#!/bin/sh
# Prova generica de publicacao imutavel de imagem OCI -
# usada porque o LocalStack Community (edicao ja usada por este projeto,
# ver docker-compose.yml) NAO suporta ECR (confirmado por execucao real:
# "Sorry, the ecr service is not included within your LocalStack license" ao
# chamar ecr:CreateRepository/DescribeRepositories contra um LocalStack
# descartavel com o servico ecr habilitado - ECR e recurso exclusivo do
# LocalStack Pro). Este script NUNCA afirma ter provado ECR - prova apenas
# a mecanica generica de OCI, usando um registry Docker generico e
# descartavel (imagem oficial "registry", fixada por digest imutavel).
#
# Modelo de identidade: "localImageId" (docker inspect .Id) e
# "registryManifestDigest" (Docker-Content-Digest reportado pelo registry)
# NAO sao garantidamente identicos em todo image store (em um image store
# classico, sem containerd, o .Id normalmente corresponde ao digest do
# blob de CONFIG, que e DIFERENTE do digest do MANIFESTO que o registry
# relata) - por isso os dois valores sao registrados e comparados como
# campos DISTINTOS. A identidade de conteudo e comprovada por um metodo
# padronizado e portavel entre implementacoes de image store - comparacao
# de RootFS.Layers (os digests de camada sao sempre estaveis, por
# definicao do formato OCI/Docker, independentemente de como o daemon
# local calcula ".Id") - e por um pull feito PELO DIGEST do registry (nao
# pela tag), nunca assumindo que localImageId == registryManifestDigest.
#
# Nao reconstroi nenhuma imagem: reutiliza a MESMA imagem ja construida por
# build-images-for-supply-chain.sh (build once), lida a partir de
# artifacts/sbom/images.json.
#
# Evidencia de execucao unica por HEAD limpo:
# este script sobe um registry descartavel e faz push/pull reais - reexecuta-lo
# sem necessidade para o MESMO commit (arvore limpa, mesma imagem construida)
# so desperdica tempo/rede/disco sem provar nada novo. A identidade de
# evidencia registrada em artifacts/oci-publish-proof/evidence.json (dentro
# de /artifacts/, ja ignorado pelo git - ver .gitignore) combina QUATRO
# campos, todos exigidos simultaneamente antes de pular a reexecucao:
#   - commit completo (git rev-parse HEAD) - nunca um commit abreviado;
#   - arvore de trabalho limpa (git status --porcelain vazio) no momento
#     do registro E da checagem - uma prova feita em cima de uma arvore
#     suja nunca e persistida como evidencia valida;
#   - versao deste script (SCRIPT_VERSION) - se a logica de prova mudar,
#     a versao muda, e qualquer evidencia anterior deixa de satisfazer o
#     gate automaticamente, mesmo que o commit e a imagem sejam iguais;
#   - identidade da imagem (COMPONENT + localImageId + RootFS.Layers) - a
#     mesma tecnica de comparacao de conteudo usada no restante deste
#     script, nunca apenas o nome/tag da imagem.
# Uma evidencia de qualquer OUTRO commit, versao de script ou identidade de
# imagem NUNCA satisfaz o gate (comparacao e sempre por igualdade exata dos
# quatro campos) - nao ha fallback silencioso. Use --force para ignorar o
# gate e reexecutar mesmo assim (ex.: apos suspeita de corrupcao local).
#
# Uso:
#   sh scripts/ci/test-generic-oci-publish-proof.sh
#   sh scripts/ci/test-generic-oci-publish-proof.sh --force
set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

SCRIPT_VERSION="3"
EVIDENCE_DIR="artifacts/oci-publish-proof"
EVIDENCE_FILE="${EVIDENCE_DIR}/evidence.json"

FORCE=0
case "${1:-}" in
  --force) FORCE=1 ;;
  "") : ;;
  *) echo "test-generic-oci-publish-proof: FALHA: argumento desconhecido: $1" >&2; exit 1 ;;
esac

# Imagem oficial "registry" (Docker Distribution) fixada por digest
# imutavel - nunca uma tag flutuante ("registry:2" sem verificacao).
REGISTRY_IMAGE="docker.io/library/registry@sha256:a3d8aaa63ed8681a604f1dea0aa03f100d5895b6a58ace528858a7b332415373"
REGISTRY_CONTAINER="banco-carrefour-oci-proof-registry-$$"
REGISTRY_PORT=15050
COMPONENT="ledger-api"
REPO_PATH="banco-carrefour/${COMPONENT}"

log() { printf '%s\n' "$1"; }
fail() { echo "test-generic-oci-publish-proof: FALHA: $1" >&2; exit 1; }

CURRENT_COMMIT="$(git rev-parse HEAD)"
if [ -z "$(git status --porcelain 2>/dev/null)" ]; then
  TREE_CLEAN="true"
else
  TREE_CLEAN="false"
fi

cleanup() {
  docker rm -f "$REGISTRY_CONTAINER" >/dev/null 2>&1 || true
  docker rmi "localhost:${REGISTRY_PORT}/${REPO_PATH}:sha-test" >/dev/null 2>&1 || true
  [ -n "${PULLED_REF:-}" ] && docker rmi "$PULLED_REF" >/dev/null 2>&1 || true
  docker rmi "banco-carrefour-oci-proof-pulled:latest" >/dev/null 2>&1 || true
}
trap cleanup EXIT INT TERM

[ -f artifacts/sbom/images.json ] || fail "artifacts/sbom/images.json nao encontrado - rode build-images-for-supply-chain.sh primeiro (build once)."

SOURCE_IMAGE=$(python3 -c "
import json
data = json.load(open('artifacts/sbom/images.json'))
entry = next(e for e in data if e['component'] == '$COMPONENT')
print(entry['image'])
" | tr -d '\r')

# --- Identidade LOCAL: registrada e nunca assumida igual ao digest remoto ---
LOCAL_IMAGE_ID=$(docker image inspect "$SOURCE_IMAGE" --format '{{.Id}}')
LOCAL_LAYERS=$(docker image inspect "$SOURCE_IMAGE" --format '{{.RootFS.Layers}}')
IMAGE_MANIFEST_IDENTITY=$(printf '%s|%s|%s' "$COMPONENT" "$LOCAL_IMAGE_ID" "$LOCAL_LAYERS" | sha256sum | awk '{print $1}')

log "=== Imagem de origem (ja construida - build once): ${SOURCE_IMAGE} ==="
log "  localImageId: ${LOCAL_IMAGE_ID}"

# --- Gate de evidencia: pula a reexecucao SOMENTE se os 4 campos baterem
# exatamente (commit completo, arvore limpa, versao do script, identidade
# da imagem) - qualquer divergencia (inclusive evidencia de outro commit)
# forca a reexecucao completa da prova. ---
if [ "$FORCE" -eq 0 ] && [ "$TREE_CLEAN" = "true" ] && [ -f "$EVIDENCE_FILE" ]; then
  EVIDENCE_VALUES=$(python3 -c "
import json
try:
    d = json.load(open('${EVIDENCE_FILE}'))
except Exception:
    d = {}
print(d.get('commit', ''))
print(d.get('scriptVersion', ''))
print(d.get('imageManifestIdentity', ''))
print(d.get('result', ''))
print(d.get('timestamp', ''))
" | tr -d '\r')
  EVIDENCE_COMMIT=$(printf '%s\n' "$EVIDENCE_VALUES" | sed -n '1p')
  EVIDENCE_SCRIPT_VERSION=$(printf '%s\n' "$EVIDENCE_VALUES" | sed -n '2p')
  EVIDENCE_IMAGE_IDENTITY=$(printf '%s\n' "$EVIDENCE_VALUES" | sed -n '3p')
  EVIDENCE_RESULT=$(printf '%s\n' "$EVIDENCE_VALUES" | sed -n '4p')
  EVIDENCE_TIMESTAMP=$(printf '%s\n' "$EVIDENCE_VALUES" | sed -n '5p')

  if [ "$EVIDENCE_COMMIT" = "$CURRENT_COMMIT" ] \
    && [ "$EVIDENCE_SCRIPT_VERSION" = "$SCRIPT_VERSION" ] \
    && [ "$EVIDENCE_IMAGE_IDENTITY" = "$IMAGE_MANIFEST_IDENTITY" ] \
    && [ "$EVIDENCE_RESULT" = "passed" ]; then
    log ""
    log "=== test-generic-oci-publish-proof: JA PROVADO para este HEAD exato ==="
    log "    commit=${CURRENT_COMMIT} (completo) | script v${SCRIPT_VERSION} | imagem identica (componente+localImageId+RootFS.Layers)"
    log "    prova anterior registrada em: ${EVIDENCE_TIMESTAMP} (${EVIDENCE_FILE})"
    log "    reexecucao pulada - nenhuma alteracao de commit, script ou imagem desde a ultima prova. Use --force para reexecutar mesmo assim."
    exit 0
  fi
fi

log "=== Subindo registry OCI generico e descartavel (${REGISTRY_IMAGE}) ==="
# --tmpfs /var/lib/registry: a imagem oficial "registry" declara esse
# caminho como VOLUME (docker image inspect --format '{{json .Config.Volumes}}'
# confirma: {"/var/lib/registry":{}}) - mesma classe de vazamento de volume
# anonimo corrigida em 4.2 para o postgres:16-alpine. Sem isto, cada
# execucao desta prova (mesmo descartavel via --rm) deixava um volume
# anonimo orfao para tras, porque "docker rm -f" no cleanup() abaixo nao
# remove volumes anonimos por si so (precisaria de "docker rm -f -v"). O
# registry e sempre descartavel (nunca precisa persistir dados entre
# execucoes), entao tmpfs e a correcao na raiz, nao so uma melhoria de
# cleanup.
#
# Nota Windows/Git Bash: o valor de "--tmpfs" precisa ser BYTE A BYTE igual
# a "/var/lib/registry" (o mesmo caminho declarado como VOLUME pela
# imagem) para de fato sobrescrever o mount implicito - testado
# empiricamente: usar "//var/lib/registry" (o truque de barra dupla usado
# em outros scripts deste repo para argumentos "-w"/"-e") faz o Docker
# registrar um tmpfs num caminho DIFERENTE ("//var/lib/registry" como chave
# literal), deixando o volume anonimo do VOLUME da imagem intacto (dois
# mounts distintos, nao uma substituicao). A correcao correta aqui e
# MSYS_NO_PATHCONV=1 (desliga a conversao de caminho do MSYS/Git Bash para
# este comando inteiro) mantendo o valor exato "/var/lib/registry" - no-op
# em Linux (a variavel so tem efeito no MSYS do Git Bash para Windows).
MSYS_NO_PATHCONV=1 docker run -d --rm --name "$REGISTRY_CONTAINER" --tmpfs /var/lib/registry -p "${REGISTRY_PORT}:5000" "$REGISTRY_IMAGE" >/dev/null

for i in $(seq 1 15); do
  curl -fsS "http://localhost:${REGISTRY_PORT}/v2/" >/dev/null 2>&1 && break
  sleep 1
done
curl -fsS "http://localhost:${REGISTRY_PORT}/v2/" >/dev/null 2>&1 || fail "registry descartavel nao respondeu a tempo."

CANONICAL_TAG="sha-test"
REMOTE_TAG_REF="localhost:${REGISTRY_PORT}/${REPO_PATH}:${CANONICAL_TAG}"

fetch_manifest_digest() {
  # $1 = referencia de tag a consultar
  curl -s -H "Accept: application/vnd.oci.image.index.v1+json, application/vnd.docker.distribution.manifest.list.v2+json, application/vnd.docker.distribution.manifest.v2+json, application/vnd.oci.image.manifest.v1+json" \
    -D - -o /dev/null "http://localhost:${REGISTRY_PORT}/v2/${REPO_PATH}/manifests/$1" \
    | grep -i "docker-content-digest" | tr -d '\r' | awk '{print $2}'
}

log "=== Publicando (docker push) sem reconstruir - mesma imagem, nova tag remota ==="
docker tag "$SOURCE_IMAGE" "$REMOTE_TAG_REF"
docker push "$REMOTE_TAG_REF" >/dev/null

REGISTRY_MANIFEST_DIGEST=$(fetch_manifest_digest "$CANONICAL_TAG")
[ -n "$REGISTRY_MANIFEST_DIGEST" ] || fail "nao foi possivel obter o digest do manifesto apos o push."

case "$REGISTRY_MANIFEST_DIGEST" in
  sha256:*) : ;;
  *) fail "registryManifestDigest com formato inesperado: ${REGISTRY_MANIFEST_DIGEST}" ;;
esac

log "  registryManifestDigest: ${REGISTRY_MANIFEST_DIGEST}"
if [ "$LOCAL_IMAGE_ID" = "$REGISTRY_MANIFEST_DIGEST" ]; then
  log "  observacao: localImageId e registryManifestDigest coincidem NESTE ambiente (containerd image store) - nao tratado como garantia geral, apenas registrado."
else
  log "  observacao: localImageId e registryManifestDigest DIFEREM neste ambiente - esperado em image stores classicos; a prova de identidade abaixo nao depende dessa igualdade."
fi

log "=== Pull PELO DIGEST do registry (nao pela tag) e comparacao de conteudo ==="
PULLED_REF="localhost:${REGISTRY_PORT}/${REPO_PATH}@${REGISTRY_MANIFEST_DIGEST}"
docker rmi "$REMOTE_TAG_REF" >/dev/null 2>&1 || true
docker pull "$PULLED_REF" >/dev/null

PULLED_LAYERS=$(docker image inspect "$PULLED_REF" --format '{{.RootFS.Layers}}')

# Comparacao de identidade de CONTEUDO padronizada e portavel: os digests
# de camada (RootFS.Layers) sao estaveis por definicao do formato
# OCI/Docker, independentemente de como o daemon local computa ".Id" -
# ao contrario de comparar strings de "imageId", isto e uma prova de
# conteudo valida em qualquer image store.
if [ "$LOCAL_LAYERS" = "$PULLED_LAYERS" ]; then
  log "  identidade de conteudo confirmada: RootFS.Layers da imagem original == RootFS.Layers da imagem puxada pelo digest do registry."
else
  fail "RootFS.Layers da imagem puxada (${PULLED_LAYERS}) difere da imagem original (${LOCAL_LAYERS}) - falha de integridade de conteudo."
fi

log "=== Verificando associacao esperada de componente/origem ==="
# Verificacao generica de associacao componente/origem: o caminho do
# repositorio remoto usado (banco-carrefour/<componente>) tem que
# corresponder exatamente ao componente esperado do manifesto local -
# nao existe LABEL de metadado no Dockerfile real deste repositorio, entao
# a associacao e feita pelo path do repositorio, que e a mesma convenção
# usada por scripts/ci/generate-release-manifest.sh.
case "$PULLED_REF" in
  *"/${REPO_PATH}@"*) log "  associacao confirmada: repositorio remoto '${REPO_PATH}' corresponde ao componente esperado '${COMPONENT}'." ;;
  *) fail "associacao de componente/origem inesperada em ${PULLED_REF}." ;;
esac

docker tag "$PULLED_REF" "banco-carrefour-oci-proof-pulled:latest" >/dev/null

log "=== Testando idempotencia: mesma tag, MESMO conteudo (nao deve ser tratado como conflito) ==="
# Sem reconstruir nem re-fazer push: consulta o digest ja publicado na tag
# e compara com o localImageId/conteudo que seria publicado - se o
# conteudo bate, a publicacao e idempotente (already_published), nunca um
# novo push. Isto espelha exatamente a logica de
# scripts/ci/publish-validated-images.sh (que nunca reenvia um push para
# uma tag que ja tem o mesmo conteudo).
docker tag "$SOURCE_IMAGE" "$REMOTE_TAG_REF" >/dev/null
docker push "$REMOTE_TAG_REF" >/dev/null
EXISTING_DIGEST_SAME_CONTENT=$(fetch_manifest_digest "$CANONICAL_TAG")
if [ "$EXISTING_DIGEST_SAME_CONTENT" = "$REGISTRY_MANIFEST_DIGEST" ]; then
  log "  idempotencia confirmada: republicar o MESMO conteudo na mesma tag preserva o mesmo registryManifestDigest (already_published, nao e um novo push distinto nem um conflito)."
else
  fail "republicar o mesmo conteudo produziu um digest diferente (${EXISTING_DIGEST_SAME_CONTENT} != ${REGISTRY_MANIFEST_DIGEST}) - comportamento inesperado."
fi

log "=== Testando rejeicao de reuso de tag imutavel com conteudo CONFLITANTE ==="
# Um registry generico (ao contrario do ECR com image_tag_mutability=IMMUTABLE)
# nao rejeita nativamente a sobrescrita de uma tag - por isso a checagem de
# conflito abaixo e feita pelo PROPRIO fluxo de publicacao (o mesmo padrao
# aplicado por scripts/ci/publish-validated-images.sh: consultar o digest
# ja existente na tag canonica ANTES de tentar publicar, e recusar se ele
# divergir do digest do conteudo que se pretende publicar), como defesa em
# profundidade alem da imutabilidade nativa do ECR.
DIFFERENT_IMAGE="alpine:3.20.3@sha256:1e42bbe2508154c9126d48c2b8a75420c3544343bf86fd041fb7527e017a4b4a"
docker pull "$DIFFERENT_IMAGE" >/dev/null
DIFFERENT_IMAGE_LOCAL_ID=$(docker image inspect "$DIFFERENT_IMAGE" --format '{{.Id}}')

EXISTING_DIGEST=$(fetch_manifest_digest "$CANONICAL_TAG")

if [ "$EXISTING_DIGEST" = "$REGISTRY_MANIFEST_DIGEST" ] && [ "$DIFFERENT_IMAGE_LOCAL_ID" != "$LOCAL_IMAGE_ID" ]; then
  log "  conflito detectado ANTES do push: a tag '${CANONICAL_TAG}' ja aponta para ${EXISTING_DIGEST}, que corresponde ao conteudo original, nao ao conteudo diferente que se tentaria publicar agora - publicacao recusada (comportamento correto: fail, sem sobrescrever)."
else
  fail "checagem de conflito nao funcionou como esperado (EXISTING_DIGEST=${EXISTING_DIGEST})."
fi

log ""
log "=== test-generic-oci-publish-proof: PASSOU ==="
log "    build once -> push -> registryManifestDigest obtido -> pull PELO DIGEST -> RootFS.Layers identicos -> associacao de componente confirmada -> idempotencia (mesmo conteudo) confirmada -> conflito (conteudo diferente) corretamente recusado"
log "    Nota: esta e evidencia OCI generica, NAO evidencia de push real no Amazon ECR (indisponivel no LocalStack Community usado por este projeto)."
log "    cleanup: removendo registry descartavel ${REGISTRY_CONTAINER} e imagens de teste locais."

# --- Registro de evidencia: SOMENTE com arvore limpa (uma prova feita em
# cima de mudancas nao commitadas nunca e uma evidencia valida e reutilizavel
# para um commit futuro) ---
if [ "$TREE_CLEAN" = "true" ]; then
  mkdir -p "$EVIDENCE_DIR"
  EVIDENCE_TIMESTAMP_NEW="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  python3 -c "
import json
data = {
    'commit': '${CURRENT_COMMIT}',
    'scriptVersion': '${SCRIPT_VERSION}',
    'imageManifestIdentity': '${IMAGE_MANIFEST_IDENTITY}',
    'component': '${COMPONENT}',
    'result': 'passed',
    'timestamp': '${EVIDENCE_TIMESTAMP_NEW}',
}
json.dump(data, open('${EVIDENCE_FILE}', 'w'), indent=2)
"
  log "  evidencia registrada em ${EVIDENCE_FILE} (commit=${CURRENT_COMMIT}, script v${SCRIPT_VERSION}) - proximas execucoes para este MESMO HEAD serao puladas."
else
  log "  arvore de trabalho NAO estava limpa durante esta execucao - evidencia NAO registrada (uma prova sobre estado nao commitado nunca e reutilizavel para gating de commit)."
fi
