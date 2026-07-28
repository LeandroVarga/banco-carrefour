#!/bin/sh
# Gera credenciais efemeras para uma execucao de
# docker-compose.release-qualification.yml (ver ADR-0013) - cada valor e
# aleatorio, gerado nesta execucao, e nunca reaproveitado entre execucoes
# ou commitado. Imprime em
# formato "KEY=VALUE" (uma por linha) para ser anexado a $GITHUB_ENV pelo
# workflow chamador.
#
# Uso:
#   sh scripts/release/generate-ephemeral-environment-secrets.sh >> "$GITHUB_ENV"
set -eu

random_hex() {
  # 32 bytes de /dev/urandom, hex - disponivel em qualquer runner Linux
  # hospedado (e na maioria das distros usadas localmente); evita
  # depender de "openssl" estar instalado.
  od -An -N32 -tx1 /dev/urandom | tr -d ' \n'
}

for var in \
  LEDGER_MIGRATION_PASSWORD \
  LEDGER_API_PASSWORD \
  LEDGER_OUTBOX_PUBLISHER_PASSWORD \
  CONSOLIDATION_MIGRATION_PASSWORD \
  CONSOLIDATION_WORKER_PASSWORD \
  CONSOLIDATION_API_READONLY_PASSWORD \
  KEYCLOAK_DB_PASSWORD \
  KC_BOOTSTRAP_ADMIN_CLIENT_SECRET \
; do
  printf '%s=%s\n' "$var" "$(random_hex)"
done

# KC_BOOTSTRAP_ADMIN_CLIENT_ID nao precisa ser secreto - e o identificador
# do admin de bootstrap do PROPRIO Keycloak ("kc.sh bootstrap-admin
# client"), criado no primeiro start do container, nao um client do realm
# importado. Qualquer valor serve, desde que o mesmo valor seja usado pelos
# servicos "keycloak" e "keycloak-bootstrap" nesta MESMA execucao efemera.
printf 'KC_BOOTSTRAP_ADMIN_CLIENT_ID=%s\n' "ephemeral-promotion-admin"
