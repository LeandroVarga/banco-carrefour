#!/bin/sh
# Confirma, de forma estatica e sem chamar a AWS, que o evento/ref esta
# autorizado e que as variaveis de repositorio obrigatorias para o
# workflow de publicacao existem e tem um formato valido - falha ANTES de
# qualquer tentativa de autenticacao. Nunca usa uma
# conta/regiao real como fallback silencioso quando uma variavel esta
# ausente, e nunca depende de a AWS rejeitar a role depois de todo o
# trabalho local (build/SBOM/scan) ja ter rodado - ver ADR-0013.
#
# a trust policy OIDC (infra/terraform/environments/aws-reference)
# restringe a role de publicacao a
# "repo:<owner>/<repo>:ref:refs/heads/main" - um disparo manual
# (workflow_dispatch) em qualquer OUTRO ref falharia de qualquer forma ao
# tentar assumir a role via OIDC, mas so DEPOIS de já ter gasto build de
# imagem, SBOM e scan. Este preflight rejeita esse cenario ANTES de
# qualquer trabalho caro ser feito.
#
# Variaveis usadas para autorizar o evento/ref (fornecidas automaticamente
# pelo GitHub Actions - GITHUB_EVENT_NAME e GITHUB_REF; opcionais para uso
# local/manual, onde a checagem de ref e simplesmente pulada se ausentes):
#   GITHUB_EVENT_NAME   ex.: push | workflow_dispatch
#   GITHUB_REF          ex.: refs/heads/main
#
# Variaveis obrigatorias (nao sensiveis - apenas identificadores):
#   AWS_REGION               ex.: us-east-1
#   ECR_PUBLISHER_ROLE_ARN   ex.: arn:aws:iam::123456789012:role/banco-carrefour-ecr-publisher
#   ECR_REGISTRY             ex.: 123456789012.dkr.ecr.us-east-1.amazonaws.com
#
# Uso:
#   sh scripts/ci/check-release-prerequisites.sh
set -eu

fail() { echo "check-release-prerequisites: FALHA: $1" >&2; exit 1; }

AUTHORIZED_REF="refs/heads/main"

# --- 1) Evento/ref autorizado (antes de qualquer trabalho caro) ---
if [ -n "${GITHUB_EVENT_NAME:-}" ] && [ -n "${GITHUB_REF:-}" ]; then
  if [ "$GITHUB_REF" != "$AUTHORIZED_REF" ]; then
    fail "evento '${GITHUB_EVENT_NAME}' disparado no ref '${GITHUB_REF}', mas a role de publicacao (trust policy OIDC) so autoriza '${AUTHORIZED_REF}'. Rejeitado ANTES do build/SBOM/scan/autenticacao AWS - nunca dependemos apenas da AWS recusar a role depois de todo o trabalho local ja ter rodado."
  fi
  echo "check-release-prerequisites: evento '${GITHUB_EVENT_NAME}' no ref autorizado '${GITHUB_REF}'."
else
  echo "check-release-prerequisites: GITHUB_EVENT_NAME/GITHUB_REF nao fornecidos (execucao local/manual) - checagem de ref pulada."
fi

# --- 2) Variaveis de repositorio obrigatorias ---
MISSING=""
[ -n "${AWS_REGION:-}" ] || MISSING="${MISSING} AWS_REGION"
[ -n "${ECR_PUBLISHER_ROLE_ARN:-}" ] || MISSING="${MISSING} ECR_PUBLISHER_ROLE_ARN"
[ -n "${ECR_REGISTRY:-}" ] || MISSING="${MISSING} ECR_REGISTRY"

if [ -n "$MISSING" ]; then
  fail "variavel(is) de repositorio ausente(s):${MISSING}. Configure em Settings > Secrets and variables > Actions > Variables antes de disparar este workflow. Nenhuma conta/regiao real e usada como fallback."
fi

# --- 3) Formato estatico das variaveis - nunca chama a AWS so para testar
# um valor malformado (isso exigiria credenciais e uma tentativa real de
# AssumeRoleWithWebIdentity). ---
case "$AWS_REGION" in
  [a-z][a-z]-[a-z]*-[0-9]) : ;;
  *) fail "AWS_REGION nao tem o formato esperado '<continente>-<direcao>-<numero>' (ex.: us-east-1): '${AWS_REGION}'." ;;
esac

case "$ECR_PUBLISHER_ROLE_ARN" in
  arn:aws:iam::[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]:role/*) : ;;
  *) fail "ECR_PUBLISHER_ROLE_ARN nao tem o formato esperado 'arn:aws:iam::<12 digitos>:role/<nome>': '${ECR_PUBLISHER_ROLE_ARN}'." ;;
esac

case "$ECR_REGISTRY" in
  [0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9].dkr.ecr.*.amazonaws.com) : ;;
  *) fail "ECR_REGISTRY nao tem o formato esperado '<12 digitos>.dkr.ecr.<regiao>.amazonaws.com': '${ECR_REGISTRY}'." ;;
esac

echo "check-release-prerequisites: variaveis de repositorio presentes e com formato valido (AWS_REGION=${AWS_REGION})."
