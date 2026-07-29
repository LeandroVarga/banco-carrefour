#!/bin/sh
# Gera, de forma idempotente, os segredos e o certificado TLS locais.
# Roda dentro de um container com bash/openssl (nunca exigido no host).
# Uso: sh bootstrap-local-security-impl.sh [--force]
#
# Categoria "(a)" do plano: segredos que precisam existir ANTES de
# `docker compose up`, porque o Compose interpola ${VAR} a partir do .env no
# momento em que o comando é invocado — nenhum serviço do próprio Compose pode
# gerá-los.
set -eu

WORKSPACE="${WORKSPACE:-/workspace}"
ENV_FILE="$WORKSPACE/.env"
CERTS_DIR="$WORKSPACE/.local/security/certs"
FORCE=0

for arg in "$@"; do
  case "$arg" in
    --force) FORCE=1 ;;
  esac
done

fail() {
  echo "bootstrap-local-security: FALHA: $1" >&2
  exit 1
}

random_hex() {
  openssl rand -hex 24
}

protect_file() {
  # chmod 600 é o mecanismo efetivo em Linux/Mac/CI. Em host Windows via bind
  # mount, o wrapper .ps1 aplica ACL restrita depois que este script termina
  # (chmod dentro do container não reflete em ACL NTFS do host).
  file="$1"
  if ! chmod 600 "$file" 2>/dev/null; then
    fail "não foi possível restringir a permissão de $file (chmod 600 falhou) — abortando em vez de deixar o arquivo com permissão ampla."
  fi
}

chown_to_host() {
  # Este container roda como root contra um bind mount do repositório -
  # sem repassar o dono real para HOST_UID/HOST_GID (passados pelo wrapper
  # bootstrap-local-security.sh), os artefatos gerados ficariam root:root
  # no host Linux/CI, e um processo não-root do host (ex.: "docker compose
  # down" na limpeza de um runner hospedado) não conseguiria mais lê-los
  # (achado real: "permission denied"). Nunca falha o bootstrap se
  # HOST_UID/HOST_GID não estiverem definidos (execução direta fora do
  # wrapper, ou plataforma sem UID/GID POSIX aplicável - ex.: Windows, que
  # usa bootstrap-local-security.ps1 e nunca define essas variáveis).
  path="$1"
  recursive="${2:-0}"
  if [ -z "${HOST_UID:-}" ] || [ -z "${HOST_GID:-}" ]; then
    return 0
  fi
  if [ "$recursive" -eq 1 ]; then
    chown -R "$HOST_UID:$HOST_GID" "$path" 2>/dev/null \
      || echo "bootstrap-local-security: aviso - não foi possível aplicar ownership de host (uid=$HOST_UID/gid=$HOST_GID) a $path." >&2
  else
    chown "$HOST_UID:$HOST_GID" "$path" 2>/dev/null \
      || echo "bootstrap-local-security: aviso - não foi possível aplicar ownership de host (uid=$HOST_UID/gid=$HOST_GID) a $path." >&2
  fi
}

if [ -f "$ENV_FILE" ] && [ "$FORCE" -ne 1 ]; then
  echo "bootstrap-local-security: $ENV_FILE já existe — nada a fazer (use --force para rotacionar)."
else
  echo "bootstrap-local-security: gerando $ENV_FILE ($( [ "$FORCE" -eq 1 ] && echo 'rotação --force' || echo 'primeira execução'))..."

  cat > "$ENV_FILE" <<EOF
# Gerado por scripts/security/bootstrap-local-security-impl.sh — NÃO versionar.
KEYCLOAK_DB_PASSWORD=$(random_hex)
KC_BOOTSTRAP_ADMIN_CLIENT_ID=bootstrap-$(random_hex | cut -c1-12)
KC_BOOTSTRAP_ADMIN_CLIENT_SECRET=$(random_hex)
LEDGER_MIGRATION_PASSWORD=$(random_hex)
LEDGER_API_PASSWORD=$(random_hex)
LEDGER_OUTBOX_PUBLISHER_PASSWORD=$(random_hex)
CONSOLIDATION_MIGRATION_PASSWORD=$(random_hex)
CONSOLIDATION_WORKER_PASSWORD=$(random_hex)
CONSOLIDATION_API_READONLY_PASSWORD=$(random_hex)
EOF
  protect_file "$ENV_FILE"
  echo "bootstrap-local-security: $ENV_FILE gerado e protegido (chmod 600)."
fi
# Sempre reaplica o ownership de host, mesmo quando o arquivo já existia
# (autocura um .env root:root residual de uma execução anterior a esta
# correção) - nunca refaz o chmod 600, que já é garantido por protect_file
# ou por uma execução anterior desta mesma correção.
chown_to_host "$ENV_FILE"

mkdir -p "$CERTS_DIR"
CA_KEY="$CERTS_DIR/ca.key"
CA_CRT="$CERTS_DIR/ca.crt"
EDGE_KEY="$CERTS_DIR/edge.key"
EDGE_CRT="$CERTS_DIR/edge.crt"

if [ -f "$CA_KEY" ] && [ -f "$EDGE_CRT" ] && [ "$FORCE" -ne 1 ]; then
  echo "bootstrap-local-security: CA/certificado já existem em $CERTS_DIR — nada a fazer (use --force para regerar)."
else
  echo "bootstrap-local-security: gerando CA local e certificado do edge-proxy..."

  openssl genrsa -out "$CA_KEY" 4096 2>/dev/null
  openssl req -x509 -new -nodes -key "$CA_KEY" -sha256 -days 825 \
    -subj "/O=banco-carrefour local/CN=banco-carrefour Local Root CA" \
    -out "$CA_CRT" 2>/dev/null

  openssl genrsa -out "$EDGE_KEY" 2048 2>/dev/null

  SAN_CONF="$CERTS_DIR/edge-san.cnf"
  cat > "$SAN_CONF" <<EOF
[req]
distinguished_name = req_distinguished_name
req_extensions = v3_req
prompt = no
[req_distinguished_name]
CN = localhost
[v3_req]
subjectAltName = @alt_names
[alt_names]
DNS.1 = localhost
DNS.2 = keycloak.localhost
IP.1 = 127.0.0.1
EOF

  openssl req -new -key "$EDGE_KEY" -out "$CERTS_DIR/edge.csr" -config "$SAN_CONF" 2>/dev/null
  openssl x509 -req -in "$CERTS_DIR/edge.csr" -CA "$CA_CRT" -CAkey "$CA_KEY" -CAcreateserial \
    -out "$EDGE_CRT" -days 825 -sha256 -extfile "$SAN_CONF" -extensions v3_req 2>/dev/null
  rm -f "$CERTS_DIR/edge.csr" "$CERTS_DIR/ca.srl"

  protect_file "$CA_KEY"
  # edge.key: leitura restrita ao owner (root, quem gerou) e ao grupo 101
  # (uid/gid do processo nginx na imagem owasp/modsecurity-crs) — nunca
  # world-readable. O entrypoint do edge-proxy copia este arquivo para um
  # caminho interno do container, já com ownership do próprio processo
  # nginx e chmod 600 (ver infra/edge-proxy/95-stage-tls-key.sh); este mount
  # somente-leitura em modo 640/grupo-101 é só o transporte, nunca o caminho
  # de disco efetivamente servido pelo nginx.
  chmod 640 "$EDGE_KEY"
  chgrp 101 "$EDGE_KEY" 2>/dev/null || echo "bootstrap-local-security: aviso — não foi possível aplicar grupo 101 a $EDGE_KEY; o edge-proxy pode falhar ao ler a chave." >&2
  chmod 644 "$CA_CRT" "$EDGE_CRT"
  echo "bootstrap-local-security: CA e certificado gerados em $CERTS_DIR (SAN: localhost, keycloak.localhost, 127.0.0.1)."
fi

# Ownership de host para todo o diretório de segurança gerado (autocura
# execuções anteriores a esta correção também) - reaplicado
# incondicionalmente, depois restaurado o caso especial de edge.key: um
# "chown -R" recursivo trocaria seu grupo para HOST_GID, quebrando a
# leitura pelo processo nginx (grupo 101) dentro do container do
# edge-proxy - nunca group=host para esse arquivo especificamente.
chown_to_host "$WORKSPACE/.local" 0
chown_to_host "$WORKSPACE/.local/security" 0
chown_to_host "$CERTS_DIR" 1
if [ -f "$EDGE_KEY" ]; then
  chmod 640 "$EDGE_KEY"
  chgrp 101 "$EDGE_KEY" 2>/dev/null || echo "bootstrap-local-security: aviso — não foi possível reaplicar grupo 101 a $EDGE_KEY após o ownership de host; o edge-proxy pode falhar ao ler a chave." >&2
fi

echo "bootstrap-local-security: concluído. Nenhum valor de segredo foi impresso."
