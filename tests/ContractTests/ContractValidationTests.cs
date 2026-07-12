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
    public void EntryCreated_schema_deve_ser_carregado_e_parseado()
    {
        var schema = LoadEntryCreatedSchema();

        Assert.NotNull(schema);
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("0.10")]
    [InlineData("0.99")]
    [InlineData("1")]
    [InlineData("1.00")]
    [InlineData("150.75")]
    public void EntryCreated_amount_deve_aceitar_valores_monetarios_validos(string amount)
    {
        var regex = GetEntryCreatedAmountRegex();

        Assert.Matches(regex, amount);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.0")]
    [InlineData("0.00")]
    public void EntryCreated_amount_deve_rejeitar_zero(string amount)
    {
        var regex = GetEntryCreatedAmountRegex();

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

    private static JsonSchema LoadEntryCreatedSchema()
    {
        var path = RepositoryRoot.EntryCreatedSchemaPath;
        var content = File.ReadAllText(path);

        return JsonSchema.FromText(content);
    }

    private static Regex GetEntryCreatedAmountRegex()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepositoryRoot.EntryCreatedSchemaPath));
        var pattern = document.RootElement
            .GetProperty("properties")
            .GetProperty("amount")
            .GetProperty("pattern")
            .GetString();

        Assert.False(string.IsNullOrWhiteSpace(pattern));

        return new Regex(pattern, RegexOptions.CultureInvariant);
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

        public static string EntryCreatedSchemaPath => Path.Combine(
            PathValue,
            "contracts",
            "events",
            "entry-created-v1.schema.json");

        private static string PathValue { get; } = Locate();

        private static string Locate()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                var openApiPath = Path.Combine(directory.FullName, "contracts", "openapi.yaml");
                var entryCreatedSchemaPath = Path.Combine(
                    directory.FullName,
                    "contracts",
                    "events",
                    "entry-created-v1.schema.json");

                if (File.Exists(openApiPath) && File.Exists(entryCreatedSchemaPath))
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
