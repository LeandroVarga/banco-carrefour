#!/bin/sh
# Valida todas as evidencias de supply chain geradas
# antes de publica-las como artifact de workflow. Falha se qualquer
# arquivo esperado estiver ausente, vazio, com JSON invalido, associado a
# imagem/commit errado, sem veredito valido, com scanner marcado como
# falho (scan_error), com excecao invalida/expirada/orfa, ou se o
# manifesto nao corresponder ao HEAD atual do repositorio. Nunca trata
# arquivo vazio como evidencia valida.
#
# alem das
# checagens originais, este script agora tambem confirma que o
# sourceCommit registrado em cada evidencia e a SHA completa (40 hex) e
# corresponde exatamente ao "git rev-parse HEAD" atual - sem essa
# comparacao, um manifesto poderia apontar para um commit antigo (o
# defeito real encontrado no audit) sem que nada acusasse o problema.
#
# Variaveis de ambiente (todas opcionais - usadas por
# scripts/ci/test-provenance-guards.sh para validar copias temporarias de
# fixtures sem tocar nas evidencias reais nem no HEAD real do repositorio):
#   SUPPLY_CHAIN_IMAGES_JSON      (default: artifacts/sbom/images.json)
#   SUPPLY_CHAIN_SBOM_DIR         (default: artifacts/sbom)
#   SUPPLY_CHAIN_VULN_DIR         (default: artifacts/vulnerability)
#   SUPPLY_CHAIN_NUGET_FILE       (default: artifacts/nuget/vulnerable-packages.json)
#   SUPPLY_CHAIN_LICENSE_FILE     (default: artifacts/sbom/license-inventory.json)
#   SUPPLY_CHAIN_EXCEPTIONS_FILE  (default: docs/security/excecoes-de-vulnerabilidade.json)
#   SUPPLY_CHAIN_EXPECTED_HEAD    (default: git rev-parse HEAD)
#   SUPPLY_CHAIN_SKIP_LIVE_TREE_CHECK=1 (pula a checagem de arvore limpa AO VIVO
#                                        do repositorio - usado apenas pelos
#                                        testes isolados de fixture)
#
# Uso:
#   sh scripts/ci/validate-supply-chain-artifacts.sh
set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

log() { printf '%s\n' "$1"; }
fail() { echo "validate-supply-chain-artifacts: FALHA: $1" >&2; exit 1; }

FORBIDDEN_PATH_SEGMENTS=".local/security .env .pem .key terraform.tfstate tfplan"

check_no_forbidden_paths() {
  FILE="$1"
  for segment in $FORBIDDEN_PATH_SEGMENTS; do
    if grep -qF "$segment" "$FILE" 2>/dev/null; then
      fail "${FILE} referencia um caminho proibido ('${segment}')."
    fi
  done
}

if [ "${SUPPLY_CHAIN_SKIP_LIVE_TREE_CHECK:-0}" != "1" ]; then
  sh "$REPO_ROOT/scripts/ci/require-clean-source-tree.sh" "$REPO_ROOT" \
    || fail "arvore de trabalho do repositorio nao esta limpa - a evidencia gerada agora nao seria reproduzivel a partir do HEAD atual."
fi

export SUPPLY_CHAIN_IMAGES_JSON="${SUPPLY_CHAIN_IMAGES_JSON:-artifacts/sbom/images.json}"
export SUPPLY_CHAIN_SBOM_DIR="${SUPPLY_CHAIN_SBOM_DIR:-artifacts/sbom}"
export SUPPLY_CHAIN_VULN_DIR="${SUPPLY_CHAIN_VULN_DIR:-artifacts/vulnerability}"
export SUPPLY_CHAIN_NUGET_FILE="${SUPPLY_CHAIN_NUGET_FILE:-artifacts/nuget/vulnerable-packages.json}"
export SUPPLY_CHAIN_LICENSE_FILE="${SUPPLY_CHAIN_LICENSE_FILE:-artifacts/sbom/license-inventory.json}"
export SUPPLY_CHAIN_EXCEPTIONS_FILE="${SUPPLY_CHAIN_EXCEPTIONS_FILE:-docs/security/excecoes-de-vulnerabilidade.json}"
export SUPPLY_CHAIN_EXPECTED_HEAD="${SUPPLY_CHAIN_EXPECTED_HEAD:-$(git rev-parse HEAD)}"
export SUPPLY_CHAIN_APPROVED_TRIVY_DIGEST="sha256:cffe3f5161a47a6823fbd23d985795b3ed72a4c806da4c4df16266c02accdd6f"

VALIDATION_EXIT=0
python3 <<'PYEOF' || VALIDATION_EXIT=$?
import json, os, re, sys, datetime

images_json_path = os.environ["SUPPLY_CHAIN_IMAGES_JSON"]
sbom_dir = os.environ["SUPPLY_CHAIN_SBOM_DIR"]
vuln_dir = os.environ["SUPPLY_CHAIN_VULN_DIR"]
nuget_file = os.environ["SUPPLY_CHAIN_NUGET_FILE"]
license_file = os.environ["SUPPLY_CHAIN_LICENSE_FILE"]
exceptions_path = os.environ["SUPPLY_CHAIN_EXCEPTIONS_FILE"]
expected_head = os.environ["SUPPLY_CHAIN_EXPECTED_HEAD"]
approved_scanner_digest = os.environ["SUPPLY_CHAIN_APPROVED_TRIVY_DIGEST"]

errors = []
SHA_RE = re.compile(r"^[0-9a-f]{40}$")


def load_json(path):
    try:
        with open(path) as f:
            content = f.read()
    except FileNotFoundError:
        errors.append(f"ausente: {path}")
        return None
    if not content.strip():
        errors.append(f"vazio: {path}")
        return None
    try:
        return json.loads(content)
    except json.JSONDecodeError as e:
        errors.append(f"JSON invalido em {path}: {e}")
        return None


if not SHA_RE.match(expected_head):
    errors.append(f"SUPPLY_CHAIN_EXPECTED_HEAD nao e uma SHA completa de 40 hex: {expected_head!r}")

# 1) Manifesto de imagens
images = load_json(images_json_path)
if images is None:
    print("\n".join(errors), file=sys.stderr)
    sys.exit(1)

EXPECTED_COMPONENTS = {
    "ledger-api",
    "ledger-outbox-publisher",
    "consolidation-api",
    "consolidation-worker",
    "migration-runner",
}
actual_components = {entry.get("component") for entry in images}
if actual_components != EXPECTED_COMPONENTS:
    errors.append(
        f"{images_json_path} deveria conter exatamente os componentes {sorted(EXPECTED_COMPONENTS)}, "
        f"tem {sorted(actual_components)}"
    )

for entry in images:
    name = entry.get("component", "?")
    source_commit = entry.get("sourceCommit", "")

    if not SHA_RE.match(source_commit or ""):
        errors.append(f"{images_json_path}[{name}]: sourceCommit nao e uma SHA completa de 40 hex: {source_commit!r}")
    elif source_commit != expected_head:
        errors.append(
            f"{images_json_path}[{name}]: sourceCommit ({source_commit}) difere do HEAD atual "
            f"({expected_head}) - a imagem/evidencia nao corresponde ao commit vigente."
        )

    if entry.get("sourceTreeClean") is not True:
        errors.append(f"{images_json_path}[{name}]: sourceTreeClean nao e 'true' ({entry.get('sourceTreeClean')!r}) - build a partir de arvore suja.")

    if entry.get("scannerImageDigest") and entry["scannerImageDigest"] != approved_scanner_digest:
        errors.append(
            f"{images_json_path}[{name}]: scannerImageDigest ({entry.get('scannerImageDigest')}) difere "
            f"do digest aprovado do Trivy ({approved_scanner_digest})."
        )
    if not entry.get("scannerVersion"):
        errors.append(f"{images_json_path}[{name}]: scannerVersion ausente.")
    if not entry.get("scannerDatabaseTimestamp"):
        errors.append(f"{images_json_path}[{name}]: scannerDatabaseTimestamp ausente.")

# 2) SBOM por imagem
for entry in images:
    name = entry["component"]
    sbom_path = f"{sbom_dir}/{name}.cyclonedx.json"
    sbom = load_json(sbom_path)
    if sbom is None:
        continue
    components = sbom.get("components", [])
    if len(components) == 0:
        errors.append(f"{sbom_path}: inventario de componentes vazio")
    sbom_image_ref = json.dumps(sbom.get("metadata", {}))
    if entry["image"].split(":")[0] not in sbom_image_ref and name not in sbom_image_ref:
        errors.append(f"{sbom_path}: SBOM nao referencia a imagem esperada ({entry['image']})")

# 3) Relatorio + sumario de vulnerabilidade por imagem
VALID_VERDICTS = ("pass", "pass_with_exceptions", "fail", "scan_error")
for entry in images:
    name = entry["component"]
    summary_path = f"{vuln_dir}/{name}.summary.json"
    summary = load_json(summary_path)
    if summary is None:
        continue

    verdict = summary.get("verdict")
    if verdict not in VALID_VERDICTS:
        errors.append(f"{summary_path}: veredito ausente ou invalido ({verdict!r}) - deve ser um de {VALID_VERDICTS}")
    elif verdict == "scan_error":
        errors.append(f"{summary_path}: scanner reportou scan_error - {summary.get('error', 'sem detalhe')} - NUNCA tratado como evidencia valida.")
    elif verdict == "fail":
        errors.append(f"{summary_path}: veredito fail - achado bloqueante ou excecao invalida - ver blockingReasons.")

    if verdict in ("pass", "pass_with_exceptions"):
        if summary.get("imageId") != entry["imageId"]:
            errors.append(f"{summary_path}: imageId nao corresponde ao manifesto ({summary.get('imageId')} != {entry['imageId']})")
        source_commit = summary.get("sourceCommit", "")
        if not SHA_RE.match(source_commit or "") or source_commit != expected_head:
            errors.append(f"{summary_path}: sourceCommit ({source_commit!r}) nao corresponde ao HEAD atual ({expected_head}).")
        if summary.get("sourceTreeClean") is not True:
            errors.append(f"{summary_path}: sourceTreeClean nao e 'true'.")
        if not summary.get("scannerVersion"):
            errors.append(f"{summary_path}: scannerVersion ausente")
        if not summary.get("scannerDatabaseTimestamp"):
            errors.append(f"{summary_path}: scannerDatabaseTimestamp ausente")
        if summary.get("scannerImageDigest") != approved_scanner_digest:
            errors.append(f"{summary_path}: scannerImageDigest ({summary.get('scannerImageDigest')}) difere do digest aprovado.")

# 4) Auditoria NuGet
nuget = load_json(nuget_file)
if nuget is not None:
    if nuget.get("verdict") not in ("pass", "fail"):
        errors.append(f"{nuget_file}: veredito ausente ou invalido")
    if not nuget.get("dotnetSdkVersion"):
        errors.append(f"{nuget_file}: dotnetSdkVersion ausente")

# 5) Inventario de licencas
licenses = load_json(license_file)
if licenses is not None and licenses.get("totalPackageLicensePairs", 0) == 0:
    errors.append(f"{license_file}: inventario vazio")

# 6) Excecoes de vulnerabilidade - schema, expiracao, revisao, formato de ID
REQUIRED_EXCEPTION_FIELDS = (
    "id", "vulnerabilityIds", "packages", "affectedImages", "rationale",
    "applicability", "compensatingControls", "owner", "created",
    "reviewDate", "expires", "status", "remediationCondition", "source",
)
VULN_ID_RE = re.compile(r"^(CVE-\d{4}-\d{4,}|GHSA-[a-zA-Z0-9]{4}-[a-zA-Z0-9]{4}-[a-zA-Z0-9]{4}|TEMP-[0-9-]+-[A-Fa-f0-9]+)$")
exceptions = load_json(exceptions_path)
if exceptions is not None:
    today = datetime.date.today().isoformat()
    seen_ids = set()
    for exc in exceptions:
        exc_id = exc.get("id", "?")
        if exc_id in seen_ids:
            errors.append(f"{exceptions_path}: id de excecao duplicado: {exc_id}")
        seen_ids.add(exc_id)
        for field in REQUIRED_EXCEPTION_FIELDS:
            if field not in exc or exc[field] in (None, "", []):
                errors.append(f"{exceptions_path}[{exc_id}]: campo obrigatorio ausente ou vazio: {field}")
        if exc.get("expires") and exc["expires"] < today:
            errors.append(f"{exceptions_path}[{exc_id}]: excecao expirada (expirou em {exc['expires']}, hoje: {today})")
        if exc.get("reviewDate") and exc["reviewDate"] < today:
            errors.append(f"{exceptions_path}[{exc_id}]: data de revisao ja passou ({exc['reviewDate']})")
        if exc.get("status") not in ("active", "expired", "retired"):
            errors.append(f"{exceptions_path}[{exc_id}]: status desconhecido: {exc.get('status')!r}")
        for vid in exc.get("vulnerabilityIds", []) or []:
            if not VULN_ID_RE.match(vid):
                errors.append(f"{exceptions_path}[{exc_id}]: ID de vulnerabilidade com formato desconhecido: {vid}")

if errors:
    print("Falhas de validacao encontradas:", file=sys.stderr)
    for e in errors:
        print(f"  - {e}", file=sys.stderr)
    sys.exit(1)

print(
    f"Todas as evidencias de supply chain ({len(images)} SBOMs, {len(images)} relatorios de vulnerabilidade, "
    "auditoria NuGet, inventario de licencas, excecoes) validadas com sucesso "
    f"contra o HEAD {expected_head}."
)
PYEOF

if [ "$VALIDATION_EXIT" -ne 0 ]; then
  fail "validacao de evidencias de supply chain reprovou - ver saida acima."
fi

log "1) Verificando ausencia de caminhos proibidos nos artifacts gerados..."
NUGET_DIR="$(dirname "$SUPPLY_CHAIN_NUGET_FILE")"
for f in "$SUPPLY_CHAIN_SBOM_DIR"/*.json "$SUPPLY_CHAIN_VULN_DIR"/*.json "$NUGET_DIR"/*.json; do
  [ -f "$f" ] || continue
  check_no_forbidden_paths "$f"
done

log ""
log "=== Validacao de evidencias de supply chain concluida com sucesso ==="
