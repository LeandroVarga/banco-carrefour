namespace BancoCarrefour.Ledger.Domain;

public readonly record struct BusinessDate(DateOnly Value)
{
    private static readonly TimeZoneInfo SaoPauloTimeZone = FindSaoPauloTimeZone();

    public static BusinessDate FromOccurredAt(DateTimeOffset occurredAt)
    {
        if (occurredAt == default)
        {
            throw new DomainValidationException("occurredAt é obrigatório.");
        }

        var localDateTime = TimeZoneInfo.ConvertTime(occurredAt.ToUniversalTime(), SaoPauloTimeZone);

        return new BusinessDate(DateOnly.FromDateTime(localDateTime.DateTime));
    }

    private static TimeZoneInfo FindSaoPauloTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
        }
    }
}
