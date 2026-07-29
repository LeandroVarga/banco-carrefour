#!/bin/sh
# Gera artifacts/release/release-manifest.json a partir da MESMA evidencia
# de supply chain ja validada (artifacts/sbom/images.json +
# artifacts/vulnerability/<componente>.summary.json) - nunca reconstroi
# nem re-escaneia nada.
#
# Bloqueia (exit != 0, sem gravar um manifesto "pending" enganoso) se
# qualquer componente tiver veredito de vulnerabilidade "fail" ou
# "scan_error" - a publicacao nunca deve prosseguir para o login/push do
# ECR nesse caso (ver docs/decisions/ADR-0013).
#
# schemaVersion "4.0.0" (schemas/release-manifest.schema.json) - unica
# referencia formal do manifesto de release, alvo Amazon ECR/AWS real
# (ver ADR-0013). A partir da versao 4.0.0, os 4
# workloads de negocio (components) sao explicitamente separados de
# artefatos operacionais (operationalArtifacts, hoje: migration-runner) -
# nunca uma colecao unica e indiferenciada de "5 componentes".
#
# O manifesto gerado aqui SEMPRE comeca com publicationStatus="pending" e
# remoteEcrDigest=null - nenhum digest remoto e inventado. Um push real
# (fora do escopo deste bloco) atualizaria o manifesto para
# publicationStatus="published" com o digest real retornado pelo ECR.
#
# provenanceAttestation/sbomAttestation: sempre "pending_hosted_execution"
# e reference=null a partir deste script (a atestacao real so ocorre em
# publish-images.yml, apos o push real, associada ao digest ECR real) -
# nenhum digest e inventado; ver build_attestation() abaixo, que so aceita
# um status "generated"/"verified" vindo de RELEASE_ATTESTATIONS_FILE
# quando esse arquivo tiver sido escrito por uma execucao hospedada real
# (publish-images.yml), nunca fabricado por este gerador.
#
# Uso:
#   sh scripts/ci/generate-release-manifest.sh
#
# Variaveis de ambiente opcionais (nao sensiveis - apenas identificadores):
#   ECR_REGISTRY   ex.: 123456789012.dkr.ecr.us-east-1.amazonaws.com
#                  Se ausente, ecrRepositoryUri/awsAccountId/awsRegion
#                  ficam null (registry ainda nao definido/conhecido nesta
#                  sessao). Se presente, DEVE seguir exatamente esse
#                  formato - awsAccountId/awsRegion sao SEMPRE derivados
#                  dele, nunca lidos de uma variavel separada (nao ha
#                  fonte de verdade independente para esses dois campos).
#   BUILD_PLATFORM (default: linux/amd64)
#
# Variaveis de ambiente exclusivas de teste isolado (usadas por
# scripts/ci/test-release-guards.sh para gerar um manifesto a partir de
# fixtures sinteticas, sem tocar em artifacts/ real):
#   RELEASE_IMAGES_FILE        (default: artifacts/sbom/images.json)
#   RELEASE_VULN_DIR           (default: artifacts/vulnerability)
#   RELEASE_OUTPUT_FILE        (default: artifacts/release/release-manifest.json)
#   RELEASE_ATTESTATIONS_FILE  (default: artifacts/release/attestations.json)
set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

log() { printf '%s\n' "$1"; }
fail() { echo "generate-release-manifest: FALHA: $1" >&2; exit 1; }

export RELEASE_IMAGES_FILE="${RELEASE_IMAGES_FILE:-artifacts/sbom/images.json}"
export RELEASE_VULN_DIR="${RELEASE_VULN_DIR:-artifacts/vulnerability}"
export RELEASE_OUTPUT_FILE="${RELEASE_OUTPUT_FILE:-artifacts/release/release-manifest.json}"
export RELEASE_ATTESTATIONS_FILE="${RELEASE_ATTESTATIONS_FILE:-artifacts/release/attestations.json}"

[ -f "$RELEASE_IMAGES_FILE" ] || fail "${RELEASE_IMAGES_FILE} nao encontrado - rode build-images-for-supply-chain.sh e scan-images.sh primeiro."

mkdir -p "$(dirname "$RELEASE_OUTPUT_FILE")"

export RELEASE_ECR_REGISTRY="${ECR_REGISTRY:-}"
export RELEASE_BUILD_PLATFORM="${BUILD_PLATFORM:-linux/amd64}"
export RELEASE_WORKFLOW_RUN_ID="${GITHUB_RUN_ID:-}"
export RELEASE_WORKFLOW_RUN_ATTEMPT="${GITHUB_RUN_ATTEMPT:-}"
export RELEASE_SOURCE_REPOSITORY="${GITHUB_REPOSITORY:-}"
export RELEASE_SOURCE_REF="${GITHUB_REF:-}"
export RELEASE_SERVER_URL="${GITHUB_SERVER_URL:-}"

GEN_EXIT=0
python3 <<'PYEOF' || GEN_EXIT=$?
import hashlib, json, os, re, subprocess, sys
from datetime import datetime, timezone

images_file = os.environ["RELEASE_IMAGES_FILE"]
vuln_dir = os.environ["RELEASE_VULN_DIR"]
output_file = os.environ["RELEASE_OUTPUT_FILE"]
attestations_file = os.environ["RELEASE_ATTESTATIONS_FILE"]

with open(images_file) as f:
    images = json.load(f)

# Fronteira explícita entre workloads de negócio (components) e artefatos
# operacionais (operationalArtifacts) - nunca uma coleção única e
# indiferenciada de "N imagens". Um nome fora dos dois conjuntos conhecidos
# é sempre um erro bloqueante, nunca ignorado silenciosamente.
BUSINESS_COMPONENT_NAMES = {"ledger-api", "ledger-outbox-publisher", "consolidation-api", "consolidation-worker"}
OPERATIONAL_ARTIFACT_NAMES = {"migration-runner"}

RUNTIME_PURPOSE_BY_ARTIFACT = {
    "migration-runner": (
        "Task ECS/Fargate one-off (nunca aws_ecs_service) que aplica migrations EF Core "
        "fase EXPAND via advisory lock de sessao do PostgreSQL, executada por "
        "'aws ecs run-task' com --boundary Ledger|Consolidation - nunca um servico de "
        "longa duracao, nunca um 5o workload de negocio no modelo de dominio arquitetural."
    ),
}

business_entries = [e for e in images if e.get("component") in BUSINESS_COMPONENT_NAMES]
operational_entries = [e for e in images if e.get("component") in OPERATIONAL_ARTIFACT_NAMES]
unrecognized_entries = [e for e in images if e.get("component") not in BUSINESS_COMPONENT_NAMES | OPERATIONAL_ARTIFACT_NAMES]

if unrecognized_entries:
    names = sorted({e.get("component") for e in unrecognized_entries})
    print(f"generate-release-manifest: images.json contem nome(s) nao reconhecido(s) (nem workload de negocio, nem artefato operacional): {names}", file=sys.stderr)
    sys.exit(1)

business_names_found = [e["component"] for e in business_entries]
if len(business_names_found) != len(BUSINESS_COMPONENT_NAMES) or set(business_names_found) != BUSINESS_COMPONENT_NAMES:
    print(f"generate-release-manifest: images.json deveria ter exatamente os 4 workloads de negocio {sorted(BUSINESS_COMPONENT_NAMES)} (sem duplicatas), encontrados: {sorted(business_names_found)}", file=sys.stderr)
    sys.exit(1)

operational_names_found = [e["component"] for e in operational_entries]
if len(operational_names_found) != len(set(operational_names_found)):
    print(f"generate-release-manifest: operationalArtifacts contem nome(s) duplicado(s): {sorted(operational_names_found)}", file=sys.stderr)
    sys.exit(1)
if not operational_entries:
    print("generate-release-manifest: nenhum artefato operacional encontrado em images.json (esperado ao menos migration-runner) - nunca gerado sem evidencia real.", file=sys.stderr)
    sys.exit(1)

ecr_registry = os.environ.get("RELEASE_ECR_REGISTRY") or None
workflow_run_id = os.environ.get("RELEASE_WORKFLOW_RUN_ID") or None
workflow_run_attempt = os.environ.get("RELEASE_WORKFLOW_RUN_ATTEMPT") or None
source_repository = os.environ.get("RELEASE_SOURCE_REPOSITORY") or None
source_ref = os.environ.get("RELEASE_SOURCE_REF") or None
server_url = os.environ.get("RELEASE_SERVER_URL") or None
build_platform = os.environ["RELEASE_BUILD_PLATFORM"]

SHA_RE = re.compile(r"^[0-9a-f]{40}$")
ECR_REGISTRY_RE = re.compile(r"^([0-9]{12})\.dkr\.ecr\.([a-z0-9-]+)\.amazonaws\.com$")

blocking = []

aws_account_id = None
aws_region = None
if ecr_registry is not None:
    m = ECR_REGISTRY_RE.match(ecr_registry)
    if not m:
        blocking.append(f"ECR_REGISTRY malformado (esperado '<conta-12-digitos>.dkr.ecr.<regiao>.amazonaws.com'): {ecr_registry!r}")
    else:
        aws_account_id, aws_region = m.group(1), m.group(2)

# Estados de atestacao: nunca fabricados por este gerador - so refletem o
# que uma execucao hospedada real (publish-images.yml) ja escreveu em
# RELEASE_ATTESTATIONS_FILE, associado ao digest ECR real. Ausencia do
# arquivo (o caso comum antes da publicacao) => pending_hosted_execution
# para todos os componentes, nunca "generated" por omissao.
VALID_ATTESTATION_STATUSES = {"not_applicable", "pending_hosted_execution", "generated", "failed", "verified"}
PLACEHOLDER_VALUES = {"", "null", "none", "n/a", "todo", "tbd", "pending"}

attestations_by_component = {}
if os.path.isfile(attestations_file):
    with open(attestations_file) as f:
        attestations_by_component = json.load(f)

def build_attestation(name, kind, errors):
    entry = (attestations_by_component.get(name) or {}).get(kind) or {}
    status = entry.get("status", "pending_hosted_execution")
    reference = entry.get("reference")

    if status not in VALID_ATTESTATION_STATUSES:
        errors.append(f"{name}: status de {kind}Attestation desconhecido: {status!r}")
        status = "pending_hosted_execution"
        reference = None

    if status in ("generated", "verified"):
        if not reference or str(reference).strip().lower() in PLACEHOLDER_VALUES:
            errors.append(f"{name}: {kind}Attestation status={status} exige uma 'reference' real (nao vazia/placeholder) - nunca fabricada.")
    else:
        if reference is not None:
            errors.append(f"{name}: {kind}Attestation status={status} nao deveria ter 'reference' preenchida ({reference!r}).")
            reference = None

    return {"status": status, "reference": reference}

components = []
operational_artifacts = []
source_commits = set()

def build_shared_fields(entry, name, blocking):
    """Campos comuns a components e operationalArtifacts - build once,
    nunca duplicado nem divergente entre as duas colecoes."""
    source_commit = entry.get("sourceCommit", "")
    source_commits.add(source_commit)
    summary_path = f"{vuln_dir}/{name}.summary.json"

    try:
        with open(summary_path) as f:
            summary = json.load(f)
    except FileNotFoundError:
        print(f"generate-release-manifest: relatorio de vulnerabilidade ausente para {name} ({summary_path}).", file=sys.stderr)
        sys.exit(1)

    verdict = summary.get("verdict")
    if verdict in ("fail", "scan_error"):
        blocking.append(f"{name}: veredito de vulnerabilidade '{verdict}' - publicacao bloqueada.")

    if not SHA_RE.match(source_commit or ""):
        blocking.append(f"{name}: sourceCommit nao e uma SHA completa de 40 hex.")

    if entry.get("sourceTreeClean") is not True:
        blocking.append(f"{name}: sourceTreeClean != true.")

    canonical_tag = f"sha-{source_commit}"
    repository_name = f"banco-carrefour/{name}"
    ecr_repository_uri = f"{ecr_registry}/{repository_name}" if ecr_registry else None

    # layerDigests: identidade de CONTEUDO estavel e portavel entre image
    # stores (ao contrario de localImageId, que em image stores baseados em
    # containerd pode coincidir com o digest do manifesto do registry, mas
    # isso e uma propriedade observada deste ambiente, nao uma garantia
    # geral do modelo de dados OCI - ver scripts/ci/test-generic-oci-publish-proof.sh).
    # Usada por publish-validated-images.sh para decidir idempotencia sem
    # depender de comparar strings de imageId com o digest remoto do ECR.
    layer_digests = None
    oci_revision = None
    try:
        raw = subprocess.check_output(
            ["docker", "image", "inspect", entry["image"], "--format", "{{json .RootFS.Layers}}"],
            text=True,
        )
        layer_digests = json.loads(raw)
    except Exception as e:
        blocking.append(f"{name}: nao foi possivel obter RootFS.Layers da imagem local ({e}).")

    try:
        oci_revision = subprocess.check_output(
            ["docker", "image", "inspect", entry["image"], "--format",
             '{{index .Config.Labels "org.opencontainers.image.revision"}}'],
            text=True,
        ).strip()
    except Exception as e:
        blocking.append(f"{name}: nao foi possivel ler o label org.opencontainers.image.revision da imagem local ({e}).")

    if oci_revision is not None and oci_revision != source_commit:
        blocking.append(f"{name}: org.opencontainers.image.revision ({oci_revision!r}) difere de sourceCommit ({source_commit!r}) - imagem nao corresponde ao commit declarado.")

    return {
        "component": name,
        "sourceCommit": source_commit,
        "sourceTreeClean": entry.get("sourceTreeClean"),
        "localImageId": entry.get("imageId"),
        "layerDigests": layer_digests,
        "ociRevision": oci_revision,
        "canonicalTag": canonical_tag,
        "ecrRepositoryUri": ecr_repository_uri,
        "remoteEcrDigest": None,
        "sbomPath": entry.get("sbomPath"),
        "vulnerabilityReportPath": entry.get("vulnerabilityReportPath"),
        "vulnerabilityVerdict": verdict,
        "activeExceptionsApplied": summary.get("activeExceptionsApplied", []),
        "publicationStatus": "pending",
        "provenanceAttestation": build_attestation(name, "provenance", blocking),
        "sbomAttestation": build_attestation(name, "sbom", blocking),
    }

for entry in business_entries:
    name = entry["component"]
    components.append(build_shared_fields(entry, name, blocking))

for entry in operational_entries:
    name = entry["component"]
    fields = build_shared_fields(entry, name, blocking)

    sbom_path = fields["sbomPath"]
    sbom_digest = None
    if sbom_path:
        try:
            with open(sbom_path, "rb") as f:
                sbom_digest = "sha256:" + hashlib.sha256(f.read()).hexdigest()
        except FileNotFoundError:
            blocking.append(f"{name}: sbomPath ({sbom_path}) nao encontrado - nao foi possivel calcular sbomDigest.")

    fields["artifactType"] = name
    fields["runtimePurpose"] = RUNTIME_PURPOSE_BY_ARTIFACT.get(name, "")
    fields["sbomDigest"] = sbom_digest
    # digestQualifiedReference: null ate a publicacao real (remoteEcrDigest
    # ainda nao existe neste ponto) - preenchido por publish-validated-images.sh,
    # nunca fabricado aqui.
    fields["digestQualifiedReference"] = None
    operational_artifacts.append(fields)

if blocking:
    print("generate-release-manifest: publicacao bloqueada:", file=sys.stderr)
    for b in blocking:
        print(f"  - {b}", file=sys.stderr)
    sys.exit(1)

if len(source_commits) != 1:
    print(f"generate-release-manifest: todos os {len(components) + len(operational_artifacts)} componentes/artefatos deveriam compartilhar o mesmo sourceCommit, encontrados: {sorted(source_commits)}", file=sys.stderr)
    sys.exit(1)

source_commit = next(iter(source_commits))
run_url = None
if server_url and source_repository and workflow_run_id:
    run_url = f"{server_url}/{source_repository}/actions/runs/{workflow_run_id}"

# overallPublicationStatus: nunca "published" quando so parte dos
# componentes/artefatos foi publicada - ver scripts/ci/validate-release-manifest.sh
# para a validacao correspondente. overallVerdict: agregado distinto, so
# "passed" quando a publicacao real terminou E nenhuma atestacao falhou.
manifest = {
    "schemaVersion": "4.0.0",
    "releaseId": f"sha-{source_commit}",
    "sourceRepository": source_repository,
    "sourceCommit": source_commit,
    "sourceRef": source_ref,
    "workflow": {
        "name": "Publish Images",
        "runId": workflow_run_id,
        "runUrl": run_url,
    },
    "createdAt": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    "registryType": "ecr",
    "awsAccountId": aws_account_id,
    "awsRegion": aws_region,
    "buildPlatform": build_platform,
    "components": components,
    "operationalArtifacts": operational_artifacts,
    "overallPublicationStatus": "pending",
    "overallVerdict": "pending",
}
with open(output_file, "w") as f:
    json.dump(manifest, f, indent=2)

for c in components:
    print(f"  [component] {c['component']}: tag={c['canonicalTag']} verdict={c['vulnerabilityVerdict']} status={c['publicationStatus']}")
for a in operational_artifacts:
    print(f"  [operational] {a['component']}: tag={a['canonicalTag']} verdict={a['vulnerabilityVerdict']} status={a['publicationStatus']}")
PYEOF

if [ "$GEN_EXIT" -ne 0 ]; then
  fail "geracao do manifesto de release bloqueada - ver mensagens acima. Nenhum manifesto foi gravado."
fi

log ""
log "=== Manifesto de release gerado em ${RELEASE_OUTPUT_FILE} (publicationStatus=pending em todos os componentes) ==="
