using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BancoCarrefour.Consolidation.Infrastructure;
using BancoCarrefour.Consolidation.Infrastructure.Entities;
using BancoCarrefour.Security.IntegrationTests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BancoCarrefour.Security.IntegrationTests.Identity;

/// <summary>
/// Prova que a identidade do comerciante deriva exclusivamente do token —
/// nunca de valores fornecidos pelo cliente (body, query ou header) — e
/// que um comerciante não acessa dados de outro. Ledger.Api não expõe
/// leitura por comerciante, então o isolamento ali é provado pela
/// derivação correta do <c>merchant_id</c> na resposta de escrita; para
/// Consolidation.Api, uma linha de <c>DailyBalance</c> é inserida
/// diretamente (mesma técnica de <c>GetDailyBalancesTests.cs</c>, sem
/// depender do pipeline Outbox→SQS→Worker, fora do escopo desta etapa) para
/// provar que o merchant B genuinamente não enxerga a projeção do
/// merchant A, mesmo existindo na mesma base.
/// </summary>
[Collection(IdentitySecurityCollection.Name)]
public sealed class MerchantIsolationTests : IDisposable
{
    private readonly IdentityFixture fixture;
    private readonly LedgerIdentityApiFactory ledgerFactory;
    private readonly ConsolidationIdentityApiFactory consolidationFactory;
    private readonly HttpClient ledgerClient;
    private readonly HttpClient consolidationClient;

    public MerchantIsolationTests(IdentityFixture fixture)
    {
        this.fixture = fixture;
        ledgerFactory = fixture.CreateLedgerApiFactory();
        consolidationFactory = fixture.CreateConsolidationApiFactory();
        ledgerClient = ledgerFactory.CreateClient();
        consolidationClient = consolidationFactory.CreateClient();
    }

    [Fact]
    public async Task MerchantId_na_resposta_deve_vir_do_token_de_cada_comerciante()
    {
        var tokenA = await fixture.IssueTokenAsync(KeycloakTestRealm.MerchantAClientId, scope: KeycloakTestRealm.LedgerWriteScope);
        var tokenB = await fixture.IssueTokenAsync(KeycloakTestRealm.MerchantBClientId, scope: KeycloakTestRealm.LedgerWriteScope);

        var responseA = await LedgerEntryRequests.PostEntryAsync(ledgerClient, tokenA);
        var responseB = await LedgerEntryRequests.PostEntryAsync(ledgerClient, tokenB);

        Assert.Equal(HttpStatusCode.Created, responseA.StatusCode);
        Assert.Equal(HttpStatusCode.Created, responseB.StatusCode);

        var bodyA = await responseA.Content.ReadFromJsonAsync<JsonElement>();
        var bodyB = await responseB.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("merchant-a", bodyA.GetProperty("merchantId").GetString());
        Assert.Equal("merchant-b", bodyB.GetProperty("merchantId").GetString());
    }

    [Fact]
    public async Task Merchant_id_informado_no_body_na_query_ou_no_header_deve_ser_ignorado()
    {
        var tokenA = await fixture.IssueTokenAsync(KeycloakTestRealm.MerchantAClientId, scope: KeycloakTestRealm.LedgerWriteScope);

        // Sem "merchantId" no corpo: CreateEntryRequest rejeita campos não
        // mapeados com 400 (comportamento real, confirmado por execução —
        // já é uma defesa própria da aplicação). O vetor testado aqui é
        // query string + header, que a aplicação de fato ignora, já que o
        // handler só lê merchant_id do claim do token.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/entries?merchantId=merchant-b")
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
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenA);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Headers.Add("X-Merchant-Id", "merchant-b");

        var response = await ledgerClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("merchant-a", body.GetProperty("merchantId").GetString());
    }

    [Fact]
    public async Task Merchant_B_nao_deve_enxergar_a_projecao_do_merchant_A()
    {
        var businessDate = new DateOnly(2026, 7, 11);
        await InsertDailyBalanceAsync("merchant-a", businessDate);

        var tokenA = await fixture.IssueTokenAsync(KeycloakTestRealm.MerchantAClientId, scope: KeycloakTestRealm.ConsolidationReadScope);
        var tokenB = await fixture.IssueTokenAsync(KeycloakTestRealm.MerchantBClientId, scope: KeycloakTestRealm.ConsolidationReadScope);

        var responseA = await LedgerEntryRequests.GetDailyBalanceAsync(consolidationClient, tokenA, "2026-07-11");
        var responseB = await LedgerEntryRequests.GetDailyBalanceAsync(consolidationClient, tokenB, "2026-07-11");

        Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);
        var bodyA = await responseA.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("merchant-a", bodyA.GetProperty("merchantId").GetString());

        Assert.Equal(HttpStatusCode.NotFound, responseB.StatusCode);
    }

    private async Task InsertDailyBalanceAsync(string merchantId, DateOnly businessDate)
    {
        await using var scope = consolidationFactory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ConsolidationDbContext>();

        dbContext.DailyBalances.Add(new DailyBalance
        {
            DailyBalanceId = Guid.NewGuid(),
            MerchantId = merchantId,
            BusinessDate = businessDate,
            TotalCredits = 150.7m,
            TotalDebits = 25.1m,
            Balance = 125.6m,
            Currency = "BRL",
            EntryCount = 2,
            LastEventOccurredAt = DateTimeOffset.Parse("2026-07-11T13:45:00Z"),
            LastUpdatedAt = DateTimeOffset.Parse("2026-07-11T13:45:05Z")
        });

        await dbContext.SaveChangesAsync();
    }

    public void Dispose()
    {
        ledgerClient.Dispose();
        consolidationClient.Dispose();
        ledgerFactory.Dispose();
        consolidationFactory.Dispose();
    }
}
