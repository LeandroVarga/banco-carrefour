#!/bin/sh
# Testes negativos (e um positivo de controle) para os dois guardas de
# provenance criados na auditoria:
#   - scripts/ci/require-clean-source-tree.sh (arvore suja bloqueia build)
#   - scripts/ci/validate-supply-chain-artifacts.sh (manifesto/evidencia
#     tem que corresponder ao HEAD atual, com SHA completa)
#
# Nunca modifica o repositorio real de forma permanente: os testes de
# arvore suja rodam contra um "git worktree" TEMPORARIO e descartavel
# (mesmo historico, diretorio de trabalho proprio), e os testes de
# manifesto/evidencia rodam contra copias sinteticas em um diretorio
# temporario fora do repositorio, nunca contra artifacts/ real.
#
# Uso:
#   sh scripts/ci/test-provenance-guards.sh
set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

PASS_COUNT=0
FAIL_COUNT=0

report_pass() { PASS_COUNT=$((PASS_COUNT + 1)); printf 'PASS: %s\n' "$1"; }
report_fail() { FAIL_COUNT=$((FAIL_COUNT + 1)); printf 'FAIL: %s\n' "$1" >&2; }

WORKTREE_DIR=""
FIXTURE_DIR=""

cleanup() {
  if [ -n "$WORKTREE_DIR" ] && [ -d "$WORKTREE_DIR" ]; then
    git worktree remove --force "$WORKTREE_DIR" >/dev/null 2>&1 || rm -rf "$WORKTREE_DIR"
  fi
  if [ -n "$FIXTURE_DIR" ] && [ -d "$FIXTURE_DIR" ]; then
    rm -rf "$FIXTURE_DIR"
  fi
}
trap cleanup EXIT INT TERM

# ---------------------------------------------------------------------
# Grupo 1: require-clean-source-tree.sh contra um worktree temporario
# ---------------------------------------------------------------------
WORKTREE_DIR="$(mktemp -d 2>/dev/null || printf '%s' "${TMPDIR:-/tmp}/provenance-guard-worktree-$$")"
rm -rf "$WORKTREE_DIR"
git worktree add --detach --quiet "$WORKTREE_DIR" HEAD

# Controle positivo: worktree recem-criado (limpo) deve passar.
if sh "$REPO_ROOT/scripts/ci/require-clean-source-tree.sh" "$WORKTREE_DIR" >/dev/null 2>&1; then
  report_pass "controle positivo: worktree limpo e aceito"
else
  report_fail "controle positivo: worktree limpo deveria ser aceito"
fi

# Teste: arquivo rastreado modificado (unstaged) deve falhar.
SAMPLE_TRACKED_FILE="$WORKTREE_DIR/README.md"
printf '\n<!-- mutacao temporaria do teste de provenance -->\n' >> "$SAMPLE_TRACKED_FILE"
if sh "$REPO_ROOT/scripts/ci/require-clean-source-tree.sh" "$WORKTREE_DIR" >/dev/null 2>&1; then
  report_fail "arquivo rastreado modificado (unstaged) deveria ser rejeitado"
else
  report_pass "arquivo rastreado modificado (unstaged) e rejeitado"
fi
git -C "$WORKTREE_DIR" checkout -- README.md

# Teste: arquivo staged deve falhar.
printf '\n<!-- mutacao staged do teste de provenance -->\n' >> "$SAMPLE_TRACKED_FILE"
git -C "$WORKTREE_DIR" add README.md
if sh "$REPO_ROOT/scripts/ci/require-clean-source-tree.sh" "$WORKTREE_DIR" >/dev/null 2>&1; then
  report_fail "arquivo staged deveria ser rejeitado"
else
  report_pass "arquivo staged e rejeitado"
fi
git -C "$WORKTREE_DIR" reset --hard --quiet HEAD

# Teste: arquivo NOVO nao rastreado (fora do .gitignore) deve falhar.
echo "// arquivo de teste temporario" > "$WORKTREE_DIR/scripts/ci/.provenance-test-scratch.cs.tmp"
if sh "$REPO_ROOT/scripts/ci/require-clean-source-tree.sh" "$WORKTREE_DIR" >/dev/null 2>&1; then
  report_fail "arquivo novo nao rastreado deveria ser rejeitado"
else
  report_pass "arquivo novo nao rastreado e rejeitado"
fi
rm -f "$WORKTREE_DIR/scripts/ci/.provenance-test-scratch.cs.tmp"

# Controle positivo: evidencia gerada e IGNORADA (artifacts/) nao deve
# bloquear o build, mesmo sendo um arquivo novo nao rastreado.
mkdir -p "$WORKTREE_DIR/artifacts/sbom"
echo '{}' > "$WORKTREE_DIR/artifacts/sbom/scratch-evidence.json"
if sh "$REPO_ROOT/scripts/ci/require-clean-source-tree.sh" "$WORKTREE_DIR" >/dev/null 2>&1; then
  report_pass "controle positivo: evidencia ignorada em artifacts/ nao bloqueia o build"
else
  report_fail "controle positivo: evidencia ignorada em artifacts/ NAO deveria bloquear o build"
fi
rm -rf "$WORKTREE_DIR/artifacts"

git worktree remove --force "$WORKTREE_DIR" >/dev/null 2>&1 || rm -rf "$WORKTREE_DIR"
WORKTREE_DIR=""

# ---------------------------------------------------------------------
# Grupo 2: validate-supply-chain-artifacts.sh contra fixtures sinteticas
# ---------------------------------------------------------------------
FIXTURE_DIR="$(mktemp -d 2>/dev/null || printf '%s' "${TMPDIR:-/tmp}/provenance-guard-fixture-$$")"
rm -rf "$FIXTURE_DIR"
mkdir -p "$FIXTURE_DIR/sbom" "$FIXTURE_DIR/vulnerability" "$FIXTURE_DIR/nuget" "$FIXTURE_DIR/security"

REAL_HEAD="$(git rev-parse HEAD)"
FAKE_HEAD="0000000000000000000000000000000000000000"

python3 - "$FIXTURE_DIR" "$REAL_HEAD" <<'PYEOF'
import json, sys, os

fixture_dir, real_head = sys.argv[1:3]
components = ["ledger-api", "ledger-outbox-publisher", "consolidation-api", "consolidation-worker", "migration-runner"]
digest = "sha256:cffe3f5161a47a6823fbd23d985795b3ed72a4c806da4c4df16266c02accdd6f"

images = []
for name in components:
    image_id = f"sha256:{name.replace('-', '')}{'0' * (64 - len(name.replace('-', '')))}"
    images.append({
        "component": name,
        "dockerfile": f"src/x/{name}/Dockerfile",
        "image": f"banco-carrefour-{name}:{real_head}",
        "imageId": image_id,
        "baseImage": "mcr.microsoft.com/dotnet/aspnet:8.0",
        "sourceCommit": real_head,
        "sourceTreeClean": True,
        "buildContext": ".",
        "targetPlatform": "linux/amd64",
        "buildTimestamp": "2026-07-27T00:00:00Z",
        "buildCommand": "docker build ...",
        "sbomPath": f"{fixture_dir}/sbom/{name}.cyclonedx.json",
        "vulnerabilityReportPath": f"{fixture_dir}/vulnerability/{name}.trivy.json",
        "scannerName": "trivy",
        "scannerVersion": "0.72.0",
        "scannerImageDigest": digest,
        "scannerDatabaseTimestamp": "2026-07-26T19:02:29Z",
        "published": False,
    })
    sbom = {
        "bomFormat": "CycloneDX",
        "specVersion": "1.5",
        "metadata": {"component": {"name": f"banco-carrefour-{name}", "type": "container"}},
        "components": [{"name": "example-pkg", "version": "1.0.0", "type": "library"}],
    }
    with open(f"{fixture_dir}/sbom/{name}.cyclonedx.json", "w") as f:
        json.dump(sbom, f)
    with open(f"{fixture_dir}/vulnerability/{name}.trivy.json", "w") as f:
        json.dump({"SchemaVersion": 2, "Results": []}, f)
    summary = {
        "component": name,
        "image": images[-1]["image"],
        "imageId": image_id,
        "sourceCommit": real_head,
        "sourceTreeClean": True,
        "scannerName": "trivy",
        "scannerVersion": "0.72.0",
        "scannerImageDigest": digest,
        "scannerDatabaseTimestamp": "2026-07-26T19:02:29Z",
        "reportSchemaVersion": 2,
        "totalFindings": 0,
        "criticalWithFixCount": 0, "criticalNoFixCount": 0,
        "highWithFixCount": 0, "highNoFixCount": 0,
        "activeExceptionsApplied": [], "blockingReasons": [],
        "verdict": "pass",
        "findings": [],
    }
    with open(f"{fixture_dir}/vulnerability/{name}.summary.json", "w") as f:
        json.dump(summary, f)

with open(f"{fixture_dir}/sbom/images.json", "w") as f:
    json.dump(images, f)
with open(f"{fixture_dir}/sbom/license-inventory.json", "w") as f:
    json.dump({"totalPackageLicensePairs": 10, "withLicense": 8, "noassertion": 2}, f)
with open(f"{fixture_dir}/nuget/vulnerable-packages.json", "w") as f:
    json.dump({"verdict": "pass", "dotnetSdkVersion": "8.0.423", "findings": []}, f)
with open(f"{fixture_dir}/security/excecoes-de-vulnerabilidade.json", "w") as f:
    json.dump([], f)

print("fixture baseline gravada em", fixture_dir)
PYEOF

run_validator() {
  SUPPLY_CHAIN_SKIP_LIVE_TREE_CHECK=1 \
  SUPPLY_CHAIN_IMAGES_JSON="$FIXTURE_DIR/sbom/images.json" \
  SUPPLY_CHAIN_SBOM_DIR="$FIXTURE_DIR/sbom" \
  SUPPLY_CHAIN_VULN_DIR="$FIXTURE_DIR/vulnerability" \
  SUPPLY_CHAIN_NUGET_FILE="$FIXTURE_DIR/nuget/vulnerable-packages.json" \
  SUPPLY_CHAIN_LICENSE_FILE="$FIXTURE_DIR/sbom/license-inventory.json" \
  SUPPLY_CHAIN_EXCEPTIONS_FILE="$FIXTURE_DIR/security/excecoes-de-vulnerabilidade.json" \
  SUPPLY_CHAIN_EXPECTED_HEAD="${1:-$REAL_HEAD}" \
  sh "$REPO_ROOT/scripts/ci/validate-supply-chain-artifacts.sh"
}

# Controle positivo: fixture baseline (nao mutada) deve passar.
if run_validator "$REAL_HEAD" >/dev/null 2>&1; then
  report_pass "controle positivo: fixture baseline (HEAD real, SHA completa) e aceita"
else
  report_fail "controle positivo: fixture baseline deveria ser aceita"
fi

# Teste: HEAD esperado diferente do sourceCommit gravado -> falha.
if run_validator "$FAKE_HEAD" >/dev/null 2>&1; then
  report_fail "sourceCommit divergente do HEAD esperado deveria ser rejeitado"
else
  report_pass "sourceCommit divergente do HEAD esperado e rejeitado"
fi

# Teste: SHA abreviada (12 chars) no manifesto -> falha.
# (paths passados via argv, nunca interpolados dentro do script Python -
# um caminho estilo "/tmp/..." do MSYS/Git Bash interpolado dentro de uma
# string Python e mal interpretado pelo python3.exe nativo do Windows,
# que so recebe traducao de caminho automatica quando o path e um
# argumento de linha de comando isolado, nao quando esta embutido no meio
# de um script - por isso todo caminho aqui vem de sys.argv.)
cp "$FIXTURE_DIR/sbom/images.json" "$FIXTURE_DIR/sbom/images.json.bak"
python3 - "$FIXTURE_DIR/sbom/images.json" <<'PYEOF'
import json, sys
p = sys.argv[1]
data = json.load(open(p))
data[0]['sourceCommit'] = data[0]['sourceCommit'][:12]
json.dump(data, open(p, 'w'))
PYEOF
if run_validator "$REAL_HEAD" >/dev/null 2>&1; then
  report_fail "SHA abreviada no manifesto deveria ser rejeitada"
else
  report_pass "SHA abreviada no manifesto e rejeitada"
fi
mv "$FIXTURE_DIR/sbom/images.json.bak" "$FIXTURE_DIR/sbom/images.json"

# Teste: imageId do summary divergente do manifesto -> falha.
cp "$FIXTURE_DIR/vulnerability/ledger-api.summary.json" "$FIXTURE_DIR/vulnerability/ledger-api.summary.json.bak"
python3 - "$FIXTURE_DIR/vulnerability/ledger-api.summary.json" <<'PYEOF'
import json, sys
p = sys.argv[1]
data = json.load(open(p))
data['imageId'] = 'sha256:' + ('f' * 64)
json.dump(data, open(p, 'w'))
PYEOF
if run_validator "$REAL_HEAD" >/dev/null 2>&1; then
  report_fail "imageId divergente do manifesto deveria ser rejeitado"
else
  report_pass "imageId divergente do manifesto e rejeitado"
fi
mv "$FIXTURE_DIR/vulnerability/ledger-api.summary.json.bak" "$FIXTURE_DIR/vulnerability/ledger-api.summary.json"

# Teste: SBOM gerado para outra imagem (metadata nao bate) -> falha.
cp "$FIXTURE_DIR/sbom/ledger-api.cyclonedx.json" "$FIXTURE_DIR/sbom/ledger-api.cyclonedx.json.bak"
python3 - "$FIXTURE_DIR/sbom/ledger-api.cyclonedx.json" <<'PYEOF'
import json, sys
p = sys.argv[1]
data = json.load(open(p))
data['metadata'] = {'component': {'name': 'imagem-totalmente-diferente', 'type': 'container'}}
json.dump(data, open(p, 'w'))
PYEOF
if run_validator "$REAL_HEAD" >/dev/null 2>&1; then
  report_fail "SBOM associado a imagem errada deveria ser rejeitado"
else
  report_pass "SBOM associado a imagem errada e rejeitado"
fi
mv "$FIXTURE_DIR/sbom/ledger-api.cyclonedx.json.bak" "$FIXTURE_DIR/sbom/ledger-api.cyclonedx.json"

# Teste: sourceTreeClean=false no manifesto -> falha.
cp "$FIXTURE_DIR/sbom/images.json" "$FIXTURE_DIR/sbom/images.json.bak"
python3 - "$FIXTURE_DIR/sbom/images.json" <<'PYEOF'
import json, sys
p = sys.argv[1]
data = json.load(open(p))
data[0]['sourceTreeClean'] = False
json.dump(data, open(p, 'w'))
PYEOF
if run_validator "$REAL_HEAD" >/dev/null 2>&1; then
  report_fail "sourceTreeClean=false deveria ser rejeitado"
else
  report_pass "sourceTreeClean=false e rejeitado"
fi
mv "$FIXTURE_DIR/sbom/images.json.bak" "$FIXTURE_DIR/sbom/images.json"

# Teste: excecao expirada -> falha.
python3 - "$FIXTURE_DIR/security/excecoes-de-vulnerabilidade.json" <<'PYEOF'
import json, sys, datetime
p = sys.argv[1]
past = (datetime.date.today() - datetime.timedelta(days=1)).isoformat()
json.dump([{
    'id': 'EXC-TEST-001', 'vulnerabilityIds': ['CVE-2020-0001'], 'packages': ['x'],
    'affectedImages': ['ledger-api'], 'rationale': 'r', 'applicability': 'a',
    'compensatingControls': ['c'], 'owner': 'o', 'created': '2020-01-01',
    'reviewDate': '2020-02-01', 'expires': past, 'status': 'active',
    'remediationCondition': 'x', 'source': 'https://example.invalid',
}], open(p, 'w'))
PYEOF
if run_validator "$REAL_HEAD" >/dev/null 2>&1; then
  report_fail "excecao expirada deveria ser rejeitada"
else
  report_pass "excecao expirada e rejeitada"
fi
echo '[]' > "$FIXTURE_DIR/security/excecoes-de-vulnerabilidade.json"

# Teste: veredito scan_error nunca e evidencia valida.
cp "$FIXTURE_DIR/vulnerability/ledger-api.summary.json" "$FIXTURE_DIR/vulnerability/ledger-api.summary.json.bak"
python3 - "$FIXTURE_DIR/vulnerability/ledger-api.summary.json" <<'PYEOF'
import json, sys
p = sys.argv[1]
json.dump({'component': 'ledger-api', 'verdict': 'scan_error', 'error': 'banco de vulnerabilidades indisponivel (teste)'}, open(p, 'w'))
PYEOF
if run_validator "$REAL_HEAD" >/dev/null 2>&1; then
  report_fail "veredito scan_error deveria ser rejeitado"
else
  report_pass "veredito scan_error e rejeitado"
fi
mv "$FIXTURE_DIR/vulnerability/ledger-api.summary.json.bak" "$FIXTURE_DIR/vulnerability/ledger-api.summary.json"

rm -rf "$FIXTURE_DIR"
FIXTURE_DIR=""

echo ""
echo "=== test-provenance-guards: ${PASS_COUNT} passaram, ${FAIL_COUNT} falharam ==="
[ "$FAIL_COUNT" -eq 0 ]
