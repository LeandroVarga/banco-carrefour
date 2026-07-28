using System.Text;
using BancoCarrefour.Security.IntegrationTests.Shared;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Xunit;

namespace BancoCarrefour.Security.IntegrationTests.Edge;

[CollectionDefinition(Name)]
public sealed class EdgeSecurityCollection : ICollectionFixture<EdgeFixture>
{
    public const string Name = "EdgeSecurity";
}

/// <summary>
/// Sobe o mesmo container/imagem/configuração do edge-proxy real
/// (<c>owasp/modsecurity-crs:nginx</c>, <c>infra/edge-proxy/default.conf.template</c>,
/// <c>infra/edge-proxy/95-stage-tls-key.sh</c>) com upstreams de teste
/// (busybox httpd, mesma imagem alpine já pinada em
/// <c>scripts/security/bootstrap-local-security.sh</c>/docker-compose)
/// registrados nos aliases de rede que o template real espera
/// (<c>ledger-api</c>, <c>consolidation-api</c>, <c>keycloak</c>) — sem
/// alterar uma linha da configuração nginx/ModSecurity real. Certificado
/// TLS efêmero, gerado em memória (nunca os de <c>.local/security/</c>).
/// </summary>
public sealed class EdgeFixture : IAsyncLifetime
{
    // Mesma tag fixada em docker-compose.yml (owasp/modsecurity-crs:nginx,
    // digest sha256:2051ff18b836c1d9bbc5c7754451c1687ea27352e497b89d0c9fc7e657861e07).
    // Testcontainers 3.10.0 não aceita o formato combinado "tag@digest" em
    // WithImage(string) — ver nota equivalente em IdentityFixture.
    private const string EdgeProxyImage = "owasp/modsecurity-crs:nginx";
    private const string UpstreamImage = "alpine:3.20.3";

    private readonly EphemeralCertificateAuthority certificateAuthority = new();
    private INetwork network = null!;
    private IContainer edgeProxy = null!;
    private readonly List<IContainer> upstreams = [];

    public string HttpsBaseUrl => $"https://127.0.0.1:{edgeProxy.GetMappedPublicPort(8443)}";
    public string HttpBaseUrl => $"http://127.0.0.1:{edgeProxy.GetMappedPublicPort(8080)}";
    public ushort HttpsPort => edgeProxy.GetMappedPublicPort(8443);
    public ushort HttpPort => edgeProxy.GetMappedPublicPort(8080);

    public HttpClientHandler CreateTrustedHandler()
    {
        return new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = certificateAuthority.ValidateServerCertificate,
            AllowAutoRedirect = false
        };
    }

    public Task<DotNet.Testcontainers.Containers.ExecResult> ExecInEdgeProxyAsync(params string[] command)
    {
        return edgeProxy.ExecAsync(command);
    }

    public async Task InitializeAsync()
    {
        network = new NetworkBuilder().Build();
        await network.CreateAsync();

        var repositoryRoot = LocateRepositoryRoot();
        var leaf = certificateAuthority.IssueLeafCertificate("localhost", "127.0.0.1");

        const UnixFileModes ReadOnlyFileMode = UnixFileModes.UserRead | UnixFileModes.UserWrite
            | UnixFileModes.GroupRead | UnixFileModes.OtherRead;
        const UnixFileModes ExecutableFileMode = ReadOnlyFileMode
            | UnixFileModes.UserExecute | UnixFileModes.GroupExecute | UnixFileModes.OtherExecute;

        var nginxTemplateBytes = await File.ReadAllBytesAsync(
            Path.Combine(repositoryRoot, "infra", "edge-proxy", "default.conf.template"));
        var stageTlsKeyScriptBytes = await File.ReadAllBytesAsync(
            Path.Combine(repositoryRoot, "infra", "edge-proxy", "95-stage-tls-key.sh"));

        upstreams.Add(BuildUpstreamEchoContainer("ledger-api"));
        upstreams.Add(BuildUpstreamEchoContainer("consolidation-api"));
        upstreams.Add(BuildUpstreamEchoContainer("keycloak"));

        await Task.WhenAll(upstreams.Select(container => container.StartAsync()));

        edgeProxy = new ContainerBuilder()
            .WithImage(EdgeProxyImage)
            .WithNetwork(network)
            .WithEnvironment("MODSEC_RULE_ENGINE", "on")
            .WithEnvironment("PORT", "8080")
            .WithEnvironment("SSL_PORT", "8443")
            .WithEnvironment("SERVER_NAME", "localhost")
            .WithEnvironment("SSL_CERT_KEY_FILE", "/etc/nginx/server.key")
            .WithResourceMapping(
                nginxTemplateBytes,
                "/etc/nginx/templates/conf.d/default.conf.template",
                ReadOnlyFileMode)
            .WithResourceMapping(
                stageTlsKeyScriptBytes,
                "/docker-entrypoint.d/95-stage-tls-key.sh",
                ExecutableFileMode)
            .WithResourceMapping(Encoding.UTF8.GetBytes(leaf.CertificatePem), "/etc/nginx/conf/server.crt", ReadOnlyFileMode)
            .WithResourceMapping(Encoding.UTF8.GetBytes(leaf.PrivateKeyPem), "/run/edge-secrets/server.key", ReadOnlyFileMode)
            .WithPortBinding(8443, true)
            .WithPortBinding(8080, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(8443))
            .WithCleanUp(true)
            .Build();

        await edgeProxy.StartAsync();

        // A imagem entrega o healthcheck HTTPS assim que o entrypoint termina
        // de escrever a config renderizada e o nginx sobe — aguardamos uma
        // resposta real (qualquer status) em vez de confiar só na porta TCP.
        await WaitForNginxReadyAsync();
    }

    private async Task WaitForNginxReadyAsync()
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                using var response = await client.GetAsync($"{HttpsBaseUrl}/");
                return;
            }
            catch
            {
                await Task.Delay(1000);
            }
        }

        throw new TimeoutException("edge-proxy não respondeu em HTTPS a tempo.");
    }

    private IContainer BuildUpstreamEchoContainer(string alias)
    {
        // httpd do pacote busybox-extras (o "busybox" base do Alpine não
        // inclui o applet httpd — confirmado por execução real: "apk add
        // busybox-extras" instala um binário separado em /usr/sbin/httpd,
        // que precisa ser chamado diretamente, não via "busybox httpd")
        // servindo um corpo fixo — "test double" de upstream explicitamente
        // permitido pelo escopo desta etapa: prova apenas se a requisição
        // alcançou o upstream, sem reimplementar Ledger.Api ou
        // Consolidation.Api.
        var marker = $"UPSTREAM-HIT:{alias}";
        var command = $"apk add --no-cache busybox-extras >/dev/null 2>&1 && mkdir -p /www && printf '%s' '{marker}' > /www/index.html && exec httpd -f -v -p 8080 -h /www";

        return new ContainerBuilder()
            .WithImage(UpstreamImage)
            .WithNetwork(network)
            .WithNetworkAliases(alias)
            .WithEntrypoint("sh", "-c")
            .WithCommand(command)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(8080))
            .WithCleanUp(true)
            .Build();
    }

    public async Task DisposeAsync()
    {
        var exceptions = new List<Exception>();

        await DisposeSafelyAsync(edgeProxy.DisposeAsync, exceptions);

        foreach (var upstream in upstreams)
        {
            await DisposeSafelyAsync(upstream.DisposeAsync, exceptions);
        }

        await DisposeSafelyAsync(() => network.DeleteAsync(), exceptions);
        certificateAuthority.Dispose();

        if (exceptions.Count > 0)
        {
            throw new AggregateException("Falha ao descartar um ou mais recursos da fixture do edge-proxy.", exceptions);
        }
    }

    private static async Task DisposeSafelyAsync(Func<Task> disposeAsync, List<Exception> exceptions)
    {
        try
        {
            await disposeAsync();
        }
        catch (Exception exception)
        {
            exceptions.Add(exception);
        }
    }

    private static async Task DisposeSafelyAsync(Func<ValueTask> disposeAsync, List<Exception> exceptions)
    {
        try
        {
            await disposeAsync();
        }
        catch (Exception exception)
        {
            exceptions.Add(exception);
        }
    }

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BancoCarrefour.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Não foi possível localizar a raiz do repositório.");
    }
}
