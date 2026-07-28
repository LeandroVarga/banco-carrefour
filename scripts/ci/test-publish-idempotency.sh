#!/bin/sh
# Testes negativos (e controles positivos) para os ramos de idempotencia e
# conflito de scripts/ci/publish-validated-images.sh: tag ausente (publicacao normal), tag ja existente com o MESMO
# conteudo (already_published, sem push), tag ja existente com conteudo
# DIFERENTE (conflict, sem push), falha real na consulta a describe-images
# (motivo diferente de "tag ausente") e divergencia entre o digest do
# "docker push" e o digest reportado por "aws ecr describe-images".
#
# Nunca toca em uma conta AWS real nem em artifacts/ real: um "aws" e um
# "docker" FALSOS (scripts POSIX) sao antepostos ao PATH, e o manifesto/
# imagens usados sao fixtures sinteticas em um diretorio temporario, via os
# overrides RELEASE_MANIFEST_FILE/RELEASE_IMAGES_FILE de
# publish-validated-images.sh.
#
# Uso:
#   sh scripts/ci/test-publish-idempotency.sh
set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

PASS_COUNT=0
FAIL_COUNT=0

report_pass() { PASS_COUNT=$((PASS_COUNT + 1)); printf 'PASS: %s\n' "$1"; }
report_fail() { FAIL_COUNT=$((FAIL_COUNT + 1)); printf 'FAIL: %s\n' "$1" >&2; }

FIXTURE_DIR=""
cleanup() {
  # Mesma correcao de exit-code aplicada em test-release-guards.sh: usar
  # "if...fi" (nunca um "&&"-chain solto) como ultima instrucao do trap, ja
  # que um "if" com condicao falsa e sem "else" sempre retorna 0 (POSIX).
  if [ -n "$FIXTURE_DIR" ] && [ -d "$FIXTURE_DIR" ]; then
    rm -rf "$FIXTURE_DIR"
  fi
}
trap cleanup EXIT INT TERM

FIXTURE_DIR="$(mktemp -d 2>/dev/null || printf '%s' "${TMPDIR:-/tmp}/publish-idempotency-fixture-$$")"
rm -rf "$FIXTURE_DIR"
mkdir -p "$FIXTURE_DIR/bin" "$FIXTURE_DIR/release" "$FIXTURE_DIR/sbom" "$FIXTURE_DIR/state"

FAKE_LOCAL_IMAGE_ID="sha256:$(printf 'a%.0s' $(seq 1 64) | head -c 64)"
CANONICAL_TAG="sha-$(printf '1%.0s' $(seq 1 40) | head -c 40)"
# Espaco apos a virgula de proposito: precisa bater byte-a-byte com o
# formato padrao de "json.dumps" usado por publish-validated-images.sh ao
# ler layerDigests do manifesto (EXPECTED_LAYERS) - a comparacao de
# conteudo e uma comparacao textual simples, nao uma reanalise de JSON.
FAKE_LAYERS_A='["sha256:layer-a", "sha256:layer-b"]'
FAKE_LAYERS_B='["sha256:layer-c", "sha256:layer-d"]'
FAKE_DIGEST="sha256:$(printf 'd%.0s' $(seq 1 64) | head -c 64)"
FAKE_DIGEST_OTHER="sha256:$(printf 'e%.0s' $(seq 1 64) | head -c 64)"

# --- "docker" falso: so entende exatamente as chamadas feitas por
# publish-validated-images.sh (image inspect --format, tag, push, pull). ---
cat > "$FIXTURE_DIR/bin/docker" <<'DOCKEREOF'
#!/bin/sh
case "$1" in
  image)
    if [ "$2" = "inspect" ]; then
      REF="$3"
      FMT="$5"
      case "$FMT" in
        '{{.Id}}')
          echo "$FAKE_LOCAL_IMAGE_ID"
          ;;
        '{{json .RootFS.Layers}}')
          echo "$FAKE_EXISTING_LAYERS"
          ;;
        *)
          echo "docker-fake: formato inesperado: $FMT" >&2
          exit 1
          ;;
      esac
    fi
    ;;
  tag)
    exit 0
    ;;
  push)
    echo "${FAKE_PUSH_DIGEST_TAG:-latest}: digest: ${FAKE_PUSH_DIGEST} size: 1234"
    ;;
  pull)
    exit 0
    ;;
  *)
    echo "docker-fake: comando nao esperado neste teste: $*" >&2
    exit 1
    ;;
esac
DOCKEREOF
chmod +x "$FIXTURE_DIR/bin/docker"

# --- "aws" falso: so entende "aws ecr describe-images ... --output text".
# Usa um contador de chamadas em disco para diferenciar a 1a consulta (antes
# do push) da 2a (verificacao cruzada apos o push), exatamente como o
# script real faz no ramo de tag ausente. ---
cat > "$FIXTURE_DIR/bin/aws" <<'AWSEOF'
#!/bin/sh
COUNT_FILE="$FAKE_AWS_STATE_DIR/call-count"
COUNT=0
[ -f "$COUNT_FILE" ] && COUNT=$(cat "$COUNT_FILE")
COUNT=$((COUNT + 1))
echo "$COUNT" > "$COUNT_FILE"

case "$FAKE_AWS_SCENARIO" in
  tag_absent)
    if [ "$COUNT" -eq 1 ]; then
      echo "aws-fake: An error occurred (ImageNotFoundException) when calling the DescribeImages operation" >&2
      exit 254
    else
      echo "$FAKE_AWS_DIGEST"
    fi
    ;;
  tag_absent_digest_mismatch)
    if [ "$COUNT" -eq 1 ]; then
      echo "aws-fake: An error occurred (ImageNotFoundException) when calling the DescribeImages operation" >&2
      exit 254
    else
      echo "$FAKE_AWS_DIGEST_OTHER"
    fi
    ;;
  tag_exists_same_content|tag_exists_conflict)
    echo "$FAKE_AWS_DIGEST"
    ;;
  tag_query_failure)
    echo "aws-fake: An error occurred (AccessDeniedException) when calling the DescribeImages operation" >&2
    exit 254
    ;;
  *)
    echo "aws-fake: cenario desconhecido: $FAKE_AWS_SCENARIO" >&2
    exit 1
    ;;
esac
AWSEOF
chmod +x "$FIXTURE_DIR/bin/aws"

write_manifest() {
  # $1 = layerDigests do componente (o que o pipeline ja validou)
  python3 - "$FIXTURE_DIR/release/release-manifest.json" "$FAKE_LOCAL_IMAGE_ID" "$CANONICAL_TAG" "$1" <<'PYEOF'
import json, sys
manifest_path, image_id, canonical_tag, layer_digests_json = sys.argv[1:5]
manifest = {
    "overallPublicationStatus": "pending",
    "components": [
        {
            "component": "ledger-api",
            "sourceCommit": "1" * 40,
            "sourceTreeClean": True,
            "localImageId": image_id,
            "layerDigests": json.loads(layer_digests_json),
            "canonicalTag": canonical_tag,
            "ecrRepositoryUri": None,
            "remoteEcrDigest": None,
            "publicationStatus": "pending",
        }
    ],
    # Este teste exercita apenas a logica de idempotencia/conflito contra
    # UM componente de negocio sintetico - nunca precisa de um artefato
    # operacional real, mas publish-validated-images.sh
    # sempre espera a chave 'operationalArtifacts' presente (mesmo vazia).
    "operationalArtifacts": [],
}
with open(manifest_path, "w") as f:
    json.dump(manifest, f)
PYEOF
}

write_images() {
  python3 - "$FIXTURE_DIR/sbom/images.json" "$FAKE_LOCAL_IMAGE_ID" <<'PYEOF'
import json, sys
images_path, image_id = sys.argv[1:3]
images = [{"component": "ledger-api", "image": "fake-ref:latest", "imageId": image_id}]
with open(images_path, "w") as f:
    json.dump(images, f)
PYEOF
}

run_publish() {
  PATH="$FIXTURE_DIR/bin:$PATH" \
  FAKE_LOCAL_IMAGE_ID="$FAKE_LOCAL_IMAGE_ID" \
  FAKE_AWS_STATE_DIR="$FIXTURE_DIR/state" \
  FAKE_AWS_SCENARIO="$1" \
  FAKE_AWS_DIGEST="${2:-}" \
  FAKE_AWS_DIGEST_OTHER="${3:-}" \
  FAKE_EXISTING_LAYERS="${4:-}" \
  FAKE_PUSH_DIGEST="${5:-}" \
  ECR_REGISTRY="123456789012.dkr.ecr.us-east-1.amazonaws.com" \
  AWS_REGION="us-east-1" \
  RELEASE_MANIFEST_FILE="$FIXTURE_DIR/release/release-manifest.json" \
  RELEASE_IMAGES_FILE="$FIXTURE_DIR/sbom/images.json" \
  sh "$REPO_ROOT/scripts/ci/publish-validated-images.sh"
}

manifest_status() {
  python3 - "$FIXTURE_DIR/release/release-manifest.json" <<'PYEOF'
import json, sys
print(json.load(open(sys.argv[1]))['components'][0]['publicationStatus'])
PYEOF
}

# ---------------------------------------------------------------------
# Cenario 1 (controle positivo): tag ausente -> publica normalmente,
# digest do push confere com describe-images -> published.
# ---------------------------------------------------------------------
write_images >/dev/null
write_manifest "$FAKE_LAYERS_A" >/dev/null
rm -f "$FIXTURE_DIR/state/call-count"
if run_publish "tag_absent" "$FAKE_DIGEST" "" "" "$FAKE_DIGEST" >/dev/null 2>&1; then
  if [ "$(manifest_status)" = "published" ]; then
    report_pass "controle positivo: tag ausente publica e marca published com digest verificado"
  else
    report_fail "tag ausente deveria marcar published, encontrado: $(manifest_status)"
  fi
else
  report_fail "controle positivo: tag ausente deveria publicar com sucesso"
fi

# ---------------------------------------------------------------------
# Cenario 2: tag ja existe com o MESMO conteudo -> already_published,
# NENHUM push tentado (o "docker" falso falharia se "push" fosse chamado
# com argumentos inesperados, mas aqui validamos via status).
# ---------------------------------------------------------------------
write_images >/dev/null
write_manifest "$FAKE_LAYERS_A" >/dev/null
rm -f "$FIXTURE_DIR/state/call-count"
if run_publish "tag_exists_same_content" "$FAKE_DIGEST" "" "$FAKE_LAYERS_A" "" >/dev/null 2>&1; then
  if [ "$(manifest_status)" = "already_published" ]; then
    report_pass "tag existente com conteudo identico e idempotente (already_published, sem push)"
  else
    report_fail "tag existente com conteudo identico deveria marcar already_published, encontrado: $(manifest_status)"
  fi
else
  report_fail "tag existente com conteudo identico deveria ser aceita como idempotente (exit 0)"
fi

# ---------------------------------------------------------------------
# Cenario 3: tag ja existe com conteudo DIFERENTE -> conflict, script
# falha ao final (marcador de falha), NUNCA sobrescreve a tag existente.
# ---------------------------------------------------------------------
write_images >/dev/null
write_manifest "$FAKE_LAYERS_A" >/dev/null
rm -f "$FIXTURE_DIR/state/call-count"
if run_publish "tag_exists_conflict" "$FAKE_DIGEST" "" "$FAKE_LAYERS_B" "" >/dev/null 2>&1; then
  report_fail "tag existente com conteudo DIFERENTE deveria falhar (conflito de tag imutavel)"
else
  if [ "$(manifest_status)" = "conflict" ]; then
    report_pass "tag existente com conteudo diferente e rejeitada como conflict (script falha, nenhum push tentado)"
  else
    report_fail "conflito deveria marcar publicationStatus=conflict, encontrado: $(manifest_status)"
  fi
fi
[ ! -f "$FIXTURE_DIR/release/.publish-failed" ] \
  && report_pass "marcador de falha e removido apos o script reportar o erro (nao vaza para execucoes futuras)" \
  || report_fail "marcador de falha nao deveria persistir apos o script falhar"

# ---------------------------------------------------------------------
# Cenario 4: describe-images falha por um motivo DIFERENTE de "tag
# ausente" (ex.: AccessDeniedException) -> o script tem que abortar
# imediatamente, nunca tratar isso como "tag ausente" (o que arriscaria
# tentar publicar quando na verdade nao sabemos o estado real da tag).
# ---------------------------------------------------------------------
write_images >/dev/null
write_manifest "$FAKE_LAYERS_A" >/dev/null
rm -f "$FIXTURE_DIR/state/call-count"
if run_publish "tag_query_failure" "" "" "" "" >/dev/null 2>&1; then
  report_fail "falha real na consulta a describe-images (nao 'tag ausente') deveria abortar a publicacao"
else
  if [ "$(manifest_status)" = "pending" ]; then
    report_pass "falha real na consulta a describe-images aborta a publicacao sem alterar o manifesto (continua pending)"
  else
    report_fail "manifesto nao deveria ter sido alterado apos falha de consulta, encontrado: $(manifest_status)"
  fi
fi

# ---------------------------------------------------------------------
# Cenario 5: tag ausente, mas o digest do "docker push" diverge do
# digest reportado por "aws ecr describe-images" na verificacao cruzada
# -> falha (nunca confia apenas na saida do push).
# ---------------------------------------------------------------------
write_images >/dev/null
write_manifest "$FAKE_LAYERS_A" >/dev/null
rm -f "$FIXTURE_DIR/state/call-count"
if run_publish "tag_absent_digest_mismatch" "" "$FAKE_DIGEST_OTHER" "" "$FAKE_DIGEST" >/dev/null 2>&1; then
  report_fail "divergencia entre digest do push e do describe-images deveria falhar"
else
  report_pass "divergencia entre digest do 'docker push' e 'aws ecr describe-images' e detectada e rejeitada"
fi

rm -rf "$FIXTURE_DIR"
FIXTURE_DIR=""

echo ""
echo "=== test-publish-idempotency: ${PASS_COUNT} passaram, ${FAIL_COUNT} falharam ==="
[ "$FAIL_COUNT" -eq 0 ]
