#!/bin/sh
# Grava os valores reais dos secrets no AWS Secrets Manager
# (LocalStack), via PutSecretValue - reaproveitando as MESMAS senhas já
# geradas por bootstrap-local-security-impl.sh (.env), nunca duplicadas,
# nunca impressas. Roda DEPOIS do terraform apply (os secrets já existem
# como containers lógicos, sem valor) e ANTES das aplicações (que leem o
# valor no startup) - ver ADR-0009.
#
# Idempotente: PutSecretValue sempre sobrescreve a versão corrente do
# secret, então reexecutar este script (ou rodar após uma rotação de senha
# em bootstrap-local-security-impl.sh --force) atualiza o valor sem
# duplicar nada.
#
# Uso: sh secret-value-bootstrap-impl.sh
set -eu

: "${AWS_ENDPOINT_URL:?AWS_ENDPOINT_URL obrigatorio (ex.: http://localstack:4566)}"
REGION="${AWS_DEFAULT_REGION:-us-east-1}"
ERROR_LOG="$(mktemp)"

fail() {
  echo "secret-value-bootstrap: FALHA: $1" >&2
  rm -f "$ERROR_LOG"
  exit 1
}

put_secret() {
  name="$1"
  username="$2"
  password="$3"

  if aws --endpoint-url "$AWS_ENDPOINT_URL" --region "$REGION" secretsmanager put-secret-value \
    --secret-id "$name" \
    --secret-string "{\"username\":\"$username\",\"password\":\"$password\"}" \
    >/dev/null 2>"$ERROR_LOG"; then
    echo "secret-value-bootstrap: valor gravado/atualizado para '$name' (username='$username'). Nenhum valor de segredo foi impresso."
    return 0
  fi

  if grep -q "ResourceNotFoundException" "$ERROR_LOG" 2>/dev/null; then
    fail "secret '$name' não existe no Secrets Manager - execute 'terraform apply' (infra/terraform/environments/localstack-hobby) antes deste bootstrap."
  fi

  echo "secret-value-bootstrap: erro da AWS CLI ao gravar '$name' (mensagem de erro da API, sem valor de segredo):" >&2
  cat "$ERROR_LOG" >&2
  fail "put-secret-value falhou para '$name'."
}

put_secret "banco-carrefour/ledger-api/db-credentials" \
  "ledger_api" \
  "${LEDGER_API_PASSWORD:?LEDGER_API_PASSWORD obrigatorio}"

put_secret "banco-carrefour/ledger-outbox-publisher/db-credentials" \
  "ledger_outbox_publisher" \
  "${LEDGER_OUTBOX_PUBLISHER_PASSWORD:?LEDGER_OUTBOX_PUBLISHER_PASSWORD obrigatorio}"

put_secret "banco-carrefour/consolidation-api/db-credentials" \
  "consolidation_api_readonly" \
  "${CONSOLIDATION_API_READONLY_PASSWORD:?CONSOLIDATION_API_READONLY_PASSWORD obrigatorio}"

put_secret "banco-carrefour/consolidation-worker/db-credentials" \
  "consolidation_worker" \
  "${CONSOLIDATION_WORKER_PASSWORD:?CONSOLIDATION_WORKER_PASSWORD obrigatorio}"

rm -f "$ERROR_LOG"
echo "secret-value-bootstrap: concluído. Nenhum valor de segredo foi impresso em nenhuma etapa."
