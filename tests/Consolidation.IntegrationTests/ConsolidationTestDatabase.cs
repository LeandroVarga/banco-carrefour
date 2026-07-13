using Npgsql;

namespace BancoCarrefour.Consolidation.IntegrationTests;

internal sealed class ConsolidationTestDatabase : IAsyncDisposable
{
    private readonly string maintenanceConnectionString;
    private readonly string databaseName;

    public ConsolidationTestDatabase()
    {
        var source = new NpgsqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("CONSOLIDATION_TEST_CONNECTION_STRING")
            ?? "Host=consolidation-postgres;Port=5432;Database=consolidation;Username=consolidation;Password=consolidation");

        var baseName = string.IsNullOrWhiteSpace(source.Database)
            ? "consolidation"
            : source.Database;
        databaseName = $"{baseName}_test_{Guid.NewGuid():N}";

        var target = new NpgsqlConnectionStringBuilder(source.ConnectionString)
        {
            Database = databaseName
        };
        ConnectionString = target.ConnectionString;

        source.Database = "postgres";
        maintenanceConnectionString = source.ConnectionString;
    }

    public string ConnectionString { get; }

    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(maintenanceConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"""CREATE DATABASE "{databaseName}";""";
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(maintenanceConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"""DROP DATABASE IF EXISTS "{databaseName}" WITH (FORCE);""";
        await command.ExecuteNonQueryAsync();
    }
}
