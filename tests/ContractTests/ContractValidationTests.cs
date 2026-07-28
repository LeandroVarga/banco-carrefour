using System.Text.Json;
using System.Text.RegularExpressions;
using Json.Schema;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Xunit;

namespace ContractTests;

public sealed class ContractValidationTests
{
    [Fact]
    public void OpenApi_deve_ser_carregado_e_parseado()
    {
        var openApi = LoadOpenApiDocument();

        Assert.Equal("Banco Carrefour Cash Flow API", openApi.Info.Title);
    }

    [Fact]
    public void FinancialEntryRegistered_schema_deve_ser_carregado_e_parseado()
    {
        var schema = LoadFinancialEntryRegisteredSchema();

        Assert.NotNull(schema);
    }

    [Fact]
    public void FinancialEntryRegistered_deve_declarar_campos_obrigatorios_do_evento()
    {
        var required = GetFinancialEntryRegisteredRequiredFields();

        Assert.Contains("eventId", required);
        Assert.Contains("entryId", required);
        Assert.Contains("eventType", required);
        Assert.Contains("eventVersion", required);
        Assert.Contains("occurredAt", required);
        Assert.Contains("registeredAt", required);
        Assert.Contains("correlationId", required);
        Assert.Contains("merchantId", required);
        Assert.Contains("businessDate", required);
        Assert.Contains("type", required);
        Assert.Contains("amount", required);
        Assert.Contains("currency", required);
        Assert.Contains("description", required);
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("0.10")]
    [InlineData("0.99")]
    [InlineData("1")]
    [InlineData("1.00")]
    [InlineData("150.75")]
    public void FinancialEntryRegistered_amount_deve_aceitar_valores_monetarios_validos(string amount)
    {
        var regex = GetFinancialEntryRegisteredAmountRegex();

        Assert.Matches(regex, amount);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.0")]
    [InlineData("0.00")]
    public void FinancialEntryRegistered_amount_deve_rejeitar_zero(string amount)
    {
        var regex = GetFinancialEntryRegisteredAmountRegex();

        Assert.DoesNotMatch(regex, amount);
    }

    [Fact]
    public void CreateEntryRequest_deve_rejeitar_propriedades_adicionais()
    {
        var schema = GetCreateEntryRequestSchema();

        Assert.False(schema.AdditionalPropertiesAllowed);
    }

    [Fact]
    public void CreateEntryRequest_nao_deve_aceitar_merchantId()
    {
        var schema = GetCreateEntryRequestSchema();

        Assert.DoesNotContain("merchantId", schema.Properties.Keys);
    }

    private static OpenApiDocument LoadOpenApiDocument()
    {
        var path = RepositoryRoot.OpenApiPath;
        var content = File.ReadAllText(path);
        var document = new OpenApiStringReader().Read(content, out var diagnostic);

        Assert.NotNull(document);
        Assert.Empty(diagnostic.Errors);

        return document;
    }

    private static JsonSchema LoadFinancialEntryRegisteredSchema()
    {
        var path = RepositoryRoot.FinancialEntryRegisteredSchemaPath;
        var content = File.ReadAllText(path);

        return JsonSchema.FromText(content);
    }

    private static Regex GetFinancialEntryRegisteredAmountRegex()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepositoryRoot.FinancialEntryRegisteredSchemaPath));
        var pattern = document.RootElement
            .GetProperty("properties")
            .GetProperty("amount")
            .GetProperty("pattern")
            .GetString();

        Assert.False(string.IsNullOrWhiteSpace(pattern));

        return new Regex(pattern, RegexOptions.CultureInvariant);
    }

    private static IReadOnlyCollection<string> GetFinancialEntryRegisteredRequiredFields()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepositoryRoot.FinancialEntryRegisteredSchemaPath));

        return document.RootElement
            .GetProperty("required")
            .EnumerateArray()
            .Select(element => element.GetString())
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();
    }

    private static OpenApiSchema GetCreateEntryRequestSchema()
    {
        var openApi = LoadOpenApiDocument();

        Assert.True(openApi.Components.Schemas.TryGetValue("CreateEntryRequest", out var schema));

        return schema;
    }

    private static class RepositoryRoot
    {
        public static string OpenApiPath => Path.Combine(PathValue, "contracts", "openapi.yaml");

        public static string FinancialEntryRegisteredSchemaPath => Path.Combine(
            PathValue,
            "contracts",
            "events",
            "financial-entry-registered-v1.schema.json");

        private static string PathValue { get; } = Locate();

        private static string Locate()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                var openApiPath = Path.Combine(directory.FullName, "contracts", "openapi.yaml");
                var financialEntryRegisteredSchemaPath = Path.Combine(
                    directory.FullName,
                    "contracts",
                    "events",
                    "financial-entry-registered-v1.schema.json");

                if (File.Exists(openApiPath) && File.Exists(financialEntryRegisteredSchemaPath))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Não foi possível localizar a raiz do repositório contendo os contratos esperados.");
        }
    }
}
