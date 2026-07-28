-- Cria (se ausentes) as roles de menor privilégio do Consolidation e
-- sincroniza suas senhas com o valor atual do .env em toda execução
-- (idempotente e compatível com rotação). Roda ANTES das migrations,
-- conectado como o owner atual (consolidation) — ver docs/decisions/ADR-0009.
\set ON_ERROR_STOP on

DO $$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'consolidation_migration') THEN
    CREATE ROLE consolidation_migration LOGIN;
  END IF;
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'consolidation_worker') THEN
    CREATE ROLE consolidation_worker LOGIN;
  END IF;
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'consolidation_api_readonly') THEN
    CREATE ROLE consolidation_api_readonly LOGIN;
  END IF;
END
$$;

ALTER ROLE consolidation_migration PASSWORD :'consolidation_migration_password';
ALTER ROLE consolidation_worker PASSWORD :'consolidation_worker_password';
ALTER ROLE consolidation_api_readonly PASSWORD :'consolidation_api_readonly_password';

-- Transferência explícita de ownership (nunca GRANT de papel amplo): em
-- volume novo, os ALTER TABLE IF EXISTS abaixo são no-op; em volume
-- existente, move as tabelas já criadas por "consolidation" para o novo
-- owner. Não se usa REASSIGN OWNED BY — ver comentário equivalente em
-- infra/postgres/ledger/db-role-bootstrap.sql (Postgres recusa reatribuir
-- as cópias locais dos catálogos do sistema, confirmado por execução real).
ALTER DATABASE consolidation OWNER TO consolidation_migration;
ALTER SCHEMA public OWNER TO consolidation_migration;
ALTER TABLE IF EXISTS "__EFMigrationsHistory" OWNER TO consolidation_migration;
ALTER TABLE IF EXISTS daily_balances OWNER TO consolidation_migration;
ALTER TABLE IF EXISTS processed_events OWNER TO consolidation_migration;

REVOKE ALL ON SCHEMA public FROM PUBLIC;
REVOKE ALL ON DATABASE consolidation FROM PUBLIC;

GRANT CONNECT ON DATABASE consolidation TO consolidation_migration, consolidation_worker, consolidation_api_readonly;
GRANT USAGE ON SCHEMA public TO consolidation_migration, consolidation_worker, consolidation_api_readonly;
