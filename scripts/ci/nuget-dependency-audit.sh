#!/bin/sh
# Auditoria de dependencias NuGet - usa exclusivamente a
# ferramenta nativa do .NET 8 SDK (dotnet list package --vulnerable), sem
# instalar nenhum scanner adicional. Restaura com a configuracao real do
# repositorio, nunca modifica .csproj/Directory.Packages.props, nunca
# atualiza pacotes automaticamente.
#
# Uso:
#   sh scripts/ci/nuget-dependency-audit.sh
set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

log() { printf '%s\n' "$1"; }
fail() { echo "nuget-dependency-audit: FALHA: $1" >&2; exit 1; }

mkdir -p artifacts/nuget

log "1) Restaurando (configuracao real do repositorio, sem alterar arquivos)..."
dotnet restore BancoCarrefour.sln >/dev/null

log "2) Enumerando vulnerabilidades diretas e transitivas..."
RAW_JSON=artifacts/nuget/vulnerable-packages.raw.json
dotnet list BancoCarrefour.sln package --vulnerable --include-transitive --format json --output-version 1 > "$RAW_JSON"

[ -s "$RAW_JSON" ] || fail "dotnet list package nao produziu saida."

AUDIT_EXIT=0
python3 - "$RAW_JSON" <<'PYEOF' || AUDIT_EXIT=$?
import json, sys

raw_path = sys.argv[1]
with open(raw_path) as f:
    data = json.load(f)

findings = []
for project in data.get("projects", []):
    project_path = project.get("path", "")
    for framework in project.get("frameworks", []) or []:
        tfm = framework.get("framework", "")
        for kind, packages in (
            ("direct", framework.get("topLevelPackages", []) or []),
            ("transitive", framework.get("transitivePackages", []) or []),
        ):
            for pkg in packages:
                for vuln in pkg.get("vulnerabilities", []) or []:
                    findings.append({
                        "project": project_path,
                        "framework": tfm,
                        "package": pkg.get("id"),
                        "resolvedVersion": pkg.get("resolvedVersion"),
                        "dependencyType": kind,
                        "severity": vuln.get("severity"),
                        "advisoryUrl": vuln.get("advisoryurl"),
                        "advisorySource": "GitHub Advisory Database (via NuGet audit source)",
                    })

critical_or_high = [f for f in findings if f["severity"] in ("Critical", "High")]
verdict = "pass" if not critical_or_high else "fail"

report = {
    "tool": "dotnet list package --vulnerable --include-transitive",
    "dotnetSdkVersion": None,
    "totalFindings": len(findings),
    "criticalOrHighCount": len(critical_or_high),
    "verdict": verdict,
    "findings": findings,
}

with open("artifacts/nuget/vulnerable-packages.json", "w") as f:
    json.dump(report, f, indent=2)

lines = [
    "## Auditoria de dependencias NuGet",
    "",
    f"- Total de achados: {len(findings)}",
    f"- Critical/High: {len(critical_or_high)}",
    f"- Veredito: **{verdict}**",
    "",
]
if findings:
    lines.append("| Projeto | Pacote | Versao | Tipo | Severidade | Advisory |")
    lines.append("|---|---|---|---|---|---|")
    for f in findings:
        project_name = f["project"].rsplit("/", 1)[-1].rsplit("\\", 1)[-1]
        lines.append(f"| {project_name} | {f['package']} | {f['resolvedVersion']} | {f['dependencyType']} | {f['severity']} | {f['advisoryUrl']} |")
else:
    lines.append("Nenhuma vulnerabilidade conhecida encontrada nas fontes NuGet configuradas.")

with open("artifacts/nuget/vulnerable-packages.md", "w") as f:
    f.write("\n".join(lines) + "\n")

print(f"Total: {len(findings)} | Critical/High: {len(critical_or_high)} | Veredito: {verdict}")
sys.exit(0 if verdict == "pass" else 2)
PYEOF

DOTNET_VERSION=$(dotnet --version)
python3 -c "
import json
with open('artifacts/nuget/vulnerable-packages.json') as f:
    d = json.load(f)
d['dotnetSdkVersion'] = '$DOTNET_VERSION'
with open('artifacts/nuget/vulnerable-packages.json', 'w') as f:
    json.dump(d, f, indent=2)
"

log ""
if [ "$AUDIT_EXIT" -ne 0 ]; then
  fail "vulnerabilidades Critical/High encontradas em dependencias NuGet - ver artifacts/nuget/vulnerable-packages.md."
fi
log "=== Auditoria NuGet concluida sem achados Critical/High bloqueantes ==="
