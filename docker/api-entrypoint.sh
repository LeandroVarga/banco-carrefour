#!/bin/sh
# Entrypoint compartilhado por Ledger.Api e Consolidation.Api:
# instala a CA local (se montada), valida o trust store, troca
# explicitamente para o usuário não privilegiado da imagem base
# (mcr.microsoft.com/dotnet/aspnet:8.0 já traz o usuário "app") e só então
# executa o processo real via exec (setpriv substitui o processo, preserva
# sinais corretamente — equivalente ao padrão gosu/su-exec). As APIs nunca
# rodam como root em runtime.
set -eu

CA_SRC="/run/secrets/ca/ca.crt"
CA_DEST="/usr/local/share/ca-certificates/banco-carrefour-local-ca.crt"

if [ -f "$CA_SRC" ]; then
  cp "$CA_SRC" "$CA_DEST"
  update-ca-certificates >/dev/null
  if ! openssl verify -CAfile "$CA_SRC" "$CA_SRC" >/dev/null 2>&1; then
    echo "api-entrypoint.sh: aviso — não foi possível autovalidar a CA montada (prosseguindo mesmo assim)." >&2
  fi
fi

exec setpriv --reuid=app --regid=app --init-groups dotnet "$@"
