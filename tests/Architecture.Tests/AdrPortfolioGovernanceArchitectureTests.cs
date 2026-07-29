using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;

namespace BancoCarrefour.Architecture.Tests;

public sealed class AdrPortfolioGovernanceArchitectureTests
{
    private const int ExpectedAdrCount = 16;

    private static readonly string[] RequiredSectionHeadings =
    [
        "Contexto",
        "Pergunta arquitetural",
        "Decisão",
        "Alternativas consideradas",
        "Trade-offs",
        "Consequências",
        "Guardrails",
        "Risco arquitetural evitado",
        "ASRs relacionados",
        "ABBs e SBBs relacionados",
        "Evidências de implementação",
        "ADRs relacionados"
    ];

    private static readonly string[] ThisTestFileAllowList =
    [
        "tests/Architecture.Tests/AdrPortfolioGovernanceArchitectureTests.cs"
    ];

    [Fact]
    public void Portfolio_deve_conter_exatamente_16_ADRs_numeradas_sequencialmente_de_0000_a_0015()
    {
        var adrIds = ListAdrFileIds();

        var expected = Enumerable.Range(0, ExpectedAdrCount)
            .Select(i => $"ADR-{i:D4}")
            .ToArray();

        Assert.Equal(
            expected,
            adrIds.OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Nenhum_arquivo_de_ADR_fora_do_intervalo_0000_0015_deve_estar_rastreado()
    {
        var trackedDecisionFiles = ListGitTrackedPaths()
            .Where(p => p.StartsWith("docs/decisions/ADR-", StringComparison.Ordinal))
            .ToArray();

        var adrFileNamePattern = new Regex(@"^docs/decisions/ADR-(\d{4})-", RegexOptions.Compiled);

        var outOfRange = trackedDecisionFiles
            .Select(p => adrFileNamePattern.Match(p))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value))
            .Where(number => number < 0 || number >= ExpectedAdrCount)
            .Distinct()
            .OrderBy(n => n)
            .ToArray();

        Assert.True(
            outOfRange.Length == 0,
            "Arquivos de ADR fora do portfólio canônico (0000-0015) ainda rastreados: "
                + string.Join(", ", outOfRange.Select(n => $"ADR-{n:D4}")));
    }

    [Fact]
    public void Toda_ADR_deve_ter_status_Aceita_no_frontmatter_e_nenhum_status_de_substituicao()
    {
        foreach (var (id, path, content) in ReadAllAdrs())
        {
            var statusMatch = Regex.Match(content, @"^status:\s*(.+)$", RegexOptions.Multiline);

            Assert.True(statusMatch.Success, $"{path}: frontmatter sem campo 'status'.");

            var status = statusMatch.Groups[1].Value.Trim();

            Assert.Equal("Aceita", status);
            Assert.DoesNotContain("Substitu", status, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Toda_ADR_deve_conter_as_12_secoes_obrigatorias_na_ordem_esperada()
    {
        foreach (var (id, path, content) in ReadAllAdrs())
        {
            var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);

            var headingIndexes = RequiredSectionHeadings
                .Select((heading, i) =>
                {
                    var headingPattern = new Regex(
                        $@"^##\s*{i + 1}\.\s*{Regex.Escape(heading)}\s*$",
                        RegexOptions.Multiline);
                    var match = headingPattern.Match(normalized);
                    return (heading, index: match.Success ? match.Index : -1);
                })
                .ToArray();

            var missing = headingIndexes.Where(h => h.index < 0).Select(h => h.heading).ToArray();
            Assert.True(missing.Length == 0, $"{path}: seções ausentes ou fora do formato numerado esperado: {string.Join(", ", missing)}.");

            for (var i = 1; i < headingIndexes.Length; i++)
            {
                Assert.True(
                    headingIndexes[i].index > headingIndexes[i - 1].index,
                    $"{path}: seção '{headingIndexes[i].heading}' aparece fora de ordem em relação a '{headingIndexes[i - 1].heading}'.");
            }
        }
    }

    [Fact]
    public void Registro_de_decisoes_deve_listar_exatamente_as_16_ADRs_do_portfolio_sem_duplicatas()
    {
        var registerContent = ReadRepositoryFile("docs/decisions/registro-de-decisoes.md");
        var referencedIds = ExtractAdrReferences(registerContent);

        var fileIds = ListAdrFileIds();

        Assert.Equal(fileIds.Count, referencedIds.Count);
        Assert.Equal(
            fileIds.OrderBy(id => id, StringComparer.Ordinal),
            referencedIds.OrderBy(id => id, StringComparer.Ordinal));

        var duplicates = referencedIds
            .GroupBy(id => id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        Assert.True(duplicates.Length == 0, "ADRs duplicadas no registro: " + string.Join(", ", duplicates));
    }

    [Fact]
    public void README_de_decisoes_deve_concordar_com_o_registro_e_com_os_arquivos_reais()
    {
        var readmeContent = ReadRepositoryFile("docs/decisions/README.md");
        var referencedIds = ExtractAdrReferences(readmeContent);

        var fileIds = ListAdrFileIds();

        Assert.Equal(fileIds.Count, referencedIds.Count);
        Assert.Equal(
            fileIds.OrderBy(id => id, StringComparer.Ordinal),
            referencedIds.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void Nenhum_arquivo_rastreado_deve_referenciar_docs_evolution()
    {
        var trackedFiles = ListGitTrackedPaths();

        var hasEvolutionPath = trackedFiles.Any(p => p.StartsWith("docs/evolution/", StringComparison.Ordinal));
        Assert.False(hasEvolutionPath, "Ainda existe caminho rastreado dentro de docs/evolution/.");

        var textExtensions = new[] { ".md", ".cs", ".yml", ".yaml", ".sh", ".json", ".hcl", ".ps1" };
        var offendingFiles = new List<string>();

        foreach (var relativePath in trackedFiles.Where(p => textExtensions.Contains(Path.GetExtension(p))))
        {
            if (ThisTestFileAllowList.Contains(relativePath, StringComparer.Ordinal))
            {
                continue;
            }

            var fullPath = Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            var content = File.ReadAllText(fullPath);
            if (content.Contains("docs/evolution", StringComparison.Ordinal))
            {
                offendingFiles.Add(relativePath);
            }
        }

        Assert.True(offendingFiles.Count == 0, "Referências residuais a docs/evolution: " + string.Join(", ", offendingFiles));
    }

    [Fact]
    public void Nenhum_arquivo_rastreado_deve_citar_uma_ADR_fora_do_portfolio_canonico()
    {
        var trackedFiles = ListGitTrackedPaths();
        var textExtensions = new[] { ".md", ".cs", ".yml", ".yaml", ".sh", ".json", ".hcl", ".ps1" };
        var adrReferencePattern = new Regex(@"ADR-(\d{4})", RegexOptions.Compiled);

        var offenders = new List<string>();

        foreach (var relativePath in trackedFiles.Where(p => textExtensions.Contains(Path.GetExtension(p))))
        {
            if (ThisTestFileAllowList.Contains(relativePath, StringComparer.Ordinal))
            {
                continue;
            }

            var fullPath = Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            var content = File.ReadAllText(fullPath);
            foreach (Match match in adrReferencePattern.Matches(content))
            {
                var number = int.Parse(match.Groups[1].Value);
                if (number >= ExpectedAdrCount)
                {
                    offenders.Add($"{relativePath}: ADR-{number:D4}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Referências a ADRs fora do portfólio canônico (0000-0015): " + string.Join("; ", offenders));
    }

    [Fact]
    public void RabbitMQ_so_pode_aparecer_na_ADR_0004_ou_como_guarda_de_teste()
    {
        var allowedPaths = new[]
        {
            "docs/decisions/ADR-0004-integracao-assincrona-confiavel.md",
            "tests/Architecture.Tests/LedgerArchitectureTests.cs",
            "tests/Architecture.Tests/AdrPortfolioGovernanceArchitectureTests.cs"
        };

        var trackedFiles = ListGitTrackedPaths();
        var textExtensions = new[] { ".md", ".cs", ".yml", ".yaml", ".sh", ".json", ".hcl", ".ps1" };

        var offenders = new List<string>();

        foreach (var relativePath in trackedFiles.Where(p => textExtensions.Contains(Path.GetExtension(p))))
        {
            if (allowedPaths.Contains(relativePath, StringComparer.Ordinal))
            {
                continue;
            }

            var fullPath = Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            var content = File.ReadAllText(fullPath);
            if (content.Contains("RabbitMQ", StringComparison.OrdinalIgnoreCase))
            {
                offenders.Add(relativePath);
            }
        }

        Assert.True(offenders.Count == 0, "Referências a RabbitMQ fora dos locais permitidos: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Nenhum_documento_deve_instruir_acesso_direto_por_host_e_porta_as_APIs_de_negocio()
    {
        var trackedDocs = ListGitTrackedPaths()
            .Where(p => p.EndsWith(".md", StringComparison.Ordinal)
                || p.EndsWith(".yml", StringComparison.Ordinal)
                || p.EndsWith(".yaml", StringComparison.Ordinal))
            .ToArray();

        var directPortPattern = new Regex(@"localhost:80(80|81)\b", RegexOptions.Compiled);

        var offenders = new List<string>();

        foreach (var relativePath in trackedDocs)
        {
            var fullPath = Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            var content = File.ReadAllText(fullPath);
            if (directPortPattern.IsMatch(content))
            {
                offenders.Add(relativePath);
            }
        }

        Assert.True(offenders.Count == 0, "Documentação instruindo acesso direto por host:porta às APIs: " + string.Join(", ", offenders));
    }

    private static IReadOnlyList<(string Id, string Path, string Content)> ReadAllAdrs()
    {
        var decisionsDirectory = Path.Combine(RepositoryRoot, "docs", "decisions");
        var files = Directory.GetFiles(decisionsDirectory, "ADR-*.md", SearchOption.TopDirectoryOnly);

        return files
            .Select(f =>
            {
                var relative = Path.GetRelativePath(RepositoryRoot, f).Replace('\\', '/');
                var idMatch = Regex.Match(Path.GetFileName(f), @"^(ADR-\d{4})-");
                return (Id: idMatch.Groups[1].Value, Path: relative, Content: File.ReadAllText(f));
            })
            .OrderBy(t => t.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyCollection<string> ListAdrFileIds()
    {
        return ReadAllAdrs().Select(t => t.Id).ToArray();
    }

    private static IReadOnlyList<string> ExtractAdrReferences(string content)
    {
        var pattern = new Regex(@"\[(ADR-\d{4})\]", RegexOptions.Compiled);
        return pattern.Matches(content).Select(m => m.Groups[1].Value).ToArray();
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        return File.ReadAllText(Path.Combine(RepositoryRoot, normalized));
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
            .Select(line => line.Trim('\r').Replace('\\', '/'))
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
