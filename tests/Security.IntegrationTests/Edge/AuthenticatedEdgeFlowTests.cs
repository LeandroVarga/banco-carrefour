using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using BancoCarrefour.Security.IntegrationTests.Shared;
using Xunit;

namespace BancoCarrefour.Security.IntegrationTests.Edge;

/// <summary>
/// Fluxo legítimo pelas duas APIs, através da borda real (edge-proxy HTTPS
/// + WAF), com Keycloak e Postgres reais — a mesma prova já validada
/// manualmente nesta sessão (ver "Fluxo de autenticação" no documento do
/// ciclo), agora automatizada. O fluxo Outbox→SQS→Worker→Projection
/// completo permanece fora de escopo (fixture sistêmica futura).
/// </summary>
[Collection(AuthenticatedEdgeFlowCollection.Name)]
public sealed class AuthenticatedEdgeFlowTests(AuthenticatedEdgeFlowFixture fixture)
{
    [Fact]
    public async Task Ledger_POST_pela_borda_com_token_real_deve_retornar_201_com_merchantId_do_token()
    {
        var token = await fixture.IssueMerchantATokenAsync(KeycloakTestRealm.LedgerWriteScope);

        using var client = new HttpClient(fixture.CreateTrustedHandler());
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{fixture.EdgeHttpsBaseUrl}/ledger/entries")
        {
            Content = JsonContent.Create(new
            {
                type = "CREDIT",
                amount = "150.75",
                currency = "BRL",
                occurredAt = DateTimeOffset.Parse("2026-07-11T13:45:00Z"),
                description = "Venda cartão"
            })
        };
        request.Headers.Host = "localhost:8443";
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Esperado 201, obtido {response.StatusCode}: {body}");
        var json = JsonDocument.Parse(body).RootElement;
        Assert.Equal("merchant-a", json.GetProperty("merchantId").GetString());
    }

    [Fact]
    public async Task Consolidation_GET_pela_borda_com_token_real_deve_retornar_resposta_funcional_com_merchantId_do_token()
    {
        var businessDate = new DateOnly(2026, 7, 11);
        await fixture.InsertConsolidationDailyBalanceAsync("merchant-a", businessDate);

        var token = await fixture.IssueMerchantATokenAsync(KeycloakTestRealm.ConsolidationReadScope);

        using var client = new HttpClient(fixture.CreateTrustedHandler());
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{fixture.EdgeHttpsBaseUrl}/consolidation/daily-balances/2026-07-11");
        request.Headers.Host = "localhost:8443";
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Esperado 200, obtido {response.StatusCode}: {body}");
        var json = JsonDocument.Parse(body).RootElement;
        Assert.Equal("merchant-a", json.GetProperty("merchantId").GetString());
    }

    /// <summary>
    /// Prova B (ver ADR-0009): não basta mostrar que o resolver, chamado
    /// isoladamente, reflete um valor novo do SSM (isso já é
    /// <c>SsmOidcConfigurationTests.Mudanca_de_valor_no_SSM_...</c>, prova de
    /// resolver/startup). Aqui o processo real do Ledger.Api (container já
    /// em execução nesta fixture) precisa continuar aceitando o token válido
    /// mesmo depois do SSM mudar (sem polling/hot-reload) e só passar a
    /// rejeitá-lo depois de um restart real do container - a semântica
    /// exigida é "sem efeito até reiniciar", não "nunca muda".
    /// </summary>
    [Fact]
    public async Task Mudanca_de_audience_no_SSM_nao_afeta_processo_em_execucao_e_exige_restart_real()
    {
        const string ledgerAudienceParameterName = "/banco-carrefour/oidc/ledger-audience";
        const string originalAudienceValue = "ledger-api";
        const string rotatedAudienceValue = "ledger-api-audience-rotacionada-teste";

        using var ssmClient = new AmazonSimpleSystemsManagementClient(
            new BasicAWSCredentials("test", "test"),
            new AmazonSimpleSystemsManagementConfig { ServiceURL = fixture.LocalStackServiceUrl, AuthenticationRegion = "us-east-1" });

        var token = await fixture.IssueMerchantATokenAsync(KeycloakTestRealm.LedgerWriteScope);

        try
        {
            // 1) Estado inicial: token válido é aceito (SSM ainda com o valor original).
            var beforeChange = await PostEntryAsync(token);
            Assert.True(beforeChange == HttpStatusCode.Created, $"Esperado 201 antes da mudança, obtido {beforeChange}.");

            // 2) Muda o parâmetro real do SSM - o processo do Ledger.Api já está
            // em execução e NÃO deve perceber a mudança (sem polling).
            await ssmClient.PutParameterAsync(new PutParameterRequest
            {
                Name = ledgerAudienceParameterName,
                Value = rotatedAudienceValue,
                Type = ParameterType.String,
                Overwrite = true
            });

            var afterChangeBeforeRestart = await PostEntryAsync(token);
            Assert.True(
                afterChangeBeforeRestart == HttpStatusCode.Created,
                $"O processo já em execução deveria continuar aceitando o token (valor antigo em memória) - obtido {afterChangeBeforeRestart}.");

            // 3) Restart real do container - só agora o novo valor é carregado.
            await fixture.RestartLedgerApiAsync();

            var afterRestart = await PostEntryAsync(token);
            Assert.True(
                afterRestart == HttpStatusCode.Unauthorized,
                $"Depois do restart, o token (audience '{originalAudienceValue}') deveria ser rejeitado pela nova audience '{rotatedAudienceValue}' - obtido {afterRestart}.");

            // 4) Não basta mostrar que a audience antiga foi descartada - a
            // prova só fica completa mostrando que a NOVA configuracao do
            // SSM ficou efetivamente operacional. Emite, no Keycloak real
            // (Admin REST API, nunca injetando opcoes de JwtBearer), um
            // token novo cuja audience passa a ser a mesma rotacionada, e
            // confirma que o processo ja reiniciado o aceita.
            await fixture.SetLedgerAudienceMapperAsync(rotatedAudienceValue);
            var rotatedAudienceToken = await fixture.IssueMerchantATokenAsync(KeycloakTestRealm.LedgerWriteScope);

            var afterRestartWithRotatedAudience = await PostEntryAsync(rotatedAudienceToken);
            Assert.True(
                afterRestartWithRotatedAudience == HttpStatusCode.Created,
                $"Depois do restart, um token com a nova audience '{rotatedAudienceValue}' deveria ser aceito (prova de que a nova configuracao do SSM ficou operacional) - obtido {afterRestartWithRotatedAudience}.");
        }
        finally
        {
            // Restaura o mapper do Keycloak e o parametro do SSM, reiniciando
            // de novo, deixando a fixture no estado original para qualquer
            // outro teste da mesma collection.
            await fixture.SetLedgerAudienceMapperAsync(originalAudienceValue);
            await ssmClient.PutParameterAsync(new PutParameterRequest
            {
                Name = ledgerAudienceParameterName,
                Value = originalAudienceValue,
                Type = ParameterType.String,
                Overwrite = true
            });
            await fixture.RestartLedgerApiAsync();
        }
    }

    private async Task<HttpStatusCode> PostEntryAsync(string token)
    {
        using var client = new HttpClient(fixture.CreateTrustedHandler());
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{fixture.EdgeHttpsBaseUrl}/ledger/entries")
        {
            Content = JsonContent.Create(new
            {
                type = "CREDIT",
                amount = "10.00",
                currency = "BRL",
                occurredAt = DateTimeOffset.Parse("2026-07-11T13:45:00Z"),
                description = "restart-ssm-probe"
            })
        };
        request.Headers.Host = "localhost:8443";
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);
        return response.StatusCode;
    }
}
