#!/bin/sh
# Wrapper host (Linux/Mac/CI) do bootstrap local de segurança.
# Não exige bash/openssl no host: toda a geração roda dentro de um container
# alpine fixado por digest. A lógica de geração vive só em
# bootstrap-local-security-impl.sh (nunca duplicada aqui ou no wrapper .ps1).
#
# Uso: sh scripts/security/bootstrap-local-security.sh [--force]
set -eu

BOOTSTRAP_IMAGE="alpine@sha256:1e42bbe2508154c9126d48c2b8a75420c3544343bf86fd041fb7527e017a4b4a"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# MSYS_NO_PATHCONV evita que o Git Bash (Windows) reescreva os caminhos
# absolutos passados ao docker.exe; é inofensivo em Linux/Mac/CI (ignorado).
MSYS_NO_PATHCONV=1 docker run --rm \
  -v "$REPO_ROOT":/workspace \
  -w /workspace \
  "$BOOTSTRAP_IMAGE" \
  sh -c "apk add --no-cache openssl >/dev/null && sh /workspace/scripts/security/bootstrap-local-security-impl.sh $*"
