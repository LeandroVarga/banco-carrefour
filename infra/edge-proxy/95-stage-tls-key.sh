#!/bin/sh
# Copia a chave privada TLS de um mount somente-leitura para o filesystem
# interno do container, com ownership do próprio processo nginx (uid 101
# nesta imagem) e permissão 600 — nunca serve a chave diretamente do mount
# de origem. Não há chave temporária residual: este é o único destino, e é
# sobrescrito a cada start do container (nunca persistido em volume).
set -eu

SRC="/run/edge-secrets/server.key"
DEST="${SSL_CERT_KEY_FILE:-/etc/nginx/conf/server.key}"

if [ ! -f "$SRC" ]; then
  echo "95-stage-tls-key.sh: $SRC não encontrado — pulando (verifique o bootstrap de certificados)." >&2
  exit 0
fi

cp "$SRC" "$DEST"
chmod 600 "$DEST"
echo "95-stage-tls-key.sh: chave TLS preparada em $DEST (600, owner=$(id -un))." >&2
