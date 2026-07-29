-- Grants explícitos por tabela — mecanismo autoritativo de precisão do
-- menor privilégio do Consolidation (ver ADR-0009). Roda DEPOIS das
-- migrations, conectado como o owner (consolidation_migration).
-- Reexecutável a cada migration futura que altere o conjunto de
-- tabelas/colunas.
\set ON_ERROR_STOP on

GRANT SELECT, INSERT, UPDATE ON TABLE processed_events TO consolidation_worker;
GRANT SELECT, INSERT, UPDATE ON TABLE daily_balances TO consolidation_worker;
REVOKE ALL ON TABLE processed_events, daily_balances FROM consolidation_api_readonly;
GRANT SELECT ON TABLE daily_balances TO consolidation_api_readonly;
