using Npgsql;
using Xunit;

namespace BancoCarrefour.Security.IntegrationTests.Database;

/// <summary>
/// Prova, com conexões reais por role (nunca inspeção textual de grants),
/// o menor privilégio descrito na ADR-0009 — usando os scripts SQL reais
/// entregues nesse bloco (<see cref="DatabasePrivilegesFixture"/>).
/// </summary>
[Collection(DatabasePrivilegesCollection.Name)]
public sealed class DatabasePrivilegesTests(DatabasePrivilegesFixture fixture)
{
    [Fact]
    public async Task Ledger_api_deve_conseguir_registrar_lancamento_mas_nao_executar_DDL_nem_acessar_Consolidation()
    {
        await using var connection = new NpgsqlConnection(fixture.LedgerApiConnectionString);
        await connection.OpenAsync();

        await using (var insertEntry = connection.CreateCommand())
        {
            insertEntry.CommandText = """
                INSERT INTO entries (entry_id, merchant_id, business_date, type, amount, currency, occurred_at, created_at, description)
                VALUES (gen_random_uuid(), 'merchant-priv-test', '2026-07-24', 'CREDIT', 10.00, 'BRL', now(), now(), 'teste');
                """;
            var affected = await insertEntry.ExecuteNonQueryAsync();
            Assert.Equal(1, affected);
        }

        await using var ddlAttempt = connection.CreateCommand();
        ddlAttempt.CommandText = "CREATE TABLE ledger_api_should_not_create (id int);";
        var exception = await Assert.ThrowsAsync<PostgresException>(() => ddlAttempt.ExecuteNonQueryAsync());
        Assert.Equal("42501", exception.SqlState); // insufficient_privilege
    }

    [Fact]
    public async Task Ledger_outbox_publisher_deve_atualizar_outbox_mas_nao_inserir_entries_nem_alterar_input_idempotency()
    {
        await using var connection = new NpgsqlConnection(fixture.LedgerOutboxPublisherConnectionString);
        await connection.OpenAsync();

        await using (var selectOutbox = connection.CreateCommand())
        {
            selectOutbox.CommandText = "SELECT count(*) FROM outbox_messages;";
            await selectOutbox.ExecuteScalarAsync();
        }

        await using var insertEntry = connection.CreateCommand();
        insertEntry.CommandText = """
            INSERT INTO entries (entry_id, merchant_id, business_date, type, amount, currency, occurred_at, created_at, description)
            VALUES (gen_random_uuid(), 'merchant-priv-test', '2026-07-24', 'CREDIT', 10.00, 'BRL', now(), now(), 'teste');
            """;
        var exception = await Assert.ThrowsAsync<PostgresException>(() => insertEntry.ExecuteNonQueryAsync());
        Assert.Equal("42501", exception.SqlState);

        await using var updateIdempotency = connection.CreateCommand();
        updateIdempotency.CommandText = "DELETE FROM input_idempotency;";
        var idempotencyException = await Assert.ThrowsAsync<PostgresException>(() => updateIdempotency.ExecuteNonQueryAsync());
        Assert.Equal("42501", idempotencyException.SqlState);
    }

    [Fact]
    public async Task Consolidation_worker_deve_aplicar_evento_mas_nao_executar_DDL_nem_acessar_Ledger()
    {
        await using var connection = new NpgsqlConnection(fixture.ConsolidationWorkerConnectionString);
        await connection.OpenAsync();

        await using (var insertProcessedEvent = connection.CreateCommand())
        {
            insertProcessedEvent.CommandText = """
                INSERT INTO processed_events (processed_event_id, event_id, event_type, event_version, merchant_id, business_date, processed_at)
                VALUES (gen_random_uuid(), gen_random_uuid(), 'FinancialEntryRegistered', 1, 'merchant-priv-test', '2026-07-24', now());
                """;
            var affected = await insertProcessedEvent.ExecuteNonQueryAsync();
            Assert.Equal(1, affected);
        }

        await using var ddlAttempt = connection.CreateCommand();
        ddlAttempt.CommandText = "CREATE TABLE consolidation_worker_should_not_create (id int);";
        var exception = await Assert.ThrowsAsync<PostgresException>(() => ddlAttempt.ExecuteNonQueryAsync());
        Assert.Equal("42501", exception.SqlState);
    }

    [Fact]
    public async Task Consolidation_api_readonly_deve_consultar_daily_balances_mas_nunca_escrever_ou_executar_DDL()
    {
        await using var connection = new NpgsqlConnection(fixture.ConsolidationApiReadonlyConnectionString);
        await connection.OpenAsync();

        await using (var select = connection.CreateCommand())
        {
            select.CommandText = "SELECT count(*) FROM daily_balances;";
            await select.ExecuteScalarAsync();
        }

        await using var insertAttempt = connection.CreateCommand();
        insertAttempt.CommandText = """
            INSERT INTO daily_balances (daily_balance_id, merchant_id, business_date, total_credits, total_debits, balance, currency, entry_count, last_event_occurred_at, last_updated_at)
            VALUES (gen_random_uuid(), 'merchant-priv-test', '2026-07-24', 0, 0, 0, 'BRL', 0, now(), now());
            """;
        var insertException = await Assert.ThrowsAsync<PostgresException>(() => insertAttempt.ExecuteNonQueryAsync());
        Assert.Equal("42501", insertException.SqlState);

        await using var ddlAttempt = connection.CreateCommand();
        ddlAttempt.CommandText = "CREATE TABLE consolidation_readonly_should_not_create (id int);";
        var ddlException = await Assert.ThrowsAsync<PostgresException>(() => ddlAttempt.ExecuteNonQueryAsync());
        Assert.Equal("42501", ddlException.SqlState);

        await using var processedEventsAttempt = connection.CreateCommand();
        processedEventsAttempt.CommandText = "SELECT count(*) FROM processed_events;";
        var processedEventsException = await Assert.ThrowsAsync<PostgresException>(() => processedEventsAttempt.ExecuteScalarAsync());
        Assert.Equal("42501", processedEventsException.SqlState);
    }
}
