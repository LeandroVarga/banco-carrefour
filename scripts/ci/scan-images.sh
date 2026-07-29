#!/bin/sh
# Escaneia vulnerabilidades das mesmas imagens usadas para SBOM e para o
# gate non-root (hoje 4 imagens de negocio + 1 operacional migration-runner,
# todas escaneadas pela mesma cadeia, sem distincao no scan em si), usando
# o mesmo Trivy fixado por digest.
# Produz um relatorio JSON por imagem em artifacts/vulnerability/ e aplica
# a politica de excecao temporaria - ver
# docs/security/dependencias-e-supply-chain.md e
# docs/security/excecoes-de-vulnerabilidade.json.
#
# Veredito por imagem (artifacts/vulnerability/<nome>.summary.json):
#   pass                 - nenhum achado CRITICAL/HIGH bloqueante.
#   pass_with_exceptions - so ha achados CRITICAL/HIGH sem correcao e TODOS
#                          cobertos por excecao ativa e nao expirada.
#   fail                 - CRITICAL/HIGH com correcao disponivel (excecionado
#                          ou nao), OU CRITICAL/HIGH sem correcao e SEM
#                          excecao ativa, OU excecao invalida/expirada/orfa.
#   scan_error           - o scanner falhou ao executar (banco de
#                          vulnerabilidades indisponivel, erro de rede,
#                          relatorio ausente/vazio) - NUNCA tratado como
#                          scan limpo.
#
# Uso:
#   sh scripts/ci/scan-images.sh
set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

TRIVY_IMAGE="docker.io/aquasec/trivy@sha256:cffe3f5161a47a6823fbd23d985795b3ed72a4c806da4c4df16266c02accdd6f"
TRIVY_CACHE_VOLUME="banco-carrefour-trivy-cache"
EXCEPTIONS_FILE="docs/security/excecoes-de-vulnerabilidade.json"

log() { printf '%s\n' "$1"; }
fail() { echo "scan-images: FALHA: $1" >&2; exit 1; }

[ -f artifacts/sbom/images.json ] || fail "artifacts/sbom/images.json nao encontrado - rode build-images-for-supply-chain.sh primeiro."

mkdir -p artifacts/vulnerability
docker pull "$TRIVY_IMAGE" >/dev/null
docker volume create "$TRIVY_CACHE_VOLUME" >/dev/null

rm -f artifacts/vulnerability/.scan-exec-failed

# "tr -d '\r'" defende contra CRLF introduzido por ambientes onde a saida
# do subprocesso Python passa por uma camada de texto que normaliza quebras
# de linha (observado localmente no Windows/Git Bash).
COMPONENTS=$(python3 -c "
import json
with open('artifacts/sbom/images.json') as f:
    data = json.load(f)
for e in data:
    print(e['component'] + '|' + e['image'] + '|' + e['imageId'])
" | tr -d '\r')

echo "$COMPONENTS" | while IFS='|' read -r NAME IMAGE IMAGE_ID; do
  [ -z "$NAME" ] && continue

  REPORT_FILE="artifacts/vulnerability/${NAME}.trivy.json"
  SUMMARY_FILE="artifacts/vulnerability/${NAME}.summary.json"
  log "=== Scan: ${NAME} (${IMAGE}) ==="

  # "--exit-code 0" aqui: a decisao de politica (bloquear ou nao) e tomada
  # DEPOIS do loop, lendo o JSON de todas as imagens de uma vez - assim
  # distinguimos "scanner falhou ao rodar" (branco abaixo, escreve
  # verdict=scan_error e NUNCA um relatorio limpo) de "scanner rodou e
  # encontrou achados" (JSON gerado normalmente, politica aplicada depois).
  # MSYS_NO_PATHCONV=1: no Windows/Git Bash, o MSYS reconhece "/var" como um
  # dos diretorios top-level que sempre converte para um caminho Windows
  # (ex.: "C:\Program Files\Git\var"), mesmo quando o argumento e
  # "/var/run/docker.sock:/var/run/docker.sock" (bind mount do socket do
  # Docker, que precisa permanecer literal dos dois lados) - achado real,
  # reproduzido e confirmado (docker falha com "mkdir C:\Program
  # Files\Git\var: Access is denied"). Sem efeito em Linux (variavel
  # simplesmente ignorada).
  if ! MSYS_NO_PATHCONV=1 docker run --rm \
    -v /var/run/docker.sock:/var/run/docker.sock \
    -v "${TRIVY_CACHE_VOLUME}:/root/.cache/trivy" \
    -v "$(pwd)/artifacts/vulnerability:/out" \
    "$TRIVY_IMAGE" image \
    --format json \
    --output "/out/${NAME}.trivy.json" \
    --exit-code 0 \
    --scanners vuln \
    "$IMAGE" || [ ! -s "$REPORT_FILE" ]; then
    printf '{\n  "component": "%s",\n  "image": "%s",\n  "imageId": "%s",\n  "verdict": "scan_error",\n  "error": "o scanner Trivy falhou ao executar ou nao produziu relatorio (banco de vulnerabilidades indisponivel ou erro de execucao) - NAO tratado como scan limpo."\n}\n' \
      "$NAME" "$IMAGE" "$IMAGE_ID" > "$SUMMARY_FILE"
    echo "1" > artifacts/vulnerability/.scan-exec-failed
    log "  scan_error: o scanner nao produziu um relatorio valido para ${NAME}."
    continue
  fi

  log "  relatorio bruto gravado: ${REPORT_FILE}"
done

if [ -f artifacts/vulnerability/.scan-exec-failed ]; then
  rm -f artifacts/vulnerability/.scan-exec-failed
  fail "o scanner Trivy falhou ao executar para uma ou mais imagens - ver artifacts/vulnerability/*.summary.json (verdict=scan_error). NAO tratado como scan limpo."
fi

# Versao/estado do banco de vulnerabilidades do MESMO Trivy usado para as 4
# imagens - capturado uma unica vez (mesma engine, mesmo cache) e anexado a
# cada summary e ao manifesto (artifacts/sbom/images.json), em vez de
# reportar o schema do JSON de saida (que e a versao do FORMATO do
# relatorio, nao a versao da ferramenta - confundir os dois foi um defeito
# real corrigido nesta auditoria).
TRIVY_VERSION_JSON=$(docker run --rm -v "${TRIVY_CACHE_VOLUME}:/root/.cache/trivy" "$TRIVY_IMAGE" version --format json)
export TRIVY_VERSION_JSON
export TRIVY_IMAGE_REF="$TRIVY_IMAGE"
export EXCEPTIONS_FILE

POLICY_EXIT=0
python3 <<'PYEOF' || POLICY_EXIT=$?
import json, os, re, sys, datetime

exceptions_path = os.environ["EXCEPTIONS_FILE"]
trivy_meta = json.loads(os.environ["TRIVY_VERSION_JSON"])
trivy_image_ref = os.environ["TRIVY_IMAGE_REF"]
scanner_digest = trivy_image_ref.split("@")[1] if "@" in trivy_image_ref else None

with open("artifacts/sbom/images.json") as f:
    images = json.load(f)

try:
    with open(exceptions_path) as f:
        exceptions = json.load(f)
except FileNotFoundError:
    exceptions = []

errors = []
today = datetime.date.today().isoformat()
REQUIRED_EXCEPTION_FIELDS = (
    "id", "vulnerabilityIds", "packages", "affectedImages", "rationale",
    "applicability", "compensatingControls", "owner", "created",
    "reviewDate", "expires", "status", "remediationCondition", "source",
)
VULN_ID_RE = re.compile(r"^(CVE-\d{4}-\d{4,}|GHSA-[a-zA-Z0-9]{4}-[a-zA-Z0-9]{4}-[a-zA-Z0-9]{4}|TEMP-[0-9-]+-[A-Fa-f0-9]+)$")

for exc in exceptions:
    for field in REQUIRED_EXCEPTION_FIELDS:
        if field not in exc or exc[field] in (None, "", []):
            errors.append(f"excecao {exc.get('id', '?')}: campo obrigatorio ausente ou vazio: {field}")
    if exc.get("expires") and exc["expires"] < today:
        errors.append(f"excecao {exc.get('id')} expirada em {exc['expires']} (hoje: {today}) - remover ou renovar antes de continuar.")
    if exc.get("reviewDate") and exc["reviewDate"] < today:
        errors.append(f"excecao {exc.get('id')}: data de revisao ({exc['reviewDate']}) ja passou - revisar antes de continuar.")
    for vid in exc.get("vulnerabilityIds", []):
        if not VULN_ID_RE.match(vid):
            errors.append(f"excecao {exc.get('id')}: ID de vulnerabilidade com formato desconhecido: {vid}")

if errors:
    print("scan-images: excecoes invalidas:", file=sys.stderr)
    for e in errors:
        print(f"  - {e}", file=sys.stderr)
    sys.exit(1)

exception_matched = {exc["id"]: False for exc in exceptions}


def find_exception(vuln_id, package):
    for exc in exceptions:
        if exc.get("status") != "active":
            continue
        if vuln_id in exc.get("vulnerabilityIds", []) and package in exc.get("packages", []):
            return exc
    return None


overall_verdict = "pass"

for entry in images:
    name = entry["component"]
    report_path = f"artifacts/vulnerability/{name}.trivy.json"
    with open(report_path) as f:
        report = json.load(f)

    critical_with_fix = high_with_fix = critical_no_fix = high_no_fix = 0
    findings = []
    blocking_reasons = []
    exceptions_applied = []

    for result in report.get("Results", []) or []:
        target = result.get("Target", "")
        for vuln in result.get("Vulnerabilities", []) or []:
            severity = vuln.get("Severity", "UNKNOWN")
            vuln_id = vuln.get("VulnerabilityID", "")
            pkg_name = vuln.get("PkgName", "")
            fixed_version = vuln.get("FixedVersion", "")
            has_fix = bool(fixed_version)
            exc = find_exception(vuln_id, pkg_name)

            findings.append({
                "target": target,
                "vulnerabilityId": vuln_id,
                "package": pkg_name,
                "installedVersion": vuln.get("InstalledVersion", ""),
                "fixedVersion": fixed_version,
                "severity": severity,
                "fixAvailable": has_fix,
                "exceptionId": exc["id"] if exc else None,
            })

            if severity not in ("CRITICAL", "HIGH"):
                continue

            if exc:
                exception_matched[exc["id"]] = True
                if has_fix:
                    # Uma excecao so cobre achados SEM correcao disponivel -
                    # se uma correcao surgiu, a excecao ficou obsoleta e o
                    # achado volta a ser bloqueante ate a excecao ser revisada.
                    blocking_reasons.append(
                        f"{vuln_id}/{pkg_name}: correcao disponivel ({fixed_version}) para achado com "
                        f"excecao ativa {exc['id']} - revisar/remover a excecao antes de prosseguir."
                    )
                    if severity == "CRITICAL":
                        critical_with_fix += 1
                    else:
                        high_with_fix += 1
                else:
                    exceptions_applied.append(exc["id"])
                    if severity == "CRITICAL":
                        critical_no_fix += 1
                    else:
                        high_no_fix += 1
                continue

            if has_fix:
                blocking_reasons.append(f"{vuln_id}/{pkg_name}: {severity} com correcao disponivel ({fixed_version}), sem excecao.")
                if severity == "CRITICAL":
                    critical_with_fix += 1
                else:
                    high_with_fix += 1
            else:
                blocking_reasons.append(f"{vuln_id}/{pkg_name}: {severity} sem correcao disponivel e sem excecao ativa.")
                if severity == "CRITICAL":
                    critical_no_fix += 1
                else:
                    high_no_fix += 1

    if blocking_reasons:
        verdict = "fail"
        overall_verdict = "fail"
    elif exceptions_applied:
        verdict = "pass_with_exceptions"
        if overall_verdict == "pass":
            overall_verdict = "pass_with_exceptions"
    else:
        verdict = "pass"

    summary = {
        "component": name,
        "image": entry["image"],
        "imageId": entry["imageId"],
        "sourceCommit": entry["sourceCommit"],
        "sourceTreeClean": entry.get("sourceTreeClean"),
        "scannerName": "trivy",
        "scannerVersion": trivy_meta.get("Version"),
        "scannerImageDigest": scanner_digest,
        "scannerDatabaseTimestamp": trivy_meta.get("VulnerabilityDB", {}).get("UpdatedAt"),
        "reportSchemaVersion": report.get("SchemaVersion"),
        "totalFindings": len(findings),
        "criticalWithFixCount": critical_with_fix,
        "criticalNoFixCount": critical_no_fix,
        "highWithFixCount": high_with_fix,
        "highNoFixCount": high_no_fix,
        "activeExceptionsApplied": sorted(set(exceptions_applied)),
        "blockingReasons": blocking_reasons,
        "verdict": verdict,
        "findings": findings,
    }
    with open(f"artifacts/vulnerability/{name}.summary.json", "w") as fh:
        json.dump(summary, fh, indent=2)

    print(
        f"  {name}: achados={len(findings)} critical-com-fix={critical_with_fix} "
        f"critical-sem-fix={critical_no_fix} high-com-fix={high_with_fix} "
        f"high-sem-fix={high_no_fix} excecoes-aplicadas={sorted(set(exceptions_applied))} "
        f"veredito={verdict}"
    )

orphaned = sorted([eid for eid, matched in exception_matched.items() if not matched])
if orphaned:
    errors.append(
        f"excecao(oes) sem nenhum achado correspondente em nenhuma das imagens escaneadas (excecao obsoleta - "
        f"o achado provavelmente ja foi corrigido - remover a excecao): {orphaned}"
    )

# Finaliza o manifesto (artifacts/sbom/images.json) com os campos de
# scanner e os caminhos de evidencia - este e o "manifesto" final consumido
# por validate-supply-chain-artifacts.sh.
for entry in images:
    entry["scannerName"] = "trivy"
    entry["scannerVersion"] = trivy_meta.get("Version")
    entry["scannerImageDigest"] = scanner_digest
    entry["scannerDatabaseTimestamp"] = trivy_meta.get("VulnerabilityDB", {}).get("UpdatedAt")

with open("artifacts/sbom/images.json", "w") as f:
    json.dump(images, f, indent=2)

if errors:
    print("scan-images: falhas de politica:", file=sys.stderr)
    for e in errors:
        print(f"  - {e}", file=sys.stderr)
    sys.exit(1)

print(f"veredito geral: {overall_verdict}")
sys.exit(0 if overall_verdict in ("pass", "pass_with_exceptions") else 2)
PYEOF

if [ "$POLICY_EXIT" -ne 0 ]; then
  fail "politica de vulnerabilidade reprovou (achado bloqueante, excecao invalida/expirada/orfa, ou ID de vulnerabilidade com formato desconhecido) - ver artifacts/vulnerability/*.summary.json."
fi

log ""
log "=== Scan de vulnerabilidade concluido - veredito pass ou pass_with_exceptions em todas as imagens ==="
