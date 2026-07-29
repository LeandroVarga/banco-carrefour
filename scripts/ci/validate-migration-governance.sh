#!/bin/sh
# Governanca de migracao expand-and-contract. Verifica TODAS as migrations
# reais do EF Core (Ledger.Infrastructure/Migrations,
# Consolidation.Infrastructure/Migrations) contra metadata REAL - o
# atributo [MigrationPhase(...)] (src/BancoCarrefour.Contracts/Migrations) -
# nunca infere seguranca a partir do nome do arquivo ou de um comentario em
# texto livre (mecanismo anterior, substituido nesta auditoria).
#
# Regras:
#   1. toda migration real precisa declarar exatamente um [MigrationPhase(...)].
#   2. uma migration cujo Up() contem operacao destrutiva
#      (DropColumn/DropTable/RenameColumn/RenameTable/AlterColumn NOT NULL)
#      só é aceita se declarada MigrationPhase.Contract - nunca Expand.
#   3. uma migration aditiva (sem operacao destrutiva em Up()) precisa ser
#      MigrationPhase.Expand - nunca Contract (contract "vazio" seria
#      metadata incorreta) e nunca Backfill (backfill real nunca e uma
#      migration EF Core neste repositorio - ver IBackfillTask/BackfillCommand
#      em src/Migrations/MigrationRunner).
#
# Uso:
#   sh scripts/ci/validate-migration-governance.sh
set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

fail() { echo "validate-migration-governance: FALHA: $1" >&2; exit 1; }
log() { printf '%s\n' "$1"; }

# Escopado explicitamente às duas pastas de migrations EF Core reais
# (nunca um "find -name Migrations" genérico em src/ - isso também
# encontraria src/BancoCarrefour.Contracts/Migrations, que contém apenas o
# enum/atributo MigrationPhase, nenhuma classe Migration real).
MIGRATION_FILES=$(find \
    src/Ledger/Ledger.Infrastructure/Migrations \
    src/Consolidation/Consolidation.Infrastructure/Migrations \
    -maxdepth 1 -name '*.cs' \
  | grep -v '\.Designer\.cs$' \
  | grep -v 'ModelSnapshot\.cs$' \
  | sort)

if [ -z "$MIGRATION_FILES" ]; then
  fail "nenhum arquivo de migration encontrado em src/*/Migrations - esperado pelo menos as migrations iniciais de Ledger e Consolidation."
fi

DESTRUCTIVE_PATTERN='DropColumn\(|DropTable\(|RenameColumn\(|RenameTable\('

violations=0

for f in $MIGRATION_FILES; do
  phase=""
  if grep -Eq '\[MigrationPhase\(MigrationPhase\.Expand\)\]' "$f"; then
    phase="Expand"
  elif grep -Eq '\[MigrationPhase\(MigrationPhase\.Contract\)\]' "$f"; then
    phase="Contract"
  elif grep -Eq '\[MigrationPhase\(MigrationPhase\.Backfill\)\]' "$f"; then
    phase="Backfill"
  fi

  if [ -z "$phase" ]; then
    echo "  - $f nao declara [MigrationPhase(...)] - toda migration real precisa de metadata explicita de fase." >&2
    violations=$((violations + 1))
    continue
  fi

  if [ "$phase" = "Backfill" ]; then
    echo "  - $f declara MigrationPhase.Backfill - nunca valido para uma migration EF Core (backfill real usa IBackfillTask/BackfillCommand, nunca uma Migration)." >&2
    violations=$((violations + 1))
    continue
  fi

  # Escopo somente ao corpo de Up() - Down() de qualquer migration aditiva
  # normal (CreateTable/AddColumn) legitimamente contem DropTable/DropColumn
  # para revertê-la; isso nunca é o problema que esta checagem visa
  # capturar (o risco real é uma revisão antiga, ainda em execução durante
  # canary/rolling, quebrar contra o schema que Up() aplicou - Down() só
  # roda num rollback explícito da própria migration).
  up_body=$(awk '/protected override void Up\(/{flag=1} flag{print} /protected override void Down\(/{exit}' "$f")

  is_destructive=0
  if printf '%s' "$up_body" | grep -Eq "$DESTRUCTIVE_PATTERN"; then
    is_destructive=1
  fi
  if printf '%s' "$up_body" | grep -Eq 'AlterColumn' && printf '%s' "$up_body" | grep -Eq 'nullable:[[:space:]]*false'; then
    is_destructive=1
  fi

  if [ "$is_destructive" = "1" ] && [ "$phase" != "Contract" ]; then
    echo "  - $f contem operacao destrutiva em Up() mas declara MigrationPhase.$phase (deveria ser Contract)." >&2
    violations=$((violations + 1))
    continue
  fi

  if [ "$is_destructive" = "0" ] && [ "$phase" != "Expand" ]; then
    echo "  - $f nao contem operacao destrutiva em Up() mas declara MigrationPhase.$phase (deveria ser Expand)." >&2
    violations=$((violations + 1))
    continue
  fi

  log "OK (fase $phase, metadata real consistente com o conteudo de Up()): $f"
done

if [ "$violations" -gt 0 ]; then
  fail "$violations migration(oes) com metadata de fase ausente ou inconsistente com o conteudo real de Up()."
fi

log "validate-migration-governance: todas as migrations reais tem [MigrationPhase(...)] consistente com o conteudo real de Up()."
