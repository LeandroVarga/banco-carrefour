#!/bin/sh
# Publica no Amazon ECR as imagens JA validadas (SBOM, scan, non-root) -
# os 4 workloads de negocio (manifest.components) e os artefatos
# operacionais (manifest.operationalArtifacts, hoje: migration-runner) -
# usando o MESMO imageId registrado no manifesto de release - nunca
# reconstroi, nunca publica uma imagem diferente da que foi escaneada
# (build once, estendido para artefatos operacionais). So deve ser chamado depois de:
#   1) scripts/ci/generate-release-manifest.sh (manifesto em estado "pending")
#   2) scripts/ci/validate-release-manifest.sh (validado)
#   3) um login bem-sucedido no ECR (aws-actions/amazon-ecr-login no
#      workflow real, ou "aws ecr get-login-password | docker login"
#      equivalente)
#
# a versao
# anterior sempre tentava "docker push" e so verificava o digest DEPOIS.
# Corrigido para NUNCA depender do comportamento do ECR ao publicar em uma
# tag ja existente (a documentacao oficial so confirma que
# ImageTagAlreadyExistsException e retornada ao publicar em uma tag ja
# existente - nao confirma um caminho de "no-op" para conteudo identico):
#   1) consulta "aws ecr describe-images" PRIMEIRO, antes de qualquer push;
#   2) se a tag NAO existe -> publica normalmente -> verifica digest;
#   3) se a tag JA existe -> NUNCA tenta push - puxa a imagem existente
#      PELO DIGEST e compara RootFS.Layers (identidade de conteudo
#      portavel, a mesma tecnica de scripts/ci/test-generic-oci-publish-proof.sh)
#      contra o layerDigests do componente ja validado:
#        - conteudo igual  -> already_published (nenhum push, digest
#          existente preservado);
#        - conteudo diferente -> conflict (nenhum push, falha, exige
#          investigacao manual - nunca sobrescreve).
#
# NUNCA executado neste bloco - requer credenciais reais obtidas via OIDC
# em um runner hospedado pelo GitHub. Escrito e revisado estruturalmente,
# nao testado ponta a ponta localmente (sem conta AWS real disponivel).
#
# Variaveis de ambiente obrigatorias:
#   ECR_REGISTRY   ex.: 123456789012.dkr.ecr.us-east-1.amazonaws.com
#                  (normalmente steps.ecr-login.outputs.registry)
#   AWS_REGION     ex.: us-east-1
#
# Variaveis de ambiente exclusivas de teste isolado (usadas por
# scripts/ci/test-publish-idempotency.sh para exercitar os ramos de
# idempotencia/conflito com um "aws"/"docker" falso via PATH, sem tocar em
# artifacts/ real nem em uma conta AWS real):
#   RELEASE_MANIFEST_FILE   (default: artifacts/release/release-manifest.json)
#   RELEASE_IMAGES_FILE     (default: artifacts/sbom/images.json)
#
# Uso:
#   sh scripts/ci/publish-validated-images.sh
set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

log() { printf '%s\n' "$1"; }
fail() { echo "publish-validated-images: FALHA: $1" >&2; exit 1; }

: "${ECR_REGISTRY:?ECR_REGISTRY precisa estar definido (registry retornado pelo login no ECR).}"
: "${AWS_REGION:?AWS_REGION precisa estar definido.}"

MANIFEST_FILE="${RELEASE_MANIFEST_FILE:-artifacts/release/release-manifest.json}"
IMAGES_FILE="${RELEASE_IMAGES_FILE:-artifacts/sbom/images.json}"

[ -f "$MANIFEST_FILE" ] || fail "${MANIFEST_FILE} nao encontrado - rode generate-release-manifest.sh primeiro."
[ -f "$IMAGES_FILE" ] || fail "${IMAGES_FILE} nao encontrado - rode build-images-for-supply-chain.sh primeiro."

FAILURE_MARKER="$(dirname "$MANIFEST_FILE")/.publish-failed"
rm -f "$FAILURE_MARKER"

COMPONENTS=$(python3 - "$MANIFEST_FILE" <<'PYEOF' | tr -d '\r'
import json, sys

manifest_path = sys.argv[1]
data = json.load(open(manifest_path))
for c in data['components'] + data['operationalArtifacts']:
    if c.get('publicationStatus') != 'pending':
        continue
    print(c['component'] + '|' + c['localImageId'] + '|' + c['canonicalTag'])
PYEOF
)

[ -n "$COMPONENTS" ] || fail "nenhum componente em estado 'pending' no manifesto - nada a publicar (ja publicado ou manifesto vazio)."

echo "$COMPONENTS" | while IFS='|' read -r NAME LOCAL_IMAGE_ID CANONICAL_TAG; do
  [ -z "$NAME" ] && continue

  LOCAL_REF=$(python3 - "$IMAGES_FILE" "$NAME" <<'PYEOF'
import json, sys

images_path, name = sys.argv[1:3]
data = json.load(open(images_path))
entry = next(e for e in data if e['component'] == name)
print(entry['image'])
PYEOF
)

  ACTUAL_ID=$(docker image inspect "$LOCAL_REF" --format '{{.Id}}')
  [ "$ACTUAL_ID" = "$LOCAL_IMAGE_ID" ] \
    || fail "${NAME}: imageId local (${ACTUAL_ID}) difere do manifesto (${LOCAL_IMAGE_ID}) - a imagem foi reconstruida, quebrando build-once. Publicacao abortada."

  REPOSITORY="banco-carrefour/${NAME}"
  REMOTE_TAG_REF="${ECR_REGISTRY}/${REPOSITORY}:${CANONICAL_TAG}"

  log "=== ${NAME}: consultando estado atual da tag canonica no ECR (sem publicar ainda) ==="
  DESCRIBE_STDERR="$(mktemp 2>/dev/null || echo /tmp/describe-stderr-$$)"
  EXISTING_DIGEST=""
  DESCRIBE_EXIT=0
  EXISTING_DIGEST=$(aws ecr describe-images \
    --region "$AWS_REGION" \
    --repository-name "$REPOSITORY" \
    --image-ids "imageTag=${CANONICAL_TAG}" \
    --query 'imageDetails[0].imageDigest' \
    --output text 2>"$DESCRIBE_STDERR") || DESCRIBE_EXIT=$?

  if [ "$DESCRIBE_EXIT" -ne 0 ] && grep -q "ImageNotFoundException" "$DESCRIBE_STDERR"; then
    rm -f "$DESCRIBE_STDERR"
    log "  tag ${CANONICAL_TAG} ausente no repositorio ${REPOSITORY} - publicacao elegivel."

    docker tag "$LOCAL_REF" "$REMOTE_TAG_REF"
    PUSH_OUTPUT=$(docker push "$REMOTE_TAG_REF")
    PUSH_DIGEST=$(printf '%s\n' "$PUSH_OUTPUT" | grep -oE 'sha256:[0-9a-f]{64}' | head -1)
    [ -n "$PUSH_DIGEST" ] || fail "${NAME}: nao foi possivel extrair o digest da saida de 'docker push'."

    # Verificacao cruzada: o digest que o proprio "docker push" reporta
    # tem que corresponder EXATAMENTE ao que o ECR relata via
    # DescribeImages - nunca confiamos apenas na saida do push.
    ECR_DIGEST=$(aws ecr describe-images \
      --region "$AWS_REGION" \
      --repository-name "$REPOSITORY" \
      --image-ids "imageTag=${CANONICAL_TAG}" \
      --query 'imageDetails[0].imageDigest' \
      --output text)

    [ "$PUSH_DIGEST" = "$ECR_DIGEST" ] \
      || fail "${NAME}: digest do 'docker push' (${PUSH_DIGEST}) diverge do digest reportado por 'aws ecr describe-images' (${ECR_DIGEST})."

    log "  digest remoto verificado (docker push == aws ecr describe-images): ${ECR_DIGEST}"
    STATUS="published"
    FINAL_DIGEST="$ECR_DIGEST"
  elif [ "$DESCRIBE_EXIT" -ne 0 ]; then
    ERR_MSG="$(cat "$DESCRIBE_STDERR")"
    rm -f "$DESCRIBE_STDERR"
    fail "${NAME}: falha ao consultar 'aws ecr describe-images' por um motivo diferente de tag ausente: ${ERR_MSG}"
  else
    rm -f "$DESCRIBE_STDERR"
    log "  tag ${CANONICAL_TAG} JA EXISTE em ${REPOSITORY} (digest ${EXISTING_DIGEST}) - NUNCA publicando por cima; verificando se e o mesmo conteudo antes de decidir."

    # Nunca tenta push numa tag existente: puxa o conteudo ja publicado
    # PELO DIGEST e compara a identidade de conteudo (RootFS.Layers) contra
    # o que este pipeline ja validou - a mesma tecnica de
    # scripts/ci/test-generic-oci-publish-proof.sh. Isso evita depender de
    # qualquer suposicao sobre o comportamento do ECR ao reenviar conteudo
    # identico para uma tag imutavel.
    EXISTING_REF="${ECR_REGISTRY}/${REPOSITORY}@${EXISTING_DIGEST}"
    docker pull "$EXISTING_REF" >/dev/null
    EXISTING_LAYERS=$(docker image inspect "$EXISTING_REF" --format '{{json .RootFS.Layers}}')
    EXPECTED_LAYERS=$(python3 - "$MANIFEST_FILE" "$NAME" <<'PYEOF'
import json, sys

manifest_path, name = sys.argv[1:3]
data = json.load(open(manifest_path))
c = next(x for x in data['components'] + data['operationalArtifacts'] if x['component'] == name)
print(json.dumps(c['layerDigests']))
PYEOF
)

    if [ "$EXISTING_LAYERS" = "$EXPECTED_LAYERS" ]; then
      log "  conteudo identico confirmado (RootFS.Layers) - publicacao idempotente, nenhum push necessario."
      STATUS="already_published"
      FINAL_DIGEST="$EXISTING_DIGEST"
    else
      echo "publish-validated-images: ${NAME}: CONFLITO DE TAG IMUTAVEL - a tag ${CANONICAL_TAG} ja existe em ${REPOSITORY} com conteudo DIFERENTE do validado neste pipeline (digest existente ${EXISTING_DIGEST}). Nenhum push foi tentado. Requer investigacao manual." >&2
      STATUS="conflict"
      FINAL_DIGEST="$EXISTING_DIGEST"
      echo "1" > "$FAILURE_MARKER"
    fi
  fi

  python3 - "$MANIFEST_FILE" "$NAME" "$FINAL_DIGEST" "$STATUS" "${ECR_REGISTRY}/${REPOSITORY}" <<'PYEOF'
import json, os, sys

manifest_path, name, digest, status, repo_uri = sys.argv[1:6]

with open(manifest_path) as f:
    manifest = json.load(f)

# migration-runner (e qualquer futuro artefato operacional) e publicado
# pela MESMA logica de idempotencia/conflito que os 4 workloads de
# negocio - a unica diferenca e ONDE o resultado e gravado (operational
# entries tambem ganham digestQualifiedReference, exigido pelo schema
# 4.0.0 para consumo direto no registro da task definition ECS).
all_entries = manifest["components"] + manifest["operationalArtifacts"]
for c in all_entries:
    if c["component"] != name:
        continue
    c["remoteEcrDigest"] = digest
    c["ecrRepositoryUri"] = repo_uri
    c["publicationStatus"] = status
    if "digestQualifiedReference" in c:
        c["digestQualifiedReference"] = f"{repo_uri}@{digest}" if digest else None
    if os.environ.get("GITHUB_RUN_ID"):
        c["workflowRunId"] = os.environ["GITHUB_RUN_ID"]
    if os.environ.get("GITHUB_RUN_ATTEMPT"):
        c["workflowRunAttempt"] = os.environ["GITHUB_RUN_ATTEMPT"]

statuses = [c["publicationStatus"] for c in all_entries]
if any(s in ("conflict", "failed") for s in statuses):
    manifest["overallPublicationStatus"] = "failed"
elif all(s in ("published", "already_published") for s in statuses):
    manifest["overallPublicationStatus"] = "published"
elif any(s in ("published", "already_published") for s in statuses):
    manifest["overallPublicationStatus"] = "partial"
else:
    manifest["overallPublicationStatus"] = "pending"

# overallVerdict: agregado distinto (schemas/release-manifest.schema.json)
# - "failed" acompanha overallPublicationStatus=failed; "passed" exige
# publicacao completa E nenhuma atestacao pendente/falha (as atestacoes
# ainda nao rodaram neste ponto do pipeline, entao permanece "pending" ate
# o passo de atestacao em publish-images.yml recalcular).
any_attestation_failed = any(
    c.get(kind, {}).get("status") == "failed"
    for c in all_entries
    for kind in ("provenanceAttestation", "sbomAttestation")
)
any_attestation_unresolved = any(
    c.get(kind, {}).get("status") == "pending_hosted_execution"
    for c in all_entries
    for kind in ("provenanceAttestation", "sbomAttestation")
)
if manifest["overallPublicationStatus"] == "failed" or any_attestation_failed:
    manifest["overallVerdict"] = "failed"
elif manifest["overallPublicationStatus"] == "published" and not any_attestation_unresolved:
    manifest["overallVerdict"] = "passed"
else:
    manifest["overallVerdict"] = "pending"

with open(manifest_path, "w") as f:
    json.dump(manifest, f, indent=2)
PYEOF

  log "  manifesto atualizado: ${NAME} -> publicationStatus=${STATUS}"
done

if [ -f "$FAILURE_MARKER" ]; then
  rm -f "$FAILURE_MARKER"
  fail "um ou mais componentes tiveram conflito de tag imutavel ou falha - ver ${MANIFEST_FILE} (overallPublicationStatus) e as mensagens acima. Nenhuma tag existente foi sobrescrita."
fi

log ""
log "=== Publicacao concluida - componentes pendentes atualizados para published/already_published, nenhum conflito detectado ==="
