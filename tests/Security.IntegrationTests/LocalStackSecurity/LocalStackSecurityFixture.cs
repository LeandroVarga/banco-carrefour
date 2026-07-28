using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Xunit;

namespace BancoCarrefour.Security.IntegrationTests.LocalStackSecurity;

[CollectionDefinition(Name)]
public sealed class LocalStackSecurityCollection : ICollectionFixture<LocalStackSecurityFixture>
{
    public const string Name = "LocalStackSecurity";
}

/// <summary>
/// Sobe um LocalStack real (mesmo digest validado por capability spike
/// empírico) e aplica
/// os MÓDULOS TERRAFORM REAIS (infra/terraform/environments/localstack-hobby)
/// dentro de um container terraform, copiando a árvore para um diretório
/// temporário DENTRO do container (mesma técnica do serviço
/// "terraform-provisioner" do docker-compose.yml) - nunca escreve
/// .terraform/terraform.tfstate na árvore real do repositório no host.
/// Nenhum valor de secret é gerado aqui além de senhas de teste efêmeras,
/// usadas só nesta fixture.
/// </summary>
public sealed class LocalStackSecurityFixture : IAsyncLifetime
{
    public const string Region = "us-east-1";
    public const string AccessKey = "test";
    public const string SecretKey = "test";

    public const string LedgerApiSecretName = "banco-carrefour/ledger-api/db-credentials";
    public const string LedgerOutboxPublisherSecretName = "banco-carrefour/ledger-outbox-publisher/db-credentials";
    public const string ConsolidationApiSecretName = "banco-carrefour/consolidation-api/db-credentials";
    public const string ConsolidationWorkerSecretName = "banco-carrefour/consolidation-worker/db-credentials";

    private const string TerraformWorkDir = "/tmp/terraform/environments/localstack-hobby";

    private INetwork network = null!;
    private IContainer localStack = null!;
    private IContainer terraformRunner = null!;
    private IContainer awsCliRunner = null!;

    public string ServiceUrl => $"http://{localStack.Hostname}:{localStack.GetMappedPublicPort(4566)}";

    public Dictionary<string, string> SecretArns { get; } = new();
    public Dictionary<string, string> ParameterValues { get; } = new();
    public string KmsKeyId { get; private set; } = string.Empty;
    public Dictionary<string, string> IamRoleArns { get; } = new();

    public async Task InitializeAsync()
    {
        network = new NetworkBuilder().Build();
        await network.CreateAsync();

        // Mesma versão exata fixada em docker-compose.yml (digest
        // sha256:3ebc37595918b8accb852f8048fef2aff047d465167edd655528065b07bc364a) -
        // WithImage(string) não aceita "tag@digest" no Testcontainers 3.10.0
        // (ver comentário equivalente em IdentityFixture.cs para o Keycloak).
        localStack = new ContainerBuilder()
            .WithImage("localstack/localstack:4.14.0")
            .WithNetwork(network)
            .WithNetworkAliases("localstack")
            // A mesma lista de SERVICES do docker-compose.yml real: o
            // ambiente localstack-hobby aplica TODOS os módulos juntos
            // (messaging incluído), não apenas os deste bloco.
            .WithEnvironment("SERVICES", "sqs,secretsmanager,ssm,kms,iam")
            .WithEnvironment("AWS_DEFAULT_REGION", Region)
            .WithEnvironment("DEFAULT_REGION", Region)
            .WithPortBinding(4566, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
                .ForPort(4566)
                .ForPath("/_localstack/health")))
            .WithCleanUp(true)
            .Build();

        await localStack.StartAsync();

        var repositoryRoot = LocateRepositoryRoot();

        terraformRunner = new ContainerBuilder()
            .WithImage("hashicorp/terraform:1.9")
            .WithNetwork(network)
            .WithEntrypoint("sh", "-c", "sleep 900")
            .WithBindMount(Path.Combine(repositoryRoot, "infra", "terraform"), "/workspace/infra/terraform", AccessMode.ReadOnly)
            .WithEnvironment("AWS_ACCESS_KEY_ID", AccessKey)
            .WithEnvironment("AWS_SECRET_ACCESS_KEY", SecretKey)
            .WithEnvironment("AWS_DEFAULT_REGION", Region)
            .WithEnvironment("TF_VAR_localstack_endpoint", "http://localstack:4566")
            .WithCleanUp(true)
            .Build();

        await terraformRunner.StartAsync();

        // Mesma imagem pinada do docker-compose.yml (secret-value-bootstrap) -
        // a imagem do Terraform não tem AWS CLI instalada.
        awsCliRunner = new ContainerBuilder()
            .WithImage("amazon/aws-cli:2.31.13")
            .WithNetwork(network)
            .WithEntrypoint("sh", "-c", "sleep 900")
            .WithEnvironment("AWS_ACCESS_KEY_ID", AccessKey)
            .WithEnvironment("AWS_SECRET_ACCESS_KEY", SecretKey)
            .WithEnvironment("AWS_DEFAULT_REGION", Region)
            .WithCleanUp(true)
            .Build();

        await awsCliRunner.StartAsync();

        // Copia para um diretório gravável dentro do container - nunca escreve
        // .terraform/terraform.tfstate na árvore real do repositório no host
        // (mesma técnica do serviço "terraform-provisioner" do docker-compose.yml).
        // Auditoria (fechamento pré-publicação, ): "cp -R" copia
        // symlinks COMO symlinks (nunca dereferencia por padrão) - se a
        // árvore infra/terraform do HOST tiver sido usada com
        // scripts/ci/terraform-cached.sh (cache compartilhado de provider via
        // TF_PLUGIN_CACHE_DIR), ".terraform/providers/.../linux_amd64" no
        // host é um symlink para um caminho absoluto ("/root/.terraform.d/plugin-cache/...")
        // que só existe DENTRO do container específico que o criou - copiado
        // para este container diferente, vira um symlink quebrado, e
        // "terraform init" recusa reaproveitar um pacote de provider
        // inválido (reproduzido e confirmado: "Required plugins are not
        // installed"). Corrigido removendo qualquer ".terraform" residual
        // da copia ANTES do init - esta fixture já tinha a intenção de nunca
        // depender de estado prévio (nunca escreve no host), então também
        // não deve depender de estado prévio ao COPIAR do host.
        await ExecOrThrowAsync("rm -rf /tmp/terraform && cp -R /workspace/infra/terraform /tmp/terraform && rm -rf /tmp/terraform/environments/*/.terraform", "preparar diretório terraform temporário");
        await ExecOrThrowAsync($"cd {TerraformWorkDir} && terraform init -input=false", "terraform init");
        await ExecOrThrowAsync($"cd {TerraformWorkDir} && terraform apply -input=false -auto-approve", "terraform apply");

        await LoadOutputsAsync();
    }

    public async Task<ExecResult> RunSecretValueBootstrapAsync(
        string repositoryRoot,
        string ledgerApiPassword,
        string ledgerOutboxPublisherPassword,
        string consolidationApiReadonlyPassword,
        string consolidationWorkerPassword)
    {
        var scriptPath = Path.Combine(repositoryRoot, "scripts", "security", "secret-value-bootstrap-impl.sh");
        var scriptBytes = await File.ReadAllBytesAsync(scriptPath);

        // O script real (não uma reimplementação) é copiado para o container
        // com AWS CLI, na mesma rede que já tem acesso ao LocalStack desta fixture.
        await awsCliRunner.CopyAsync(scriptBytes, "/tmp/secret-value-bootstrap-impl.sh");
        var chmodResult = await awsCliRunner.ExecAsync(["sh", "-c", "chmod +x /tmp/secret-value-bootstrap-impl.sh"]);
        if (chmodResult.ExitCode != 0)
        {
            throw new InvalidOperationException($"Falha em 'chmod do script real' (exit {chmodResult.ExitCode}): {chmodResult.Stderr}");
        }

        return await awsCliRunner.ExecAsync(
        [
            "sh", "-c",
            "AWS_ENDPOINT_URL=http://localstack:4566 " +
            $"LEDGER_API_PASSWORD='{ledgerApiPassword}' " +
            $"LEDGER_OUTBOX_PUBLISHER_PASSWORD='{ledgerOutboxPublisherPassword}' " +
            $"CONSOLIDATION_API_READONLY_PASSWORD='{consolidationApiReadonlyPassword}' " +
            $"CONSOLIDATION_WORKER_PASSWORD='{consolidationWorkerPassword}' " +
            "sh /tmp/secret-value-bootstrap-impl.sh"
        ]);
    }

    public async Task<string> ReadTerraformStateRawAsync()
    {
        var result = await terraformRunner.ExecAsync(["sh", "-c", $"cat {TerraformWorkDir}/terraform.tfstate"]);
        Assert.True(result.ExitCode == 0, $"Falha ao ler terraform.tfstate: {result.Stderr}");
        return result.Stdout;
    }

    private async Task LoadOutputsAsync()
    {
        var result = await terraformRunner.ExecAsync(["sh", "-c", $"cd {TerraformWorkDir} && terraform output -json"]);
        Assert.True(result.ExitCode == 0, $"terraform output falhou: {result.Stderr}");

        using var document = JsonDocument.Parse(result.Stdout);
        var root = document.RootElement;

        foreach (var property in root.GetProperty("secret_arns").GetProperty("value").EnumerateObject())
        {
            SecretArns[property.Name] = property.Value.GetString()!;
        }

        foreach (var property in root.GetProperty("parameter_names").GetProperty("value").EnumerateObject())
        {
            ParameterValues[property.Name] = property.Value.GetString()!;
        }

        foreach (var property in root.GetProperty("iam_role_arns").GetProperty("value").EnumerateObject())
        {
            IamRoleArns[property.Name] = property.Value.GetString()!;
        }

        KmsKeyId = root.GetProperty("kms_key_id").GetProperty("value").GetString()!;
    }

    private async Task ExecOrThrowAsync(string shellCommand, string step)
    {
        var result = await terraformRunner.ExecAsync(["sh", "-c", shellCommand]);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Falha em '{step}' (exit {result.ExitCode}): {result.Stderr}");
        }
    }

    public async Task DisposeAsync()
    {
        var exceptions = new List<Exception>();

        try
        {
            await ExecOrThrowAsync($"cd {TerraformWorkDir} && terraform destroy -input=false -auto-approve", "terraform destroy");
        }
        catch (Exception ex)
        {
            exceptions.Add(ex);
        }

        await DisposeSafelyAsync(() => awsCliRunner is null ? Task.CompletedTask : awsCliRunner.DisposeAsync().AsTask(), exceptions);
        await DisposeSafelyAsync(() => terraformRunner is null ? Task.CompletedTask : terraformRunner.DisposeAsync().AsTask(), exceptions);
        await DisposeSafelyAsync(() => localStack is null ? Task.CompletedTask : localStack.DisposeAsync().AsTask(), exceptions);
        await DisposeSafelyAsync(() => network is null ? Task.CompletedTask : network.DeleteAsync(), exceptions);

        if (exceptions.Count > 0)
        {
            throw new AggregateException("Falha ao descartar recursos da LocalStackSecurityFixture.", exceptions);
        }
    }

    private static async Task DisposeSafelyAsync(Func<Task> action, List<Exception> exceptions)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            exceptions.Add(ex);
        }
    }

    public static string LocateRepositoryRoot()
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
