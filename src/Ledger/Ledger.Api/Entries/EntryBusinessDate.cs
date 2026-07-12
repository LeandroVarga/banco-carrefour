namespace BancoCarrefour.Ledger.Api.Entries;

internal static class EntryBusinessDate
{
    private static readonly TimeZoneInfo SaoPauloTimeZone = FindSaoPauloTimeZone();

    public static DateOnly FromOccurredAt(DateTimeOffset occurredAt)
    {
        var localDateTime = TimeZoneInfo.ConvertTime(occurredAt, SaoPauloTimeZone);

        return DateOnly.FromDateTime(localDateTime.DateTime);
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
