using BancoCarrefour.MigrationRunner;
using Xunit;

namespace BancoCarrefour.MigrationRunner.Tests;

/// <summary>
/// O gate de aprovacao do comando "contract" nunca havia sido testado
/// diretamente - so os testes de concorrencia do lock (Testcontainers)
/// existiam. A checagem em ContractCommand.RunAsync roda ANTES de
/// qualquer resolucao de connection string/conexao ao banco, entao estes
/// testes chamam o metodo real sem nenhuma infraestrutura (nunca um
/// dublê/mensageiro do comportamento - o proprio codigo de producao).
/// </summary>
public sealed class ContractApprovalGateTests
{
    [Fact]
    public async Task Contract_deve_recusar_sem_approved_by()
    {
        var options = new CliOptions
        {
            Command = "contract",
            Boundary = MigrationBoundary.Ledger,
            ApprovedBy = null,
            CompatibilityWindowClosed = true,
        };

        var exitCode = await ContractCommand.RunAsync(options, CancellationToken.None);

        Assert.Equal(ExitCode.UsageOrConfigurationError, exitCode);
    }

    [Fact]
    public async Task Contract_deve_recusar_sem_compatibility_window_closed()
    {
        var options = new CliOptions
        {
            Command = "contract",
            Boundary = MigrationBoundary.Ledger,
            ApprovedBy = "alice",
            CompatibilityWindowClosed = false,
        };

        var exitCode = await ContractCommand.RunAsync(options, CancellationToken.None);

        Assert.Equal(ExitCode.UsageOrConfigurationError, exitCode);
    }

    [Fact]
    public async Task Contract_deve_recusar_com_approved_by_em_branco()
    {
        var options = new CliOptions
        {
            Command = "contract",
            Boundary = MigrationBoundary.Ledger,
            ApprovedBy = "   ",
            CompatibilityWindowClosed = true,
        };

        var exitCode = await ContractCommand.RunAsync(options, CancellationToken.None);

        Assert.Equal(ExitCode.UsageOrConfigurationError, exitCode);
    }

    [Theory]
    [InlineData("migrate")]
    [InlineData("contract")]
    public void Parse_deve_exigir_boundary_para_migrate_e_contract(string command)
    {
        var ex = Assert.Throws<ArgumentException>(() => CliOptions.Parse([command]));
        Assert.Contains("--boundary", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_deve_rejeitar_comando_desconhecido()
    {
        Assert.Throws<ArgumentException>(() => CliOptions.Parse(["deploy"]));
    }

    [Fact]
    public void Parse_nunca_deve_exigir_boundary_para_backfill()
    {
        // "backfill" varre AMBAS as fronteiras (BackfillCommand), nunca
        // uma so - diferente de migrate/contract, que operam sempre em
        // exatamente uma fronteira por invocação.
        var options = CliOptions.Parse(["backfill"]);

        Assert.Equal("backfill", options.Command);
        Assert.Null(options.Boundary);
    }
}
