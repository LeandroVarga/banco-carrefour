-- Cria (se ausentes) as roles de menor privilégio do Ledger e sincroniza
-- suas senhas com o valor atual do .env em toda execução (idempotente e
-- compatível com rotação). Roda ANTES das migrations, conectado como o
-- owner atual (ledger) — ver docs/decisions/ADR-0009.
\set ON_ERROR_STOP on

DO $$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'ledger_migration') THEN
    CREATE ROLE ledger_migration LOGIN;
  END IF;
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'ledger_api') THEN
    CREATE ROLE ledger_api LOGIN;
  END IF;
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'ledger_outbox_publisher') THEN
    CREATE ROLE ledger_outbox_publisher LOGIN;
  END IF;
END
$$;

ALTER ROLE ledger_migration PASSWORD :'ledger_migration_password';
ALTER ROLE ledger_api PASSWORD :'ledger_api_password';
ALTER ROLE ledger_outbox_publisher PASSWORD :'ledger_outbox_publisher_password';

-- Transferência explícita de ownership (nunca GRANT de papel amplo): em
-- volume novo, os ALTER TABLE IF EXISTS abaixo são no-op (as tabelas ainda
-- não existem — as migrations as criam diretamente como ledger_migration,
-- já owner do schema); em volume existente, move as tabelas já criadas por
-- "ledger" para o novo owner. Não se usa REASSIGN OWNED BY: além de mover
-- as tabelas de aplicação, essa instrução tentaria reatribuir também as
-- cópias locais dos catálogos do sistema (pg_type, pg_statistic, tabelas
-- toast) que a imagem oficial do Postgres atribui ao usuário que criou o
-- banco (POSTGRES_USER) — Postgres recusa esse tipo de reatribuição
-- ("required by the database system"), confirmado por execução real.
ALTER DATABASE ledger OWNER TO ledger_migration;
ALTER SCHEMA public OWNER TO ledger_migration;
ALTER TABLE IF EXISTS "__EFMigrationsHistory" OWNER TO ledger_migration;
ALTER TABLE IF EXISTS entries OWNER TO ledger_migration;
ALTER TABLE IF EXISTS input_idempotency OWNER TO ledger_migration;
ALTER TABLE IF EXISTS outbox_messages OWNER TO ledger_migration;

REVOKE ALL ON SCHEMA public FROM PUBLIC;
REVOKE ALL ON DATABASE ledger FROM PUBLIC;

GRANT CONNECT ON DATABASE ledger TO ledger_migration, ledger_api, ledger_outbox_publisher;
GRANT USAGE ON SCHEMA public TO ledger_migration, ledger_api, ledger_outbox_publisher;
