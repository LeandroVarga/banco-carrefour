#!/bin/sh
# keycloak-bootstrap: configura o realm banco-carrefour de forma idempotente.
#
# Primeira execução: autentica com o service account de bootstrap TEMPORÁRIO
# (KC_BOOTSTRAP_ADMIN_CLIENT_ID/SECRET, criado por `kc.sh bootstrap-admin
# service` só porque o master realm ainda não existia) -> cria o client
# persistente banco-carrefour-realm-configurator com privilegio minimo
# (roles de gestao escopadas ao realm banco-carrefour, nunca master) -> grava
# os secrets necessarios em .local/security/.env.security -> EXCLUI o
# service account de bootstrap temporario.
#
# Execucoes seguintes: le .local/security/.env.security (se existir) e
# autentica como banco-carrefour-realm-configurator para reconciliar a
# configuracao — nunca usa o bootstrap temporario de novo (nao pode: so
# existe antes do master realm ser criado).
#
# Nunca imprime valores de secret.
set -eu

KC_BASE="${KC_BASE:-http://keycloak:8080}"
REALM="banco-carrefour"
SECRETS_FILE="/local-security/.env.security"

fail() { echo "keycloak-bootstrap: FALHA: $1" >&2; exit 1; }

wait_for_keycloak() {
  # KC_HEALTH_ENABLED expoe /health/ready na interface de management (9000),
  # nao na porta de aplicacao (8080/$KC_BASE).
  health_url="$(echo "$KC_BASE" | sed -E 's#:[0-9]+$##'):9000/health/ready"
  echo "keycloak-bootstrap: aguardando $health_url ficar saudável..." >&2
  i=0
  while [ "$i" -lt 60 ]; do
    if curl -fsS "$health_url" >/dev/null 2>&1; then
      echo "keycloak-bootstrap: Keycloak saudável." >&2
      return 0
    fi
    i=$((i + 1))
    sleep 2
  done
  fail "Keycloak não ficou saudável a tempo."
}

get_token() {
  client_id="$1"; client_secret="$2"; realm="$3"
  resp=$(curl -fsS -X POST "$KC_BASE/realms/$realm/protocol/openid-connect/token" \
    -H "Content-Type: application/x-www-form-urlencoded" \
    -d "grant_type=client_credentials" -d "client_id=$client_id" -d "client_secret=$client_secret") \
    || fail "não foi possível obter token para $client_id"
  printf '%s' "$resp" | jq -r '.access_token'
}

# Variante não-fatal: usada para TESTAR se um secret já conhecido ainda é
# válido (ex.: .env.security sobreviveu a um `docker compose down -v` que
# recriou o banco do Keycloak). Nunca aborta o script via `fail`.
try_get_token() {
  client_id="$1"; client_secret="$2"; realm="$3"
  resp=$(curl -fsS -X POST "$KC_BASE/realms/$realm/protocol/openid-connect/token" \
    -H "Content-Type: application/x-www-form-urlencoded" \
    -d "grant_type=client_credentials" -d "client_id=$client_id" -d "client_secret=$client_secret" 2>/dev/null) || return 1
  token=$(printf '%s' "$resp" | jq -r '.access_token // empty')
  [ -n "$token" ] || return 1
  printf '%s' "$token"
}

kc_api() {
  method="$1"; path="$2"; token="$3"; body="${4:-}"
  if [ -n "$body" ]; then
    curl -fsS -X "$method" "$KC_BASE$path" \
      -H "Authorization: Bearer $token" -H "Content-Type: application/json" -d "$body"
  else
    curl -fsS -X "$method" "$KC_BASE$path" -H "Authorization: Bearer $token"
  fi
}

get_client_uuid() {
  token="$1"; client_id="$2"
  resp=$(kc_api GET "/admin/realms/$REALM/clients?clientId=$client_id" "$token") || return 1
  printf '%s' "$resp" | jq -r '.[0].id // empty'
}

ensure_realm_configurator() {
  admin_token="$1"

  uuid=$(get_client_uuid "$admin_token" "banco-carrefour-realm-configurator" || true)
  if [ -z "$uuid" ]; then
    echo "keycloak-bootstrap: criando banco-carrefour-realm-configurator..." >&2
    kc_api POST "/admin/realms/$REALM/clients" "$admin_token" \
      '{"clientId":"banco-carrefour-realm-configurator","publicClient":false,"protocol":"openid-connect","serviceAccountsEnabled":true,"standardFlowEnabled":false,"directAccessGrantsEnabled":false}' >/dev/null
    uuid=$(get_client_uuid "$admin_token" "banco-carrefour-realm-configurator")
  fi
  [ -n "$uuid" ] || fail "não foi possível criar/localizar banco-carrefour-realm-configurator"

  sa_user_id=$(kc_api GET "/admin/realms/$REALM/clients/$uuid/service-account-user" "$admin_token" | jq -r '.id')
  rm_uuid=$(get_client_uuid "$admin_token" "realm-management")
  [ -n "$rm_uuid" ] || fail "client realm-management não encontrado no realm $REALM"

  roles_json=$(kc_api GET "/admin/realms/$REALM/clients/$rm_uuid/roles" "$admin_token")
  wanted="manage-clients view-realm"
  assign_payload=$(printf '%s' "$roles_json" | jq -c --arg wanted "$wanted" \
    '[.[] | select(.name as $n | ($wanted | split(" ") | index($n)))]')

  if [ "$(printf '%s' "$assign_payload" | jq 'length')" -gt 0 ]; then
    kc_api POST "/admin/realms/$REALM/users/$sa_user_id/role-mappings/clients/$rm_uuid" "$admin_token" \
      "$assign_payload" >/dev/null 2>&1 || echo "keycloak-bootstrap: aviso — role-mapping pode já estar aplicado." >&2
  fi

  # secret do realm-configurator: obtem o existente; so gera novo se ainda nao houver
  secret=$(kc_api GET "/admin/realms/$REALM/clients/$uuid/client-secret" "$admin_token" | jq -r '.value // empty')
  if [ -z "$secret" ]; then
    secret=$(kc_api POST "/admin/realms/$REALM/clients/$uuid/client-secret" "$admin_token" "" | jq -r '.value // empty')
  fi
  [ -n "$secret" ] || fail "não foi possível obter/gerar o secret de banco-carrefour-realm-configurator"
  printf '%s' "$secret"
}

get_or_create_client_secret() {
  admin_token="$1"; client_id="$2"
  uuid=$(get_client_uuid "$admin_token" "$client_id") || fail "client $client_id não encontrado (import do realm falhou/parcial)"
  [ -n "$uuid" ] || fail "client $client_id não encontrado (import do realm falhou/parcial)"
  secret=$(kc_api GET "/admin/realms/$REALM/clients/$uuid/client-secret" "$admin_token" | jq -r '.value // empty')
  if [ -z "$secret" ]; then
    secret=$(kc_api POST "/admin/realms/$REALM/clients/$uuid/client-secret" "$admin_token" "" | jq -r '.value // empty')
  fi
  [ -n "$secret" ] || fail "não foi possível obter/gerar o secret de $client_id"
  printf '%s' "$secret"
}

delete_bootstrap_admin() {
  admin_token="$1"
  resp=$(kc_api GET "/admin/realms/master/clients?clientId=$KC_BOOTSTRAP_ADMIN_CLIENT_ID" "$admin_token") || true
  uuid=$(printf '%s' "${resp:-}" | jq -r '.[0].id // empty' 2>/dev/null || true)
  if [ -n "$uuid" ]; then
    kc_api DELETE "/admin/realms/master/clients/$uuid" "$admin_token" >/dev/null 2>&1 || true
    echo "keycloak-bootstrap: service account de bootstrap temporário excluído." >&2
  else
    echo "keycloak-bootstrap: service account de bootstrap temporário já não existe (execução idempotente)." >&2
  fi
}

write_secrets_file() {
  configurator_secret="$1"; merchant_a_secret="$2"; merchant_b_secret="$3"
  umask 077
  cat > "$SECRETS_FILE" <<EOF
# Gerado por scripts/security/keycloak-bootstrap.sh — NÃO versionar.
REALM_CONFIGURATOR_CLIENT_SECRET=$configurator_secret
MERCHANT_A_TEST_CLIENT_SECRET=$merchant_a_secret
MERCHANT_B_TEST_CLIENT_SECRET=$merchant_b_secret
EOF
  chmod 600 "$SECRETS_FILE"
}

wait_for_keycloak

mkdir -p "$(dirname "$SECRETS_FILE")"

# O modo NÃO é decidido apenas pela existência de .env.security: um
# `docker compose down -v` recria o banco do Keycloak (master realm e o
# client de bootstrap voltam a não existir) mas preserva esse arquivo local
# — um secret de realm-configurator obsoleto não pode ser tratado como
# válido só porque o arquivo está presente.
admin_token=""
if [ -f "$SECRETS_FILE" ]; then
  # shellcheck disable=SC1090
  . "$SECRETS_FILE"
  if admin_token=$(try_get_token "banco-carrefour-realm-configurator" "${REALM_CONFIGURATOR_CLIENT_SECRET:-}" "$REALM"); then
    echo "keycloak-bootstrap: secret existente ainda válido — reconciliando com banco-carrefour-realm-configurator." >&2
    ensure_realm_configurator "$admin_token" >/dev/null
    merchant_a_secret=$(get_or_create_client_secret "$admin_token" "merchant-a-test-client")
    merchant_b_secret=$(get_or_create_client_secret "$admin_token" "merchant-b-test-client")
    echo "keycloak-bootstrap: reconciliação concluída." >&2
  else
    echo "keycloak-bootstrap: .env.security presente, mas o secret não autentica (banco do Keycloak provavelmente recriado) — tratando como nova instância." >&2
    admin_token=""
  fi
fi

if [ -z "$admin_token" ]; then
  echo "keycloak-bootstrap: autenticando com o service account de bootstrap temporário..." >&2
  [ -n "${KC_BOOTSTRAP_ADMIN_CLIENT_ID:-}" ] || fail "KC_BOOTSTRAP_ADMIN_CLIENT_ID ausente."
  [ -n "${KC_BOOTSTRAP_ADMIN_CLIENT_SECRET:-}" ] || fail "KC_BOOTSTRAP_ADMIN_CLIENT_SECRET ausente."

  admin_token=$(get_token "$KC_BOOTSTRAP_ADMIN_CLIENT_ID" "$KC_BOOTSTRAP_ADMIN_CLIENT_SECRET" "master") \
    || fail "bootstrap temporário indisponível e banco-carrefour-realm-configurator não autentica — recuperação manual necessária (ver runbook: kc.sh bootstrap-admin com os nós parados)."

  configurator_secret=$(ensure_realm_configurator "$admin_token")
  merchant_a_secret=$(get_or_create_client_secret "$admin_token" "merchant-a-test-client")
  merchant_b_secret=$(get_or_create_client_secret "$admin_token" "merchant-b-test-client")

  # substitui o arquivo de forma segura: escreve em um temporário no mesmo
  # diretório e só then troca (mv é atômico no mesmo filesystem), nunca
  # deixando um .env.security parcialmente escrito em caso de falha.
  tmp_secrets_file="$SECRETS_FILE.tmp.$$"
  SECRETS_FILE="$tmp_secrets_file" write_secrets_file "$configurator_secret" "$merchant_a_secret" "$merchant_b_secret"
  mv -f "$tmp_secrets_file" "/local-security/.env.security"

  delete_bootstrap_admin "$admin_token"
  echo "keycloak-bootstrap: inicialização (ou recuperação) concluída." >&2
fi

echo "keycloak-bootstrap: concluído. Nenhum valor de segredo foi impresso." >&2
