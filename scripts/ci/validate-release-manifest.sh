#!/bin/sh
# Valida artifacts/release/release-manifest.json antes (e depois) de
# qualquer publicacao real. Nunca trata um manifesto
# incompleto ou inconsistente como valido. Schema de referencia formal:
# schemas/release-manifest.schema.json (versao 4.0.0, alvo Amazon ECR/AWS
# real - ver ADR-0013).
#
# Variaveis de ambiente opcionais (usadas por
# scripts/ci/test-release-guards.sh para validar copias sinteticas sem
# tocar no manifesto real nem no HEAD real do repositorio):
#   RELEASE_MANIFEST_FILE       (default: artifacts/release/release-manifest.json)
#   RELEASE_EXPECTED_HEAD       (default: git rev-parse HEAD)
#   RELEASE_SKIP_LIVE_TREE_CHECK=1 (pula a checagem de arvore limpa AO VIVO
#                                   do repositorio - usado apenas pelos
#                                   testes isolados de fixture)
#
# Uso:
#   sh scripts/ci/validate-release-manifest.sh
set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

log() { printf '%s\n' "$1"; }
fail() { echo "validate-release-manifest: FALHA: $1" >&2; exit 1; }

if [ "${RELEASE_SKIP_LIVE_TREE_CHECK:-0}" != "1" ]; then
  sh "$REPO_ROOT/scripts/ci/require-clean-source-tree.sh" "$REPO_ROOT" \
    || fail "arvore de trabalho do repositorio nao esta limpa."
fi

export RELEASE_MANIFEST_FILE="${RELEASE_MANIFEST_FILE:-artifacts/release/release-manifest.json}"
export RELEASE_EXPECTED_HEAD="${RELEASE_EXPECTED_HEAD:-$(git rev-parse HEAD)}"

VALIDATION_EXIT=0
python3 <<'PYEOF' || VALIDATION_EXIT=$?
import json, os, re, sys

manifest_path = os.environ["RELEASE_MANIFEST_FILE"]
expected_head = os.environ["RELEASE_EXPECTED_HEAD"]

EXPECTED_COMPONENTS = {"ledger-api", "ledger-outbox-publisher", "consolidation-api", "consolidation-worker"}
SHA_RE = re.compile(r"^[0-9a-f]{40}$")
CANONICAL_TAG_RE = re.compile(r"^sha-[0-9a-f]{40}$")
DIGEST_RE = re.compile(r"^sha256:[0-9a-f]{64}$")
ECR_ACCOUNT_RE = re.compile(r"^[0-9]{12}$")
ECR_REGION_RE = re.compile(r"^[a-z0-9-]+$")
VALID_ATTESTATION_STATUSES = ("not_applicable", "pending_hosted_execution", "generated", "failed", "verified")
PLACEHOLDER_VALUES = {"", "null", "none", "n/a", "todo", "tbd", "pending"}

errors = []

try:
    with open(manifest_path) as f:
        content = f.read()
except FileNotFoundError:
    print(f"ausente: {manifest_path}", file=sys.stderr)
    sys.exit(1)

if not content.strip():
    print(f"vazio: {manifest_path}", file=sys.stderr)
    sys.exit(1)

try:
    manifest = json.loads(content)
except json.JSONDecodeError as e:
    print(f"JSON invalido em {manifest_path}: {e}", file=sys.stderr)
    sys.exit(1)

if manifest.get("schemaVersion") != "4.0.0":
    errors.append(f"schemaVersion deveria ser '4.0.0' (schemas/release-manifest.schema.json), encontrado: {manifest.get('schemaVersion')!r}")

if manifest.get("registryType") != "ecr":
    errors.append(f"registryType deveria ser 'ecr' (unico registry oficial - ver ADR-0013), encontrado: {manifest.get('registryType')!r}")

top_source_commit = manifest.get("sourceCommit", "")
if not SHA_RE.match(top_source_commit or ""):
    errors.append(f"sourceCommit (top-level) nao e uma SHA completa de 40 hex: {top_source_commit!r}")
elif top_source_commit != expected_head:
    errors.append(f"sourceCommit (top-level) ({top_source_commit}) difere do HEAD atual ({expected_head})")

release_id = manifest.get("releaseId", "")
if release_id != f"sha-{top_source_commit}":
    errors.append(f"releaseId ({release_id!r}) deveria ser 'sha-<sourceCommit>' ({'sha-' + top_source_commit!r})")

aws_account_id = manifest.get("awsAccountId")
aws_region = manifest.get("awsRegion")
if aws_account_id is not None and not ECR_ACCOUNT_RE.match(aws_account_id):
    errors.append(f"awsAccountId malformado (esperado 12 digitos): {aws_account_id!r}")
if aws_region is not None and not ECR_REGION_RE.match(aws_region):
    errors.append(f"awsRegion malformado: {aws_region!r}")
if (aws_account_id is None) != (aws_region is None):
    errors.append("awsAccountId e awsRegion devem estar ambos presentes ou ambos ausentes (derivados juntos de ECR_REGISTRY)")

VALID_COMPONENT_STATUSES = ("pending", "already_published", "published", "conflict", "failed")
PUBLISHED_LIKE_STATUSES = ("already_published", "published")
EXPECTED_OPERATIONAL_ARTIFACTS = {"migration-runner"}
OPERATIONAL_ECR_URI_RE = re.compile(r"^[0-9]{12}\.dkr\.ecr\.[a-z0-9-]+\.amazonaws\.com/banco-carrefour/migration-runner$")
DIGEST_QUALIFIED_RE = re.compile(r"^[0-9]{12}\.dkr\.ecr\.[a-z0-9-]+\.amazonaws\.com/banco-carrefour/migration-runner@sha256:[0-9a-f]{64}$")

components = manifest.get("components", [])
operational_artifacts = manifest.get("operationalArtifacts", [])

component_names = [c.get("component") for c in components]
operational_names = [a.get("component") for a in operational_artifacts]

# Fronteira de negocio vs operacional: rejeita explicitamente cross-contaminacao e
# duplicatas - nunca so uma comparacao de CONJUNTO de nomes (que sozinha
# nao capturaria uma entrada duplicada quando o conjunto de nomes unicos
# ainda coincide com o esperado).
if len(components) != len(EXPECTED_COMPONENTS) or set(component_names) != EXPECTED_COMPONENTS:
    errors.append(f"componentes de negocio esperados exatamente {sorted(EXPECTED_COMPONENTS)} (sem duplicatas), encontrados {sorted(component_names)}")

for name in component_names:
    if name in EXPECTED_OPERATIONAL_ARTIFACTS:
        errors.append(f"'{name}' e um artefato operacional - nunca pode aparecer na colecao 'components' (workloads de negocio)")

if len(operational_names) != len(set(operational_names)):
    errors.append(f"operationalArtifacts contem nome(s) duplicado(s): {sorted(operational_names)}")

if not operational_artifacts:
    errors.append("operationalArtifacts esta vazio - esperado ao menos migration-runner")

for name in operational_names:
    if name in EXPECTED_COMPONENTS:
        errors.append(f"'{name}' e um workload de negocio - nunca pode aparecer na colecao 'operationalArtifacts'")
    elif name not in EXPECTED_OPERATIONAL_ARTIFACTS:
        errors.append(f"'{name}' nao e um artefato operacional conhecido (esperado um de {sorted(EXPECTED_OPERATIONAL_ARTIFACTS)})")

tag_digest_map = {}
all_statuses = []
any_attestation_failed = False
any_attestation_unresolved = False
all_source_commits = set()

def validate_entry(entry, is_operational):
    """Validacao compartilhada entre components e operationalArtifacts -
    nunca duplicada/divergente entre as duas colecoes. Acumula em
    all_statuses/tag_digest_map/any_attestation_* via closure (mesmas
    variaveis do escopo do modulo)."""
    # "global" e obrigatorio aqui: any_attestation_failed/
    # any_attestation_unresolved sao REATRIBUIDOS (nunca so mutados) nesta
    # funcao - sem "global", Python cria variaveis LOCAIS com o mesmo nome
    # que sao descartadas ao final da chamada, nunca propagando para o
    # escopo do modulo (achado real: a checagem de overallVerdict abaixo
    # nunca detectava atestacao falha/pendente por causa disso).
    global any_attestation_failed, any_attestation_unresolved
    name = entry.get("component", "?")

    source_commit = entry.get("sourceCommit", "")
    all_source_commits.add(source_commit)
    if not SHA_RE.match(source_commit or ""):
        errors.append(f"{name}: sourceCommit nao e uma SHA completa de 40 hex: {source_commit!r}")
    elif source_commit != expected_head:
        errors.append(f"{name}: sourceCommit ({source_commit}) difere do HEAD atual ({expected_head})")

    if entry.get("sourceTreeClean") is not True:
        errors.append(f"{name}: sourceTreeClean != true")

    if not entry.get("localImageId"):
        errors.append(f"{name}: localImageId ausente")

    if not entry.get("layerDigests"):
        errors.append(f"{name}: layerDigests ausente - necessario para verificacao de identidade de conteudo")

    oci_revision = entry.get("ociRevision")
    if not oci_revision or not SHA_RE.match(oci_revision):
        errors.append(f"{name}: ociRevision ausente ou malformado: {oci_revision!r}")
    elif oci_revision != source_commit:
        errors.append(f"{name}: ociRevision ({oci_revision}) difere de sourceCommit ({source_commit}) - imagem nao corresponde ao commit declarado")

    sbom_path = entry.get("sbomPath")
    if not sbom_path or not os.path.isfile(sbom_path):
        errors.append(f"{name}: SBOM ausente ({sbom_path})")

    vuln_path = entry.get("vulnerabilityReportPath")
    if not vuln_path or not os.path.isfile(vuln_path):
        errors.append(f"{name}: relatorio de vulnerabilidade ausente ({vuln_path})")

    verdict = entry.get("vulnerabilityVerdict")
    if verdict in ("fail", "scan_error"):
        errors.append(f"{name}: vulnerabilityVerdict={verdict} - publicacao deveria estar bloqueada, nao chegar a este manifesto")
    elif verdict not in ("pass", "pass_with_exceptions"):
        errors.append(f"{name}: vulnerabilityVerdict ausente ou desconhecido: {verdict!r}")

    canonical_tag = entry.get("canonicalTag", "")
    if canonical_tag == "latest" or not CANONICAL_TAG_RE.match(canonical_tag or ""):
        errors.append(f"{name}: canonicalTag nao segue o formato imutavel 'sha-<40 hex>' (nunca 'latest'): {canonical_tag!r}")

    status = entry.get("publicationStatus")
    if status not in VALID_COMPONENT_STATUSES:
        errors.append(f"{name}: publicationStatus desconhecido: {status!r} (deve ser um de {VALID_COMPONENT_STATUSES})")
    all_statuses.append(status)

    # remoteEcrDigest: nunca comparado diretamente a localImageId - ver
    # scripts/ci/test-generic-oci-publish-proof.sh para o motivo (a
    # igualdade textual e uma propriedade observada deste ambiente
    # especifico de image store, nao uma garantia geral do modelo OCI).
    remote_digest = entry.get("remoteEcrDigest")
    if status in PUBLISHED_LIKE_STATUSES:
        if not remote_digest:
            errors.append(f"{name}: publicationStatus={status} mas remoteEcrDigest esta ausente")
        elif not DIGEST_RE.match(remote_digest):
            errors.append(f"{name}: remoteEcrDigest malformado: {remote_digest!r}")
    elif status == "pending":
        if remote_digest is not None:
            errors.append(f"{name}: publicationStatus=pending mas remoteEcrDigest ja preenchido ({remote_digest}) - digest nunca deve ser registrado antes de uma publicacao/verificacao real")
    elif status in ("conflict", "failed"):
        if remote_digest is not None and status == "conflict":
            # Um conflito PODE registrar o digest existente encontrado na
            # tag (para investigacao), mas nunca o digest do conteudo que
            # se tentou publicar - portanto apenas exige formato valido.
            if not DIGEST_RE.match(remote_digest):
                errors.append(f"{name}: remoteEcrDigest malformado no registro de conflito: {remote_digest!r}")

    if remote_digest and CANONICAL_TAG_RE.match(canonical_tag or "") and status in PUBLISHED_LIKE_STATUSES:
        key = (entry.get("ecrRepositoryUri"), canonical_tag)
        if key in tag_digest_map and tag_digest_map[key] != remote_digest:
            errors.append(f"{name}: tag imutavel {canonical_tag} referencia digests conflitantes ({tag_digest_map[key]} vs {remote_digest}) no mesmo repositorio")
        tag_digest_map[key] = remote_digest

    # provenanceAttestation/sbomAttestation: mesma logica de fabricacao
    # nunca aceita - 'generated'/'verified' exigem uma reference real, e
    # jamais sao inferidos por ausencia.
    for kind in ("provenanceAttestation", "sbomAttestation"):
        att = entry.get(kind)
        if not isinstance(att, dict):
            errors.append(f"{name}: {kind} ausente ou nao e um objeto")
            continue
        att_status = att.get("status")
        att_reference = att.get("reference")
        if att_status not in VALID_ATTESTATION_STATUSES:
            errors.append(f"{name}: {kind}.status desconhecido: {att_status!r}")
            continue
        if att_status in ("generated", "verified"):
            if not att_reference or str(att_reference).strip().lower() in PLACEHOLDER_VALUES:
                errors.append(f"{name}: {kind}.status={att_status} exige 'reference' real (nao vazia/placeholder) - isso seria uma atestacao fabricada")
        else:
            if att_reference is not None:
                errors.append(f"{name}: {kind}.status={att_status} nao deveria ter 'reference' preenchida ({att_reference!r})")
        if att_status == "failed":
            any_attestation_failed = True
        if att_status == "pending_hosted_execution":
            any_attestation_unresolved = True

    if is_operational:
        artifact_type = entry.get("artifactType")
        if artifact_type != name:
            errors.append(f"{name}: artifactType ({artifact_type!r}) deveria ser igual ao nome do artefato ({name!r})")

        if not entry.get("runtimePurpose"):
            errors.append(f"{name}: runtimePurpose ausente - todo artefato operacional precisa declarar explicitamente seu proposito de execucao")

        sbom_digest = entry.get("sbomDigest")
        if not sbom_digest or not DIGEST_RE.match(sbom_digest):
            errors.append(f"{name}: sbomDigest ausente ou malformado: {sbom_digest!r}")

        ecr_uri = entry.get("ecrRepositoryUri")
        if ecr_uri is not None and not OPERATIONAL_ECR_URI_RE.match(ecr_uri):
            errors.append(f"{name}: ecrRepositoryUri operacional malformado (esperado repositorio dedicado banco-carrefour/{name}): {ecr_uri!r}")

        digest_qualified = entry.get("digestQualifiedReference")
        if status in PUBLISHED_LIKE_STATUSES:
            if not digest_qualified or not DIGEST_QUALIFIED_RE.match(digest_qualified):
                errors.append(f"{name}: digestQualifiedReference ausente ou malformado para publicationStatus={status}: {digest_qualified!r}")
            elif remote_digest and not digest_qualified.endswith(f"@{remote_digest}"):
                errors.append(f"{name}: digestQualifiedReference ({digest_qualified!r}) nao termina no mesmo remoteEcrDigest ({remote_digest!r})")
        elif status == "pending" and digest_qualified is not None:
            errors.append(f"{name}: publicationStatus=pending mas digestQualifiedReference ja preenchido ({digest_qualified!r})")

for c in components:
    validate_entry(c, is_operational=False)

for a in operational_artifacts:
    validate_entry(a, is_operational=True)

# O artefato operacional pertence à MESMA release que os 4 workloads de
# negócio - nunca um commit de origem diferente (Section 9 - ).
if len(all_source_commits) > 1:
    errors.append(f"components e operationalArtifacts deveriam compartilhar o mesmo sourceCommit, encontrados: {sorted(all_source_commits)}")

component_statuses = all_statuses

# overallPublicationStatus: nunca "published" quando so parte dos
# componentes foi publicada (idempotentemente ou nao) - a mentira mais
# perigosa que este manifesto poderia contar.
overall_status = manifest.get("overallPublicationStatus")
if overall_status not in ("pending", "partial", "published", "failed"):
    errors.append(f"overallPublicationStatus desconhecido: {overall_status!r}")
elif component_statuses:
    if any(s in ("conflict", "failed") for s in component_statuses):
        expected_overall = "failed"
    elif all(s in PUBLISHED_LIKE_STATUSES for s in component_statuses):
        expected_overall = "published"
    elif any(s in PUBLISHED_LIKE_STATUSES for s in component_statuses):
        expected_overall = "partial"
    else:
        expected_overall = "pending"

    if overall_status != expected_overall:
        errors.append(
            f"overallPublicationStatus={overall_status!r} nao corresponde ao estado real dos componentes "
            f"(esperado {expected_overall!r} dado {component_statuses})"
        )

# overallVerdict: agregado distinto de overallPublicationStatus - so
# "passed" quando a publicacao real terminou E nenhuma atestacao falhou e
# nenhuma ainda esta pendente; nunca fabricado como "passed" so porque a
# publicacao terminou.
overall_verdict = manifest.get("overallVerdict")
if overall_verdict not in ("pending", "passed", "failed"):
    errors.append(f"overallVerdict desconhecido: {overall_verdict!r}")
elif overall_status in ("pending", "partial", "published", "failed"):
    if overall_status == "failed" or any_attestation_failed:
        expected_verdict = "failed"
    elif overall_status == "published" and not any_attestation_unresolved:
        expected_verdict = "passed"
    else:
        expected_verdict = "pending"

    if overall_verdict != expected_verdict:
        errors.append(
            f"overallVerdict={overall_verdict!r} nao corresponde ao estado real "
            f"(esperado {expected_verdict!r} dado overallPublicationStatus={overall_status!r}, "
            f"atestacao_falhou={any_attestation_failed}, atestacao_pendente={any_attestation_unresolved})"
        )

    # Nunca aceitar um veredito "passed" com uma atestacao fabricada -
    # redundante com a checagem por componente acima, mas explicito aqui
    # porque e a consequencia mais perigosa possivel (release "aprovada"
    # sem evidencia real).
    if overall_verdict == "passed":
        for entry in components + operational_artifacts:
            for kind in ("provenanceAttestation", "sbomAttestation"):
                att = entry.get(kind) or {}
                if att.get("status") in ("generated", "verified") and not att.get("reference"):
                    errors.append(f"{entry.get('component')}: overallVerdict=passed mas {kind} nao tem reference real - isso seria uma atestacao fabricada")

if errors:
    print("Falhas de validacao do manifesto de release:", file=sys.stderr)
    for e in errors:
        print(f"  - {e}", file=sys.stderr)
    sys.exit(1)

print(f"Manifesto de release ({manifest_path}) validado com sucesso contra o HEAD {expected_head} - {len(components)} componentes de negocio + {len(operational_artifacts)} artefato(s) operacional(is), overallPublicationStatus={overall_status}, overallVerdict={overall_verdict}.")
PYEOF

if [ "$VALIDATION_EXIT" -ne 0 ]; then
  fail "validacao do manifesto de release reprovou - ver saida acima."
fi

log "=== Validacao do manifesto de release concluida com sucesso ==="
