-- Grants explícitos por tabela — mecanismo autoritativo de precisão do
-- menor privilégio do Ledger (ver ADR-0009). Roda DEPOIS das migrations,
-- conectado como o owner (ledger_migration). Reexecutável a cada migration
-- futura que altere o conjunto de tabelas/colunas.
\set ON_ERROR_STOP on

GRANT SELECT, INSERT ON TABLE entries TO ledger_api;
GRANT SELECT, INSERT ON TABLE input_idempotency TO ledger_api;
GRANT INSERT ON TABLE outbox_messages TO ledger_api;
REVOKE ALL ON TABLE entries, input_idempotency, outbox_messages FROM ledger_outbox_publisher;
GRANT SELECT, UPDATE ON TABLE outbox_messages TO ledger_outbox_publisher;
