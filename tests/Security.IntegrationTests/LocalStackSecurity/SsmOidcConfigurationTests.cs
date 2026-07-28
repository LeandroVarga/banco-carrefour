using System.Diagnostics;
using Amazon.Runtime;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using BancoCarrefour.Ledger.Infrastructure.Ssm;
using Microsoft.Extensions.Configuration;
using Xunit;
using ConsolidationSsm = BancoCarrefour.Consolidation.Infrastructure.Ssm;

namespace BancoCarrefour.Security.IntegrationTests.LocalStackSecurity;

/// <summary>
/// Prova, contra o SSM Parameter Store real (mesmo LocalStack e Terraform
/// reais de <see cref="LocalStackSecurityFixture"/>) e os resolvers de
/// produção (<see cref="LedgerOidcConfigurationResolver"/>,
/// <see cref="ConsolidationSsm.ConsolidationOidcConfigurationResolver"/>,
/// nunca reimplementados aqui), o comportamento exigido pela ADR-0009 para
/// a fonte autoritativa de issuer/audience: caso positivo por API,
/// parâmetro ausente, valor inválido, indisponibilidade com timeout finito,
/// separação por API e semântica de "mudança de valor não afeta resolução
/// já feita" (equivalente a exigir restart).
/// </summary>
[Collection(LocalStackSecurityCollection.Name)]
public sealed class SsmOidcConfigurationTests(LocalStackSecurityFixture fixture)
{
    [Fact]
    public async Task Caso_positivo_Ledger_resolve_issuer_e_audience_reais_do_SSM()
    {
        var configuration = BuildConfiguration(
            issuerParameterName: "/banco-carrefour/oidc/issuer",
            audienceParameterName: "/banco-carrefour/oidc/ledger-audience");

        var result = await LedgerOidcConfigurationResolver.ResolveAsync(configuration, "Production");

        Assert.Equal("https://keycloak.localhost:8443/realms/banco-carrefour", result.Authority);
        Assert.Equal("ledger-api", result.Audience);
    }

    [Fact]
    public async Task Caso_positivo_Consolidation_resolve_issuer_e_audience_reais_do_SSM()
    {
        var configuration = BuildConfiguration(
            issuerParameterName: "/banco-carrefour/oidc/issuer",
            audienceParameterName: "/banco-carrefour/oidc/consolidation-audience");

        var result = await ConsolidationSsm.ConsolidationOidcConfigurationResolver.ResolveAsync(configuration, "Production");

        Assert.Equal("https://keycloak.localhost:8443/realms/banco-carrefour", result.Authority);
        Assert.Equal("consolidation-api", result.Audience);
    }

    [Fact]
    public async Task Separacao_por_API_ledger_e_consolidation_resolvem_audiences_distintas()
    {
        // A garantia de que CADA API só é configurada (docker-compose/IAM) com
        // o nome do SEU próprio parâmetro é verificada separadamente em
        // Architecture.Tests (configuração declarada) e no módulo IAM
        // (Caso_12_IAM_resources_foram_provisionados, em LocalStackSecurityTests).
        // Aqui confirmamos que os dois parâmetros reais têm valores distintos -
        // pré-condição para essa separação fazer sentido.
        var ledgerConfiguration = BuildConfiguration(
            issuerParameterName: "/banco-carrefour/oidc/issuer",
            audienceParameterName: "/banco-carrefour/oidc/ledger-audience");
        var consolidationConfiguration = BuildConfiguration(
            issuerParameterName: "/banco-carrefour/oidc/issuer",
            audienceParameterName: "/banco-carrefour/oidc/consolidation-audience");

        var ledgerResult = await LedgerOidcConfigurationResolver.ResolveAsync(ledgerConfiguration, "Production");
        var consolidationResult = await ConsolidationSsm.ConsolidationOidcConfigurationResolver.ResolveAsync(consolidationConfiguration, "Production");

        Assert.NotEqual(ledgerResult.Audience, consolidationResult.Audience);
        Assert.Equal("ledger-api", ledgerResult.Audience);
        Assert.Equal("consolidation-api", consolidationResult.Audience);
    }

    [Fact]
    public async Task Parametro_de_issuer_ausente_causa_falha_clara_no_startup()
    {
        var configuration = BuildConfiguration(
            issuerParameterName: $"/banco-carrefour/oidc/issuer-inexistente-{Guid.NewGuid():N}",
            audienceParameterName: "/banco-carrefour/oidc/ledger-audience");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => LedgerOidcConfigurationResolver.ResolveAsync(configuration, "Production"));

        Assert.Contains("SSM", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Parametro_de_audience_ausente_causa_falha_clara_no_startup()
    {
        var configuration = BuildConfiguration(
            issuerParameterName: "/banco-carrefour/oidc/issuer",
            audienceParameterName: $"/banco-carrefour/oidc/audience-inexistente-{Guid.NewGuid():N}");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => LedgerOidcConfigurationResolver.ResolveAsync(configuration, "Production"));
    }

    [Fact]
    public async Task Valor_de_issuer_sem_https_no_SSM_causa_falha_de_validacao()
    {
        using var ssmClient = CreateSsmClient();
        var tempParameterName = $"/banco-carrefour/oidc/issuer-invalido-{Guid.NewGuid():N}";
        await ssmClient.PutParameterAsync(new PutParameterRequest
        {
            Name = tempParameterName,
            Value = "http://issuer-sem-https.local/realms/banco-carrefour",
            Type = ParameterType.String
        });

        var configuration = BuildConfiguration(
            issuerParameterName: tempParameterName,
            audienceParameterName: "/banco-carrefour/oidc/ledger-audience");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => LedgerOidcConfigurationResolver.ResolveAsync(configuration, "Production"));
        Assert.Contains("HTTPS", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Valor_de_audience_vazio_no_SSM_causa_falha_de_validacao()
    {
        using var ssmClient = CreateSsmClient();
        var tempParameterName = $"/banco-carrefour/oidc/audience-vazia-{Guid.NewGuid():N}";
        await ssmClient.PutParameterAsync(new PutParameterRequest
        {
            Name = tempParameterName,
            Value = " ",
            Type = ParameterType.String
        });

        var configuration = BuildConfiguration(
            issuerParameterName: "/banco-carrefour/oidc/issuer",
            audienceParameterName: tempParameterName);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => LedgerOidcConfigurationResolver.ResolveAsync(configuration, "Production"));
    }

    [Fact]
    public async Task SSM_indisponivel_falha_rapido_com_timeout_finito_sem_retry_infinito()
    {
        // Porta fechada no próprio host de teste (connection refused quase
        // imediato) combinada com um timeout curto explícito - nunca um hang
        // indefinido nem retry infinito.
        var configuration = BuildConfiguration(
            issuerParameterName: "/banco-carrefour/oidc/issuer",
            audienceParameterName: "/banco-carrefour/oidc/ledger-audience",
            serviceUrlOverride: "http://127.0.0.1:1",
            timeoutSecondsOverride: "3");

        var stopwatch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => LedgerOidcConfigurationResolver.ResolveAsync(configuration, "Production"));
        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(15),
            $"Esperava falha rápida e finita (<15s), levou {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task Mudanca_de_valor_no_SSM_nao_afeta_resolucao_ja_feita_novo_resolve_reflete_valor_atualizado()
    {
        using var ssmClient = CreateSsmClient();
        var tempParameterName = $"/banco-carrefour/oidc/audience-mutavel-{Guid.NewGuid():N}";
        await ssmClient.PutParameterAsync(new PutParameterRequest
        {
            Name = tempParameterName,
            Value = "audience-valor-a",
            Type = ParameterType.String
        });

        var configuration = BuildConfiguration(
            issuerParameterName: "/banco-carrefour/oidc/issuer",
            audienceParameterName: tempParameterName);

        var firstResolve = await LedgerOidcConfigurationResolver.ResolveAsync(configuration, "Production");
        Assert.Equal("audience-valor-a", firstResolve.Audience);

        await ssmClient.PutParameterAsync(new PutParameterRequest
        {
            Name = tempParameterName,
            Value = "audience-valor-b",
            Type = ParameterType.String,
            Overwrite = true
        });

        // A instância já resolvida é um record imutável - não muda sozinha
        // (equivalente a: uma API já iniciada não recarrega o valor).
        Assert.Equal("audience-valor-a", firstResolve.Audience);

        // Uma nova resolução (equivalente a reiniciar/redeployar o componente)
        // reflete o novo valor.
        var secondResolve = await LedgerOidcConfigurationResolver.ResolveAsync(configuration, "Production");
        Assert.Equal("audience-valor-b", secondResolve.Audience);
    }

    private IConfiguration BuildConfiguration(
        string issuerParameterName,
        string audienceParameterName,
        string? serviceUrlOverride = null,
        string? timeoutSecondsOverride = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["SystemsManager:ServiceUrl"] = serviceUrlOverride ?? fixture.ServiceUrl,
            ["SystemsManager:Region"] = LocalStackSecurityFixture.Region,
            ["SystemsManager:AccessKey"] = LocalStackSecurityFixture.AccessKey,
            ["SystemsManager:SecretKey"] = LocalStackSecurityFixture.SecretKey,
            ["SystemsManager:IssuerParameterName"] = issuerParameterName,
            ["SystemsManager:AudienceParameterName"] = audienceParameterName
        };

        if (timeoutSecondsOverride is not null)
        {
            values["SystemsManager:TimeoutSeconds"] = timeoutSecondsOverride;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private AmazonSimpleSystemsManagementClient CreateSsmClient() =>
        new(new BasicAWSCredentials(LocalStackSecurityFixture.AccessKey, LocalStackSecurityFixture.SecretKey),
            new AmazonSimpleSystemsManagementConfig { ServiceURL = fixture.ServiceUrl, AuthenticationRegion = LocalStackSecurityFixture.Region });
}
