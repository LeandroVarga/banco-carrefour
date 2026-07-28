namespace BancoCarrefour.MigrationRunner;

public sealed class CliOptions
{
    public required string Command { get; init; }
    public MigrationBoundary? Boundary { get; init; }
    public string Environment { get; init; } = "Production";
    public int LockTimeoutSeconds { get; init; } = 300;
    public int PollIntervalMilliseconds { get; init; } = 2000;
    public string ReleaseId { get; init; } = "unknown";
    public string SourceCommit { get; init; } = "unknown";
    public string? ApprovedBy { get; init; }
    public bool CompatibilityWindowClosed { get; init; }

    /// <summary>
    /// Nome/ARN do secret do Secrets Manager a usar nesta execução -
    /// necessariamente informado por invocação (nunca fixo na task
    /// definition), porque a MESMA task definition serve tanto Ledger
    /// quanto Consolidation (cada fronteira usa um secret de credenciais de
    /// migração distinto) - ver módulo Terraform ecs-migration-task e o
    /// "aws ecs run-task --overrides" nos workflows de deploy.
    /// </summary>
    public string? SecretName { get; init; }

    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException("Comando obrigatório: migrate | contract | backfill.");
        }

        var command = args[0].Trim().ToLowerInvariant();
        if (command is not ("migrate" or "contract" or "backfill"))
        {
            throw new ArgumentException($"Comando desconhecido: '{args[0]}'. Use migrate, contract ou backfill.");
        }

        MigrationBoundary? boundary = null;
        var environment = System.Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
        var lockTimeoutSeconds = 300;
        var pollIntervalMs = 2000;
        var releaseId = System.Environment.GetEnvironmentVariable("RELEASE_ID") ?? "unknown";
        var sourceCommit = System.Environment.GetEnvironmentVariable("SOURCE_COMMIT_SHA") ?? "unknown";
        string? approvedBy = null;
        var compatibilityWindowClosed = false;
        string? secretName = System.Environment.GetEnvironmentVariable("SecretsManager__SecretName");

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--boundary":
                    boundary = MigrationBoundaryExtensions.Parse(RequireValue(args, ref i, "--boundary"));
                    break;
                case "--environment":
                    environment = RequireValue(args, ref i, "--environment");
                    break;
                case "--lock-timeout-seconds":
                    lockTimeoutSeconds = int.Parse(RequireValue(args, ref i, "--lock-timeout-seconds"));
                    break;
                case "--poll-interval-milliseconds":
                    pollIntervalMs = int.Parse(RequireValue(args, ref i, "--poll-interval-milliseconds"));
                    break;
                case "--release-id":
                    releaseId = RequireValue(args, ref i, "--release-id");
                    break;
                case "--source-commit":
                    sourceCommit = RequireValue(args, ref i, "--source-commit");
                    break;
                case "--approved-by":
                    approvedBy = RequireValue(args, ref i, "--approved-by");
                    break;
                case "--compatibility-window-closed":
                    compatibilityWindowClosed = true;
                    break;
                case "--secret-name":
                    secretName = RequireValue(args, ref i, "--secret-name");
                    break;
                default:
                    throw new ArgumentException($"Argumento desconhecido: '{args[i]}'.");
            }
        }

        if (command is "migrate" or "contract" && boundary is null)
        {
            throw new ArgumentException("--boundary Ledger|Consolidation é obrigatório para os comandos migrate/contract.");
        }

        return new CliOptions
        {
            Command = command,
            Boundary = boundary,
            Environment = environment,
            LockTimeoutSeconds = lockTimeoutSeconds,
            PollIntervalMilliseconds = pollIntervalMs,
            ReleaseId = releaseId,
            SourceCommit = sourceCommit,
            ApprovedBy = approvedBy,
            CompatibilityWindowClosed = compatibilityWindowClosed,
            SecretName = secretName,
        };
    }

    private static string RequireValue(string[] args, ref int i, string flagName)
    {
        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"{flagName} exige um valor.");
        }

        i++;
        return args[i];
    }
}
