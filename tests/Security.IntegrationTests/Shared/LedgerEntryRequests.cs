using System.Net.Http.Json;

namespace BancoCarrefour.Security.IntegrationTests.Shared;

/// <summary>
/// Constrói as requisições HTTP reais usadas pelos testes de identidade
/// contra <c>POST /entries</c> (Ledger.Api) e <c>GET /daily-balances/{date}</c>
/// (Consolidation.Api) — mesmo payload mínimo válido já usado pelos testes
/// de integração existentes (<c>tests/Ledger.IntegrationTests/PostEntriesTests.cs</c>),
/// sem reimplementar a lógica de validação do produto.
/// </summary>
internal static class LedgerEntryRequests
{
    public static async Task<HttpResponseMessage> PostEntryAsync(HttpClient client, string? bearerToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/entries")
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

        if (bearerToken is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
        }

        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> GetDailyBalanceAsync(
        HttpClient client,
        string? bearerToken,
        string businessDate = "2026-07-11")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/daily-balances/{businessDate}");

        if (bearerToken is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
        }

        return await client.SendAsync(request);
    }
}
