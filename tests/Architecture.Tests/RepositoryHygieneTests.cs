using System.Diagnostics;
using Xunit;

namespace BancoCarrefour.Architecture.Tests;

public sealed class RepositoryHygieneTests
{
    private static readonly string[] ForbiddenDirectorySegments =
    [
        ".claude",
        ".codex",
        ".cursor",
        ".continue",
        ".windsurf",
        ".gemini",
        ".roo",
        ".junie",
        ".amazonq",
        ".qodo"
    ];

    private static readonly string[] ForbiddenFileNames =
    [
        "CLAUDE.md",
        "CLAUDE.local.md",
        "AGENTS.md",
        "AGENTS.local.md",
        "GEMINI.md",
        ".mcp.json",
        ".mcp.local.json"
    ];

    private const string ForbiddenFileNamePrefix = ".aider";
    private const string ForbiddenGithubFile = ".github/copilot-instructions.md";
    private const string ForbiddenGithubDirectoryPrefix = ".github/instructions/";

    [Fact]
    public void Nenhum_arquivo_rastreado_deve_corresponder_a_artefato_local_de_assistente_ou_ferramenta()
    {
        var trackedPaths = ListGitTrackedPaths();

        var violations = trackedPaths.Where(IsForbidden).ToArray();

        Assert.True(
            violations.Length == 0,
            "Caminhos proibidos rastreados pelo Git (configuração local de assistentes/IDEs/ferramentas):"
                + Environment.NewLine
                + string.Join(Environment.NewLine, violations));
    }

    private static bool IsForbidden(string path)
    {
        var normalized = path.Replace('\\', '/');
        var segments = normalized.Split('/');
        var fileName = segments[^1];
        var directorySegments = segments.Take(segments.Length - 1);

        if (directorySegments.Any(segment => ForbiddenDirectorySegments.Contains(segment, StringComparer.Ordinal)))
        {
            return true;
        }

        if (ForbiddenFileNames.Contains(fileName, StringComparer.Ordinal))
        {
            return true;
        }

        if (fileName.StartsWith(ForbiddenFileNamePrefix, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(normalized, ForbiddenGithubFile, StringComparison.Ordinal))
        {
            return true;
        }

        if (normalized.StartsWith(ForbiddenGithubDirectoryPrefix, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static IReadOnlyCollection<string> ListGitTrackedPaths()
    {
        var startInfo = new ProcessStartInfo("git", "ls-files")
        {
            WorkingDirectory = RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Não foi possível iniciar o processo 'git ls-files'.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'git ls-files' falhou com código de saída {process.ExitCode}. Erro: {error}");
        }

        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim('\r'))
            .ToArray();
    }

    private static string RepositoryRoot { get; } = LocateRepositoryRoot();

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
