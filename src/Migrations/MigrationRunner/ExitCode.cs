namespace BancoCarrefour.MigrationRunner;

/// <summary>
/// Códigos de saída determinísticos - o workflow de deploy (ver
/// .github/workflows/{deploy-development,promote-staging,promote-production}.yml)
/// depende de "0" para prosseguir com o deploy da aplicação; qualquer outro
/// valor bloqueia o deploy (nunca apenas "diferente de zero" tratado de
/// forma genérica - cada código tem um significado auditável específico).
/// </summary>
public static class ExitCode
{
    public const int Success = 0;
    public const int MigrationFailed = 1;
    public const int LockTimedOut = 2;
    public const int UsageOrConfigurationError = 3;
    public const int RefusedUnapprovedPhase = 4;
    public const int BackfillIncomplete = 5;
}
