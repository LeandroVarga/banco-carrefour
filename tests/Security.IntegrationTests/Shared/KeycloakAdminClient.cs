using System.Net.Http.Json;
using System.Text.Json;

namespace BancoCarrefour.Security.IntegrationTests.Shared;

/// <summary>
/// Cliente mínimo do Admin REST API do Keycloak, usado só pelos testes para
/// obter tokens (client_credentials) e ler/gerar client secrets — o mesmo
/// fluxo administrativo de <c>scripts/security/keycloak-bootstrap.sh</c>,
/// reimplementado em C# para uso dentro da fixture (não duplica a
/// implementação de autenticação do produto: não emite nem valida tokens,
/// só orquestra chamadas HTTP ao Keycloak real).
/// </summary>
internal sealed class KeycloakAdminClient(HttpClient httpClient, string baseUrl, string realm)
{
    private readonly string baseUrl = baseUrl.TrimEnd('/');

    public async Task<string> GetTokenAsync(
        string clientId,
        string clientSecret,
        string? scope = null,
        string? hostHeaderOverride = null,
        string realmOverride = "")
    {
        var targetRealm = string.IsNullOrEmpty(realmOverride) ? realm : realmOverride;
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret
        };

        if (!string.IsNullOrEmpty(scope))
        {
            form["scope"] = scope;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{baseUrl}/realms/{targetRealm}/protocol/openid-connect/token")
        {
            Content = new FormUrlEncodedContent(form)
        };

        if (hostHeaderOverride is not null)
        {
            request.Headers.Host = hostHeaderOverride;
        }

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Resposta do Keycloak não contém access_token.");
    }

    /// <summary>
    /// Solicita um token sem exigir sucesso — usado só para provar que o
    /// Keycloak rejeita, no próprio endpoint de token, um <c>scope</c>
    /// solicitado que não está atribuído (nem como default nem opcional)
    /// ao client, retornando o status HTTP bruto da resposta.
    /// </summary>
    public async Task<System.Net.HttpStatusCode> RequestTokenStatusCodeAsync(string clientId, string clientSecret, string scope)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{baseUrl}/realms/{realm}/protocol/openid-connect/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["scope"] = scope
            })
        };

        using var response = await httpClient.SendAsync(request);
        return response.StatusCode;
    }

    public async Task<string?> GetClientUuidAsync(string adminToken, string clientId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{baseUrl}/admin/realms/{realm}/clients?clientId={Uri.EscapeDataString(clientId)}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var clients = await response.Content.ReadFromJsonAsync<JsonElement>();
        return clients.GetArrayLength() > 0
            ? clients[0].GetProperty("id").GetString()
            : null;
    }

    public async Task<string> GetClientScopeIdAsync(string adminToken, string clientScopeName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/admin/realms/{realm}/client-scopes");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var scopes = await response.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var scope in scopes.EnumerateArray())
        {
            if (scope.GetProperty("name").GetString() == clientScopeName)
            {
                return scope.GetProperty("id").GetString()
                    ?? throw new InvalidOperationException($"Client scope '{clientScopeName}' sem id.");
            }
        }

        throw new InvalidOperationException($"Client scope '{clientScopeName}' não encontrado (import do realm falhou/parcial).");
    }

    /// <summary>
    /// Altera, via Admin REST API real do Keycloak (não injeção de opções de
    /// JwtBearer), o valor de audience emitido pelo mapper
    /// <c>oidc-audience-mapper</c> de um client scope real — usado só pela
    /// prova de que uma nova audience configurada via SSM realmente fica
    /// operacional após o restart (um token novo, emitido com essa audience,
    /// passa a ser aceito).
    /// </summary>
    public async Task SetAudienceMapperValueAsync(string adminToken, string clientScopeName, string mapperName, string audienceValue)
    {
        var scopeId = await GetClientScopeIdAsync(adminToken, clientScopeName);

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"{baseUrl}/admin/realms/{realm}/client-scopes/{scopeId}/protocol-mappers/models");
        listRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

        using var listResponse = await httpClient.SendAsync(listRequest);
        listResponse.EnsureSuccessStatusCode();
        var mappers = await listResponse.Content.ReadFromJsonAsync<JsonElement>();

        string? mapperId = null;
        foreach (var mapper in mappers.EnumerateArray())
        {
            if (mapper.GetProperty("name").GetString() == mapperName)
            {
                mapperId = mapper.GetProperty("id").GetString();
                break;
            }
        }

        if (mapperId is null)
        {
            throw new InvalidOperationException($"Protocol mapper '{mapperName}' não encontrado no client scope '{clientScopeName}'.");
        }

        var updatedMapper = new
        {
            id = mapperId,
            name = mapperName,
            protocol = "openid-connect",
            protocolMapper = "oidc-audience-mapper",
            config = new Dictionary<string, string>
            {
                ["included.custom.audience"] = audienceValue,
                ["access.token.claim"] = "true",
                ["id.token.claim"] = "false"
            }
        };

        using var putRequest = new HttpRequestMessage(
            HttpMethod.Put,
            $"{baseUrl}/admin/realms/{realm}/client-scopes/{scopeId}/protocol-mappers/models/{mapperId}")
        {
            Content = JsonContent.Create(updatedMapper)
        };
        putRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

        using var putResponse = await httpClient.SendAsync(putRequest);
        putResponse.EnsureSuccessStatusCode();
    }

    public async Task<string> GetOrCreateClientSecretAsync(string adminToken, string clientId)
    {
        var uuid = await GetClientUuidAsync(adminToken, clientId)
            ?? throw new InvalidOperationException($"Client '{clientId}' não encontrado (import do realm falhou/parcial).");

        using var getRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"{baseUrl}/admin/realms/{realm}/clients/{uuid}/client-secret");
        getRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

        using var getResponse = await httpClient.SendAsync(getRequest);
        getResponse.EnsureSuccessStatusCode();
        var existing = await getResponse.Content.ReadFromJsonAsync<JsonElement>();

        if (existing.TryGetProperty("value", out var existingValue) && existingValue.GetString() is { Length: > 0 } secret)
        {
            return secret;
        }

        using var postRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{baseUrl}/admin/realms/{realm}/clients/{uuid}/client-secret");
        postRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

        using var postResponse = await httpClient.SendAsync(postRequest);
        postResponse.EnsureSuccessStatusCode();
        var created = await postResponse.Content.ReadFromJsonAsync<JsonElement>();

        return created.GetProperty("value").GetString()
            ?? throw new InvalidOperationException($"Não foi possível gerar o secret de '{clientId}'.");
    }
}
