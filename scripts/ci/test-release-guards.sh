#!/bin/sh
# Testes negativos (e controles positivos) para os guardas de release e
# publicacao criados:
#   - scripts/ci/check-release-prerequisites.sh (variaveis ausentes/ARN
#     invalido - sem nunca chamar a AWS)
#   - scripts/ci/generate-release-manifest.sh (bloqueia publicacao para
#     veredito fail/scan_error)
#   - scripts/ci/validate-release-manifest.sh (tag mutavel, SBOM ausente,
#     publicado sem digest, digest malformado)
#
# Nunca modifica o repositorio real nem artifacts/ real - todas as
# fixtures sao construidas em um diretorio temporario.
#
# Uso:
#   sh scripts/ci/test-release-guards.sh
set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

PASS_COUNT=0
FAIL_COUNT=0

report_pass() { PASS_COUNT=$((PASS_COUNT + 1)); printf 'PASS: %s\n' "$1"; }
report_fail() { FAIL_COUNT=$((FAIL_COUNT + 1)); printf 'FAIL: %s\n' "$1" >&2; }

FIXTURE_DIR=""
cleanup() {
  # um "&&"-chain solto (sem "if") como statement de topo tem,
  # como status de saida do proprio statement, o status do PRIMEIRO teste
  # que falhar - se $FIXTURE_DIR estiver vazio, isso e "1", e como esta e
  # a ULTIMA instrucao da funcao chamada por "trap ... EXIT", esse "1"
  # se torna o codigo de saida FINAL do script inteiro, mesmo com todos os
  # testes internos passando (FAIL_COUNT=0) - reproduzido e confirmado
  # neste script antes da correcao. "if...fi" sempre retorna 0 quando a
  # condicao e falsa e nao ha "else" (regra POSIX), por isso e seguro.
  if [ -n "$FIXTURE_DIR" ] && [ -d "$FIXTURE_DIR" ]; then
    rm -rf "$FIXTURE_DIR"
  fi
}
trap cleanup EXIT INT TERM

# ---------------------------------------------------------------------
# Grupo 1: check-release-prerequisites.sh - sem nunca chamar a AWS
# ---------------------------------------------------------------------
if AWS_REGION="" ECR_PUBLISHER_ROLE_ARN="" ECR_REGISTRY="" sh scripts/ci/check-release-prerequisites.sh >/dev/null 2>&1; then
  report_fail "variaveis ausentes deveriam ser rejeitadas"
else
  report_pass "variaveis ausentes sao rejeitadas (sem tentar autenticar na AWS)"
fi

VALID_ENV="AWS_REGION=us-east-1 ECR_PUBLISHER_ROLE_ARN=arn:aws:iam::123456789012:role/banco-carrefour-ecr-publisher ECR_REGISTRY=123456789012.dkr.ecr.us-east-1.amazonaws.com"

if env $VALID_ENV GITHUB_EVENT_NAME=workflow_dispatch GITHUB_REF=refs/heads/feature-x \
   sh scripts/ci/check-release-prerequisites.sh >/dev/null 2>&1; then
  report_fail "workflow_dispatch fora de refs/heads/main deveria ser rejeitado ANTES de configurar credenciais AWS"
else
  report_pass "workflow_dispatch fora de refs/heads/main e rejeitado no preflight (antes de qualquer chamada AWS)"
fi

if env $VALID_ENV GITHUB_EVENT_NAME=push GITHUB_REF=refs/heads/main \
   sh scripts/ci/check-release-prerequisites.sh >/dev/null 2>&1; then
  report_pass "controle positivo: push em refs/heads/main e aceito no preflight"
else
  report_fail "controle positivo: push em refs/heads/main deveria ser aceito"
fi

if AWS_REGION="us-east-1" ECR_PUBLISHER_ROLE_ARN="not-an-arn" ECR_REGISTRY="123456789012.dkr.ecr.us-east-1.amazonaws.com" \
   sh scripts/ci/check-release-prerequisites.sh >/dev/null 2>&1; then
  report_fail "ARN de role malformado deveria ser rejeitado estaticamente"
else
  report_pass "ARN de role malformado e rejeitado estaticamente (sem chamada a AWS)"
fi

if AWS_REGION="us-east-1" ECR_PUBLISHER_ROLE_ARN="arn:aws:iam::123456789012:role/banco-carrefour-ecr-publisher" ECR_REGISTRY="registry-invalido" \
   sh scripts/ci/check-release-prerequisites.sh >/dev/null 2>&1; then
  report_fail "ECR_REGISTRY malformado deveria ser rejeitado"
else
  report_pass "ECR_REGISTRY malformado e rejeitado"
fi

if AWS_REGION="us-east-1" ECR_PUBLISHER_ROLE_ARN="arn:aws:iam::123456789012:role/banco-carrefour-ecr-publisher" ECR_REGISTRY="123456789012.dkr.ecr.us-east-1.amazonaws.com" \
   sh scripts/ci/check-release-prerequisites.sh >/dev/null 2>&1; then
  report_pass "controle positivo: variaveis validas sao aceitas"
else
  report_fail "controle positivo: variaveis validas deveriam ser aceitas"
fi

# ---------------------------------------------------------------------
# Grupo 2: generate-release-manifest.sh / validate-release-manifest.sh
# contra fixtures sinteticas de images.json + vulnerability summaries
# ---------------------------------------------------------------------
FIXTURE_DIR="$(mktemp -d 2>/dev/null || printf '%s' "${TMPDIR:-/tmp}/release-guard-fixture-$$")"
rm -rf "$FIXTURE_DIR"
mkdir -p "$FIXTURE_DIR/sbom" "$FIXTURE_DIR/vulnerability" "$FIXTURE_DIR/release"

REAL_HEAD="$(git rev-parse HEAD)"

write_fixture() {
  # $1 = verdict a usar para todos os 4 componentes
  # As entradas "image"/"imageId" sao as MESMAS 4 imagens reais ja
  # construidas nesta sessao (artifacts/sbom/images.json) - necessario
  # porque generate-release-manifest.sh agora chama
  # "docker image inspect --format {{json .RootFS.Layers}}" em cada
  # imagem para popular layerDigests; uma imagem local fake/inexistente
  # faria essa chamada falhar. Reaproveitar as imagens reais nao viola o
  # isolamento do teste: sbom/vulnerability/release ficam em um diretorio
  # temporario proprio, nunca em artifacts/ real.
  python3 - "$FIXTURE_DIR" "$REAL_HEAD" "$1" <<'PYEOF'
import json, sys

fixture_dir, real_head, verdict = sys.argv[1:4]

with open("artifacts/sbom/images.json") as f:
    real_images = {e["component"]: e for e in json.load(f)}

# 4 workloads de negocio + migration-runner (artefato operacional) - o
# MESMO conjunto de 5 nomes que build-images-for-supply-chain.sh produz
# de verdade; generate-release-manifest.sh e quem separa
# isso em components/operationalArtifacts, nunca esta fixture.
components = ["ledger-api", "ledger-outbox-publisher", "consolidation-api", "consolidation-worker", "migration-runner"]

images = []
for name in components:
    real_entry = real_images[name]
    sbom_path = f"{fixture_dir}/sbom/{name}.cyclonedx.json"
    vuln_path = f"{fixture_dir}/vulnerability/{name}.trivy.json"
    images.append({
        "component": name,
        "image": real_entry["image"],
        "imageId": real_entry["imageId"],
        "sourceCommit": real_head,
        "sourceTreeClean": True,
        "sbomPath": sbom_path,
        "vulnerabilityReportPath": vuln_path,
        "scannerName": "trivy",
        "scannerVersion": "0.72.0",
        "scannerImageDigest": "sha256:" + "c" * 64,
        "buildTimestamp": "2026-07-27T00:00:00Z",
    })
    with open(sbom_path, "w") as f:
        json.dump({"components": [{"name": "x", "version": "1.0.0"}]}, f)
    with open(vuln_path, "w") as f:
        json.dump({"SchemaVersion": 2, "Results": []}, f)
    with open(f"{fixture_dir}/vulnerability/{name}.summary.json", "w") as f:
        json.dump({"component": name, "verdict": verdict}, f)

with open(f"{fixture_dir}/sbom/images.json", "w") as f:
    json.dump(images, f)

print("fixture gravada, verdict=" + verdict)
PYEOF
}

run_generate() {
  RELEASE_IMAGES_FILE="$FIXTURE_DIR/sbom/images.json" \
  RELEASE_VULN_DIR="$FIXTURE_DIR/vulnerability" \
  RELEASE_OUTPUT_FILE="$FIXTURE_DIR/release/release-manifest.json" \
  sh "$REPO_ROOT/scripts/ci/generate-release-manifest.sh"
}

run_validate() {
  RELEASE_SKIP_LIVE_TREE_CHECK=1 \
  RELEASE_MANIFEST_FILE="$FIXTURE_DIR/release/release-manifest.json" \
  RELEASE_EXPECTED_HEAD="${1:-$REAL_HEAD}" \
  sh "$REPO_ROOT/scripts/ci/validate-release-manifest.sh"
}

# Controle positivo: veredito pass_with_exceptions deve gerar e validar um
# manifesto pending com sucesso.
write_fixture "pass_with_exceptions" >/dev/null
if run_generate >/dev/null 2>&1 && run_validate >/dev/null 2>&1; then
  report_pass "controle positivo: veredito pass_with_exceptions gera e valida um manifesto pending"
else
  report_fail "controle positivo: veredito pass_with_exceptions deveria gerar/validar com sucesso"
fi

# Teste: veredito "fail" bloqueia a geracao do manifesto (nenhum manifesto
# "pending" enganoso e gravado).
rm -f "$FIXTURE_DIR/release/release-manifest.json"
write_fixture "fail" >/dev/null
if run_generate >/dev/null 2>&1; then
  report_fail "veredito 'fail' deveria bloquear a geracao do manifesto de release"
else
  report_pass "veredito 'fail' bloqueia a geracao do manifesto de release"
fi
[ ! -f "$FIXTURE_DIR/release/release-manifest.json" ] \
  && report_pass "nenhum manifesto foi gravado apos bloqueio por veredito 'fail'" \
  || report_fail "um manifesto NAO deveria existir apos bloqueio por veredito 'fail'"

# Teste: veredito "scan_error" bloqueia a geracao do manifesto.
rm -f "$FIXTURE_DIR/release/release-manifest.json"
write_fixture "scan_error" >/dev/null
if run_generate >/dev/null 2>&1; then
  report_fail "veredito 'scan_error' deveria bloquear a geracao do manifesto de release"
else
  report_pass "veredito 'scan_error' bloqueia a geracao do manifesto de release"
fi

# Restaura um manifesto valido para os testes de mutacao do validador.
write_fixture "pass_with_exceptions" >/dev/null
run_generate >/dev/null

# Teste: tag canonica mutavel (fora do formato sha-<40hex>) -> falha.
python3 - "$FIXTURE_DIR/release/release-manifest.json" <<'PYEOF'
import json, sys
p = sys.argv[1]
data = json.load(open(p))
data["components"][0]["canonicalTag"] = "latest"
json.dump(data, open(p, "w"))
PYEOF
if run_validate >/dev/null 2>&1; then
  report_fail "tag canonica 'latest' (mutavel) deveria ser rejeitada"
else
  report_pass "tag canonica mutavel ('latest') e rejeitada"
fi

# Restaura e testa: SBOM ausente -> falha.
run_generate >/dev/null
rm -f "$FIXTURE_DIR/sbom/ledger-api.cyclonedx.json"
if run_validate >/dev/null 2>&1; then
  report_fail "SBOM ausente deveria ser rejeitado"
else
  report_pass "SBOM ausente e rejeitado"
fi
write_fixture "pass_with_exceptions" >/dev/null
run_generate >/dev/null

# Teste: publicationStatus=published sem remoteEcrDigest -> falha.
python3 - "$FIXTURE_DIR/release/release-manifest.json" <<'PYEOF'
import json, sys
p = sys.argv[1]
data = json.load(open(p))
data["components"][0]["publicationStatus"] = "published"
json.dump(data, open(p, "w"))
PYEOF
if run_validate >/dev/null 2>&1; then
  report_fail "publicationStatus=published sem remoteEcrDigest deveria ser rejeitado"
else
  report_pass "publicationStatus=published sem remoteEcrDigest e rejeitado"
fi

# Teste: remoteEcrDigest malformado -> falha (mesmo com published).
python3 - "$FIXTURE_DIR/release/release-manifest.json" <<'PYEOF'
import json, sys
p = sys.argv[1]
data = json.load(open(p))
data["components"][0]["publicationStatus"] = "published"
data["components"][0]["remoteEcrDigest"] = "nao-e-um-digest"
json.dump(data, open(p, "w"))
PYEOF
if run_validate >/dev/null 2>&1; then
  report_fail "remoteEcrDigest malformado deveria ser rejeitado"
else
  report_pass "remoteEcrDigest malformado e rejeitado"
fi

# Teste: componente faltando (so 3 de 4) -> falha.
python3 - "$FIXTURE_DIR/release/release-manifest.json" <<'PYEOF'
import json, sys
p = sys.argv[1]
data = json.load(open(p))
data["components"] = data["components"][:3]
json.dump(data, open(p, "w"))
PYEOF
if run_validate >/dev/null 2>&1; then
  report_fail "manifesto com apenas 3 de 4 componentes deveria ser rejeitado"
else
  report_pass "manifesto com componente faltando e rejeitado"
fi

# Controle positivo: already_published (idempotente) com digest valido e
# overallPublicationStatus="partial" (so 1 de 4 publicado) deve ser aceito.
write_fixture "pass_with_exceptions" >/dev/null
run_generate >/dev/null
python3 - "$FIXTURE_DIR/release/release-manifest.json" <<'PYEOF'
import json, sys
p = sys.argv[1]
data = json.load(open(p))
data["components"][0]["publicationStatus"] = "already_published"
data["components"][0]["remoteEcrDigest"] = "sha256:" + "a" * 64
data["overallPublicationStatus"] = "partial"
json.dump(data, open(p, "w"))
PYEOF
if run_validate >/dev/null 2>&1; then
  report_pass "controle positivo: already_published com digest valido e overallPublicationStatus=partial e aceito"
else
  report_fail "already_published com digest valido e overallPublicationStatus=partial deveria ser aceito"
fi

# Teste: overallPublicationStatus="published" quando so parte dos
# componentes foi publicada -> falha (nunca mentir sobre publicacao total).
python3 - "$FIXTURE_DIR/release/release-manifest.json" <<'PYEOF'
import json, sys
p = sys.argv[1]
data = json.load(open(p))
data["overallPublicationStatus"] = "published"
json.dump(data, open(p, "w"))
PYEOF
if run_validate >/dev/null 2>&1; then
  report_fail "overallPublicationStatus=published com publicacao parcial deveria ser rejeitado"
else
  report_pass "overallPublicationStatus=published com publicacao parcial e rejeitado (nao mente sobre publicacao total)"
fi

# Teste: um componente "conflict" nao pode coexistir com overallPublicationStatus
# de sucesso (published/partial sem refletir a falha).
write_fixture "pass_with_exceptions" >/dev/null
run_generate >/dev/null
python3 - "$FIXTURE_DIR/release/release-manifest.json" <<'PYEOF'
import json, sys
p = sys.argv[1]
data = json.load(open(p))
data["components"][0]["publicationStatus"] = "conflict"
data["components"][0]["remoteEcrDigest"] = "sha256:" + "b" * 64
data["overallPublicationStatus"] = "partial"
json.dump(data, open(p, "w"))
PYEOF
if run_validate >/dev/null 2>&1; then
  report_fail "overallPublicationStatus=partial com um componente em conflict deveria ser rejeitado (esperado overallPublicationStatus=failed)"
else
  report_pass "componente em conflict exige overallPublicationStatus=failed"
fi

# Teste: layerDigests ausente -> falha (necessario para verificacao de
# identidade de conteudo na idempotencia de publicacao).
write_fixture "pass_with_exceptions" >/dev/null
run_generate >/dev/null
python3 - "$FIXTURE_DIR/release/release-manifest.json" <<'PYEOF'
import json, sys
p = sys.argv[1]
data = json.load(open(p))
data["components"][0]["layerDigests"] = None
json.dump(data, open(p, "w"))
PYEOF
if run_validate >/dev/null 2>&1; then
  report_fail "layerDigests ausente deveria ser rejeitado"
else
  report_pass "layerDigests ausente e rejeitado"
fi

# ---------------------------------------------------------------------
# Grupo 3: fronteira components (negocio) vs operationalArtifacts
# ---------------------------------------------------------------------

# Teste: migration-runner dentro de "components" -> rejeitado.
write_fixture "pass_with_exceptions" >/dev/null
run_generate >/dev/null
python3 - "$FIXTURE_DIR/release/release-manifest.json" <<'PYEOF'
import json, sys
p = sys.argv[1]
data = json.load(open(p))
migration_runner = data["operationalArtifacts"].pop()
data["components"].append(migration_runner)
json.dump(data, open(p, "w"))
PYEOF
if run_validate >/dev/null 2>&1; then
  report_fail "migration-runner dentro de 'components' deveria ser rejeitado"
else
  report_pass "migration-runner dentro de 'components' e rejeitado"
fi

# Teste: um workload de negocio dentro de "operationalArtifacts" -> rejeitado.
write_fixture "pass_with_exceptions" >/dev/null
run_generate >/dev/null
python3 - "$FIXTURE_DIR/release/release-manifest.json" <<'PYEOF'
import json, sys
p = sys.argv[1]
data = json.load(open(p))
business = data["components"].pop()
data["operationalArtifacts"].append(business)
json.dump(data, open(p, "w"))
PYEOF
if run_validate >/dev/null 2>&1; then
  report_fail "workload de negocio dentro de 'operationalArtifacts' deveria ser rejeitado"
else
  report_pass "workload de negocio dentro de 'operationalArtifacts' e rejeitado"
fi

# Teste: componente de negocio duplicado (5 entradas, mas so 4 nomes
# unicos) -> rejeitado (nunca aceito so por conjunto de nomes coincidir).
write_fixture "pass_with_exceptions" >/dev/null
run_generate >/dev/null
python3 - "$FIXTURE_DIR/release/release-manifest.json" <<'PYEOF'
import json, sys
p = sys.argv[1]
data = json.load(open(p))
data["components"].append(dict(data["components"][0]))
json.dump(data, open(p, "w"))
PYEOF
if run_validate >/dev/null 2>&1; then
  report_fail "componente de negocio duplicado deveria ser rejeitado"
else
  report_pass "componente de negocio duplicado e rejeitado"
fi

# Teste: operationalArtifacts vazio -> rejeitado (nunca aceito sem
# evidencia real de ao menos migration-runner).
write_fixture "pass_with_exceptions" >/dev/null
run_generate >/dev/null
python3 - "$FIXTURE_DIR/release/release-manifest.json" <<'PYEOF'
import json, sys
p = sys.argv[1]
data = json.load(open(p))
data["operationalArtifacts"] = []
json.dump(data, open(p, "w"))
PYEOF
if run_validate >/dev/null 2>&1; then
  report_fail "operationalArtifacts vazio deveria ser rejeitado"
else
  report_pass "operationalArtifacts vazio e rejeitado"
fi

# Teste: sourceCommit do artefato operacional divergente do restante da
# release -> rejeitado (a mesma release, nunca commits diferentes).
write_fixture "pass_with_exceptions" >/dev/null
run_generate >/dev/null
python3 - "$FIXTURE_DIR/release/release-manifest.json" <<'PYEOF'
import json, sys
p = sys.argv[1]
data = json.load(open(p))
data["operationalArtifacts"][0]["sourceCommit"] = "f" * 40
json.dump(data, open(p, "w"))
PYEOF
if run_validate >/dev/null 2>&1; then
  report_fail "sourceCommit divergente do artefato operacional deveria ser rejeitado"
else
  report_pass "sourceCommit divergente do artefato operacional e rejeitado"
fi

# Teste: digestQualifiedReference malformado com publicationStatus=published
# -> rejeitado.
write_fixture "pass_with_exceptions" >/dev/null
run_generate >/dev/null
python3 - "$FIXTURE_DIR/release/release-manifest.json" <<'PYEOF'
import json, sys
p = sys.argv[1]
data = json.load(open(p))
data["operationalArtifacts"][0]["publicationStatus"] = "published"
data["operationalArtifacts"][0]["remoteEcrDigest"] = "sha256:" + "d" * 64
data["operationalArtifacts"][0]["digestQualifiedReference"] = "nao-e-uma-referencia-qualificada"
json.dump(data, open(p, "w"))
PYEOF
if run_validate >/dev/null 2>&1; then
  report_fail "digestQualifiedReference malformado deveria ser rejeitado"
else
  report_pass "digestQualifiedReference malformado e rejeitado"
fi

# Teste: sbomDigest ausente no artefato operacional -> rejeitado.
write_fixture "pass_with_exceptions" >/dev/null
run_generate >/dev/null
python3 - "$FIXTURE_DIR/release/release-manifest.json" <<'PYEOF'
import json, sys
p = sys.argv[1]
data = json.load(open(p))
data["operationalArtifacts"][0]["sbomDigest"] = None
json.dump(data, open(p, "w"))
PYEOF
if run_validate >/dev/null 2>&1; then
  report_fail "sbomDigest ausente no artefato operacional deveria ser rejeitado"
else
  report_pass "sbomDigest ausente no artefato operacional e rejeitado"
fi

rm -rf "$FIXTURE_DIR"
FIXTURE_DIR=""

echo ""
echo "=== test-release-guards: ${PASS_COUNT} passaram, ${FAIL_COUNT} falharam ==="
[ "$FAIL_COUNT" -eq 0 ]
