#!/bin/sh
# Teste deterministico (sem "aws ecr get-lifecycle-policy-preview" real e
# sem conta AWS) da SELECAO de imagens pela lifecycle policy dos
# repositorios ECR (infra/terraform/environments/aws-reference/main.tf,
# local.ecr_lifecycle_policy), para garantir seguranca de lifecycle para
# deploy/rollback futuro.
#
# Reimplementa em Python, de forma equivalente as regras reais (selection
# por tagStatus/tagPrefixList/countType), a decisao de "esta tag/imagem
# seria candidata a expiracao por esta regra", e confirma:
#   - uma tag de release/promocao (ex.: "v1.2.3", "prod") NUNCA e
#     selecionada por nenhuma regra (prefixo diferente de "sha-");
#   - uma imagem SEM tag e sempre candidata a expiracao (rule 1);
#   - entre imagens COM a tag canonica "sha-", so as que excedem as
#     ultimas N mantidas (rule 2, imageCountMoreThan) sao candidatas -
#     rollback recente e imagem ativa (entre as N mais novas) NUNCA sao
#     selecionadas por esta regra;
#   - LIMITACAO CONHECIDA (documentada, nao resolvida neste bloco):
#     a regra e contada por PUSH, nao por uso real em ECS - uma imagem
#     alem das ultimas N seria candidata a expiracao mesmo se ainda
#     estiver ativamente implantada. Este teste confirma que o
#     comportamento ATUAL e exatamente esse (nao inventa uma
#     protecao por ECS que nao existe), para que a limitacao continue
#     visivel e rastreavel ate o bloco de deploy no ECS.
#
# Uso:
#   sh scripts/ci/test-lifecycle-policy-safety.sh
set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

PASS_COUNT=0
FAIL_COUNT=0
report_pass() { PASS_COUNT=$((PASS_COUNT + 1)); printf 'PASS: %s\n' "$1"; }
report_fail() { FAIL_COUNT=$((FAIL_COUNT + 1)); printf 'FAIL: %s\n' "$1" >&2; }

EVAL_EXIT=0
python3 <<'PYEOF' || EVAL_EXIT=$?
import json
import sys

KEEP_LAST_TAGGED = 20
UNTAGGED_EXPIRE_DAYS = 14

# Mesma estrutura de "local.ecr_lifecycle_policy" em
# infra/terraform/environments/aws-reference/main.tf - reimplementada aqui
# apenas para simular a SELECAO (nao a execucao real do ECR), ja que nao
# ha "aws ecr get-lifecycle-policy-preview" disponivel sem uma conta real.
RULES = [
    {
        "rulePriority": 1,
        "selection": {"tagStatus": "untagged", "countType": "sinceImagePushed", "countUnit": "days", "countNumber": UNTAGGED_EXPIRE_DAYS},
    },
    {
        "rulePriority": 2,
        "selection": {"tagStatus": "tagged", "tagPrefixList": ["sha-"], "countType": "imageCountMoreThan", "countNumber": KEEP_LAST_TAGGED},
    },
]


def is_selected_by_rule2(tag, position_from_newest, total_sha_tagged_images):
    """position_from_newest: 1 = imagem sha- mais recente. ECR aplica
    imageCountMoreThan mantendo as N mais recentes (por data de push) e
    selecionando o RESTANTE (mais antigas) para expiracao."""
    rule = RULES[1]["selection"]
    if not tag.startswith(tuple(rule["tagPrefixList"])):
        return False
    if total_sha_tagged_images <= rule["countNumber"]:
        return False
    return position_from_newest > rule["countNumber"]


def is_selected_by_rule1(tag):
    return tag is None


errors = []

# 1) Tag de release/promocao (prefixo diferente de "sha-") nunca e
#    selecionada por nenhuma regra, mesmo com centenas de imagens.
for release_tag in ["v1.2.3", "prod", "prod-2026-07-27", "latest"]:
    if is_selected_by_rule1(release_tag) or is_selected_by_rule2(release_tag, position_from_newest=999, total_sha_tagged_images=999):
        errors.append(f"tag de release/promocao '{release_tag}' NUNCA deveria ser selecionada por nenhuma regra de lifecycle")

# 2) Imagem sem tag e sempre candidata a expiracao (rule 1) - unica regra
#    que se aplica a ela.
if not is_selected_by_rule1(None):
    errors.append("imagem sem tag deveria ser candidata a expiracao pela rule 1 (untagged)")

# 3) Entre as tags "sha-", a mais recente (posicao 1) nunca e selecionada,
#    mesmo havendo muito mais que N imagens no repositorio (candidata a
#    deploy ativo).
if is_selected_by_rule2("sha-" + "1" * 40, position_from_newest=1, total_sha_tagged_images=50):
    errors.append("a imagem sha- mais recente NUNCA deveria ser candidata a expiracao (seria o deploy ativo mais provavel)")

# 4) Uma tag "sha-" dentro das ultimas N (candidata a rollback recente)
#    nunca e selecionada.
if is_selected_by_rule2("sha-" + "2" * 40, position_from_newest=KEEP_LAST_TAGGED, total_sha_tagged_images=50):
    errors.append(f"a imagem sha- na posicao {KEEP_LAST_TAGGED} (dentro do limite mantido) nao deveria ser candidata a expiracao")

# 5) Com poucas imagens no total (<= N), nenhuma "sha-" e candidata,
#    mesmo a mais antiga - a regra e "imageCountMoreThan", nunca
#    "sempre expira alem de X dias" para imagens COM tag.
if is_selected_by_rule2("sha-" + "3" * 40, position_from_newest=5, total_sha_tagged_images=5):
    errors.append("com total <= N (limite mantido), nenhuma imagem sha- deveria ser candidata a expiracao")

# 6) LIMITACAO CONHECIDA (documentada em aws-reference/main.tf e no ADR):
#    uma imagem sha- ALEM das ultimas N mantidas E selecionada por esta
#    regra mesmo que ainda esteja ativamente implantada no ECS - o ECR
#    nao tem conhecimento de referencias de task definition. Este teste
#    CONFIRMA o comportamento atual (nao finge que ja foi corrigido).
if not is_selected_by_rule2("sha-" + "4" * 40, position_from_newest=KEEP_LAST_TAGGED + 1, total_sha_tagged_images=50):
    errors.append(
        "LIMITACAO CONHECIDA nao reproduzida: a imagem sha- na posicao "
        f"{KEEP_LAST_TAGGED + 1} deveria ser candidata a expiracao sob a regra atual "
        "(contagem por push, sem conhecimento de uso real em ECS) - se isso mudou, "
        "atualize main.tf/ADR-0013 e este teste juntos."
    )

if errors:
    print("Falhas na simulacao de selecao da lifecycle policy:", file=sys.stderr)
    for e in errors:
        print(f"  - {e}", file=sys.stderr)
    sys.exit(1)

print("OK: simulacao deterministica da selecao de lifecycle policy (release/promocao protegida, untagged sempre candidata, sha- respeitando imageCountMoreThan, limitacao ECS documentada e confirmada).")
PYEOF

if [ "$EVAL_EXIT" -eq 0 ]; then
  report_pass "simulacao deterministica da lifecycle policy: release/promocao protegida, ativo/rollback recente preservados, limitacao de contagem-por-push confirmada"
else
  report_fail "simulacao deterministica da lifecycle policy reprovou - ver mensagens acima"
fi

# ---------------------------------------------------------------------
# Confirma que os NUMEROS usados na simulacao acima (KEEP_LAST_TAGGED=20,
# UNTAGGED_EXPIRE_DAYS=14) realmente correspondem aos defaults declarados
# em infra/terraform/environments/aws-reference/variables.tf - a
# simulacao nao deve divergir silenciosamente do Terraform real.
# ---------------------------------------------------------------------
VARIABLES_TF="infra/terraform/environments/aws-reference/variables.tf"
if grep -q 'variable "ecr_lifecycle_keep_last_tagged"' "$VARIABLES_TF" \
   && grep -A3 'variable "ecr_lifecycle_keep_last_tagged"' "$VARIABLES_TF" | grep -q 'default     = 20'; then
  report_pass "KEEP_LAST_TAGGED=20 usado na simulacao corresponde ao default real de ecr_lifecycle_keep_last_tagged"
else
  report_fail "default de ecr_lifecycle_keep_last_tagged mudou no Terraform - atualize esta simulacao (KEEP_LAST_TAGGED)"
fi

if grep -q 'variable "ecr_lifecycle_untagged_expire_days"' "$VARIABLES_TF" \
   && grep -A3 'variable "ecr_lifecycle_untagged_expire_days"' "$VARIABLES_TF" | grep -q 'default     = 14'; then
  report_pass "UNTAGGED_EXPIRE_DAYS=14 usado na simulacao corresponde ao default real de ecr_lifecycle_untagged_expire_days"
else
  report_fail "default de ecr_lifecycle_untagged_expire_days mudou no Terraform - atualize esta simulacao (UNTAGGED_EXPIRE_DAYS)"
fi

echo ""
echo "=== test-lifecycle-policy-safety: ${PASS_COUNT} passaram, ${FAIL_COUNT} falharam ==="
[ "$FAIL_COUNT" -eq 0 ]
