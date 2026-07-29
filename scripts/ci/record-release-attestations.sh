#!/bin/sh
# Registra no manifesto de release (artifacts/release/release-manifest.json)
# o resultado REAL das 10 atestacoes (proveniencia + SBOM, uma de cada por
# componente - 4 de negocio + 1 operacional migration-runner) geradas por
# publish-images.yml apos o push real no Amazon ECR - nunca fabrica um
# status "generated" sem um attestation-id real vindo da action oficial
# (actions/attest-build-provenance / actions/attest).
#
# So deve ser chamado depois de:
#   1) scripts/ci/publish-validated-images.sh (publicationStatus=published,
#      remoteEcrDigest real por componente)
#   2) as 10 actions de atestacao (uma por componente x tipo) terem rodado,
#      cada uma com subject-digest = remoteEcrDigest real do componente
#
# Variaveis de ambiente obrigatorias (uma por componente x tipo de
# atestacao, 10 no total - 4 componentes de negocio + 1 artefato
# operacional migration-runner, ) - vazias/ausentes sao
# tratadas como falha real da action correspondente (nunca omitidas
# silenciosamente):
#   LEDGER_API_PROVENANCE_ATTESTATION_ID
#   LEDGER_API_SBOM_ATTESTATION_ID
#   LEDGER_OUTBOX_PUBLISHER_PROVENANCE_ATTESTATION_ID
#   LEDGER_OUTBOX_PUBLISHER_SBOM_ATTESTATION_ID
#   CONSOLIDATION_API_PROVENANCE_ATTESTATION_ID
#   CONSOLIDATION_API_SBOM_ATTESTATION_ID
#   CONSOLIDATION_WORKER_PROVENANCE_ATTESTATION_ID
#   CONSOLIDATION_WORKER_SBOM_ATTESTATION_ID
#   MIGRATION_RUNNER_PROVENANCE_ATTESTATION_ID
#   MIGRATION_RUNNER_SBOM_ATTESTATION_ID
#
# Variaveis de ambiente opcionais (uso exclusivo de teste isolado):
#   RELEASE_MANIFEST_FILE (default: artifacts/release/release-manifest.json)
#
# Uso:
#   sh scripts/ci/record-release-attestations.sh
set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

log() { printf '%s\n' "$1"; }
fail() { echo "record-release-attestations: FALHA: $1" >&2; exit 1; }

MANIFEST_FILE="${RELEASE_MANIFEST_FILE:-artifacts/release/release-manifest.json}"
[ -f "$MANIFEST_FILE" ] || fail "${MANIFEST_FILE} nao encontrado - rode publish-validated-images.sh primeiro."

python3 - "$MANIFEST_FILE" \
  "${LEDGER_API_PROVENANCE_ATTESTATION_ID:-}" "${LEDGER_API_SBOM_ATTESTATION_ID:-}" \
  "${LEDGER_OUTBOX_PUBLISHER_PROVENANCE_ATTESTATION_ID:-}" "${LEDGER_OUTBOX_PUBLISHER_SBOM_ATTESTATION_ID:-}" \
  "${CONSOLIDATION_API_PROVENANCE_ATTESTATION_ID:-}" "${CONSOLIDATION_API_SBOM_ATTESTATION_ID:-}" \
  "${CONSOLIDATION_WORKER_PROVENANCE_ATTESTATION_ID:-}" "${CONSOLIDATION_WORKER_SBOM_ATTESTATION_ID:-}" \
  "${MIGRATION_RUNNER_PROVENANCE_ATTESTATION_ID:-}" "${MIGRATION_RUNNER_SBOM_ATTESTATION_ID:-}" <<'PYEOF'
import json, sys

(manifest_path,
 ledger_api_prov, ledger_api_sbom,
 outbox_prov, outbox_sbom,
 consolidation_api_prov, consolidation_api_sbom,
 consolidation_worker_prov, consolidation_worker_sbom,
 migration_runner_prov, migration_runner_sbom) = sys.argv[1:12]

ids_by_business_component = {
    "ledger-api": (ledger_api_prov, ledger_api_sbom),
    "ledger-outbox-publisher": (outbox_prov, outbox_sbom),
    "consolidation-api": (consolidation_api_prov, consolidation_api_sbom),
    "consolidation-worker": (consolidation_worker_prov, consolidation_worker_sbom),
}
ids_by_operational_artifact = {
    "migration-runner": (migration_runner_prov, migration_runner_sbom),
}

with open(manifest_path) as f:
    manifest = json.load(f)

def apply_attestation_ids(entries, ids_by_name):
    for entry in entries:
        name = entry["component"]
        prov_id, sbom_id = ids_by_name[name]

        if prov_id:
            entry["provenanceAttestation"] = {"status": "generated", "reference": prov_id}
        else:
            entry["provenanceAttestation"] = {"status": "failed", "reference": None}

        if sbom_id:
            entry["sbomAttestation"] = {"status": "generated", "reference": sbom_id}
        else:
            entry["sbomAttestation"] = {"status": "failed", "reference": None}

apply_attestation_ids(manifest["components"], ids_by_business_component)
apply_attestation_ids(manifest["operationalArtifacts"], ids_by_operational_artifact)

all_entries = manifest["components"] + manifest["operationalArtifacts"]
any_attestation_failed = any(
    e.get(kind, {}).get("status") == "failed"
    for e in all_entries
    for kind in ("provenanceAttestation", "sbomAttestation")
)
any_attestation_unresolved = any(
    e.get(kind, {}).get("status") == "pending_hosted_execution"
    for e in all_entries
    for kind in ("provenanceAttestation", "sbomAttestation")
)

if manifest.get("overallPublicationStatus") == "failed" or any_attestation_failed:
    manifest["overallVerdict"] = "failed"
elif manifest.get("overallPublicationStatus") == "published" and not any_attestation_unresolved:
    manifest["overallVerdict"] = "passed"
else:
    manifest["overallVerdict"] = "pending"

with open(manifest_path, "w") as f:
    json.dump(manifest, f, indent=2)

for name, (prov_id, sbom_id) in {**ids_by_business_component, **ids_by_operational_artifact}.items():
    print(f"  {name}: provenanceAttestation={'generated' if prov_id else 'failed'} sbomAttestation={'generated' if sbom_id else 'failed'}")
print(f"overallVerdict={manifest['overallVerdict']}")
PYEOF

log "=== Atestacoes registradas em ${MANIFEST_FILE} (nunca fabricadas - refletem os 8 attestation-id reais das actions) ==="
