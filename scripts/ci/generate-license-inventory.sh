#!/bin/sh
# Gera o inventario de licencas a partir dos SBOMs
# CycloneDX ja gerados por generate-sboms.sh - nao introduz uma ferramenta
# de licenca separada. Nao aplica nenhuma politica bloqueante: apenas
# reporta, ver docs/security/dependencias-e-supply-chain.md secao de
# licencas para a decisao de nao bloquear sem revisar o inventario real
# primeiro.
#
# Uso:
#   sh scripts/ci/generate-license-inventory.sh
set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

log() { printf '%s\n' "$1"; }
fail() { echo "generate-license-inventory: FALHA: $1" >&2; exit 1; }

[ -f artifacts/sbom/images.json ] || fail "artifacts/sbom/images.json nao encontrado - rode generate-sboms.sh primeiro."

python3 <<'PYEOF'
import json, glob

rows = []
unknown_count = 0
known_count = 0
by_license = {}

for sbom_path in sorted(glob.glob("artifacts/sbom/*.cyclonedx.json")):
    component_name = sbom_path.split("/")[-1].replace(".cyclonedx.json", "")
    with open(sbom_path) as f:
        sbom = json.load(f)

    for comp in sbom.get("components", []):
        name = comp.get("name", "")
        version = comp.get("version", "")
        licenses = comp.get("licenses", [])
        license_ids = []
        for lic in licenses:
            if "license" in lic:
                license_ids.append(lic["license"].get("id") or lic["license"].get("name") or "NOASSERTION")
            elif "expression" in lic:
                license_ids.append(lic["expression"])
        if not license_ids:
            license_ids = ["NOASSERTION"]

        for lic_id in license_ids:
            rows.append({
                "image": component_name,
                "package": name,
                "version": version,
                "license": lic_id,
            })
            by_license[lic_id] = by_license.get(lic_id, 0) + 1
            if lic_id == "NOASSERTION":
                unknown_count += 1
            else:
                known_count += 1

inventory = {
    "totalPackageLicensePairs": len(rows),
    "knownLicenseCount": known_count,
    "unknownLicenseCount": unknown_count,
    "byLicense": dict(sorted(by_license.items(), key=lambda x: -x[1])),
    "rows": rows,
}

with open("artifacts/sbom/license-inventory.json", "w") as f:
    json.dump(inventory, f, indent=2)

lines = [
    "## Inventario de licencas (a partir dos SBOMs)",
    "",
    f"- Pares pacote/licenca: {len(rows)}",
    f"- Com licenca identificada: {known_count}",
    f"- NOASSERTION (nao identificada pelo scanner): {unknown_count}",
    "",
    "Nenhuma politica de bloqueio por licenca esta ativa neste bloco - ver docs/security/dependencias-e-supply-chain.md.",
    "",
    "| Licenca | Quantidade |",
    "|---|---:|",
]
for lic, count in sorted(by_license.items(), key=lambda x: -x[1]):
    lines.append(f"| {lic} | {count} |")

with open("artifacts/sbom/license-inventory.md", "w") as f:
    f.write("\n".join(lines) + "\n")

print(f"Pares pacote/licenca: {len(rows)} | conhecidas: {known_count} | NOASSERTION: {unknown_count}")
PYEOF

log "=== Inventario de licencas escrito em artifacts/sbom/license-inventory.{json,md} ==="
