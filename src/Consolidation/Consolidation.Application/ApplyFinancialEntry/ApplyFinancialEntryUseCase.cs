using System.Globalization;
using System.Text.RegularExpressions;
using BancoCarrefour.Consolidation.Domain;

namespace BancoCarrefour.Consolidation.Application.ApplyFinancialEntry;

public sealed partial class ApplyFinancialEntryUseCase(
    IDailyBalanceProjectionStore projectionStore,
    TimeProvider timeProvider) : IApplyFinancialEntryUseCase
{
    private const string CurrentEventType = "FinancialEntryRegistered";
    private const string LegacyEventType = "EntryCreated";
    private const int SupportedEventVersion = 1;
    private const string AmountPattern = "^(?!0+(\\.0{1,2})?$)[0-9]{1,16}(\\.[0-9]{1,2})?$";
    private static readonly TimeZoneInfo SaoPauloTimeZone = FindSaoPauloTimeZone();

    public async Task<ProjectionResult> ApplyAsync(
        ApplyFinancialEntryCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var contribution = ToContribution(command);
        var processedEntry = new ProcessedFinancialEntry(
            command.EventId,
            command.EventType,
            command.EventVersion,
            contribution.MerchantId.Value,
            contribution.BusinessDate.Value,
            timeProvider.GetUtcNow());

        return await projectionStore.ApplyAsync(processedEntry, contribution, cancellationToken);
    }

    private static DailyBalanceContribution ToContribution(ApplyFinancialEntryCommand command)
    {
        if (command.EventId == Guid.Empty)
        {
            throw new ProjectionValidationException("eventId é obrigatório.");
        }

        if (command.EventType is not (CurrentEventType or LegacyEventType))
        {
            throw new ProjectionValidationException("eventType deve ser FinancialEntryRegistered ou EntryCreated legado.");
        }

        if (command.EventVersion != SupportedEventVersion)
        {
            throw new ProjectionValidationException("eventVersion deve ser 1.");
        }

        if (command.OccurredAt == default)
        {
            throw new ProjectionValidationException("occurredAt é obrigatório.");
        }

        if (command.RegisteredAt == default)
        {
            throw new ProjectionValidationException("registeredAt é obrigatório para FinancialEntryRegistered ou createdAt para EntryCreated legado.");
        }

        if (string.IsNullOrWhiteSpace(command.CorrelationId) || command.CorrelationId.Length > 128)
        {
            throw new ProjectionValidationException("correlationId é obrigatório e deve ter no máximo 128 caracteres.");
        }

        if (command.EntryId == Guid.Empty)
        {
            throw new ProjectionValidationException("entryId é obrigatório.");
        }

        if (string.IsNullOrWhiteSpace(command.Amount)
            || !AmountRegex().IsMatch(command.Amount)
            || !decimal.TryParse(command.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            throw new ProjectionValidationException("amount deve ser monetário positivo conforme contrato.");
        }

        if (command.Currency != Money.SupportedCurrency)
        {
            throw new ProjectionValidationException("currency deve ser BRL.");
        }

        if (!DateOnly.TryParseExact(command.BusinessDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var businessDate))
        {
            throw new ProjectionValidationException("businessDate deve ser uma data válida no formato yyyy-MM-dd.");
        }

        var expectedBusinessDate = BusinessDateFromOccurredAt(command.OccurredAt);
        if (businessDate != expectedBusinessDate)
        {
            throw new ProjectionValidationException("businessDate deve ser coerente com occurredAt em America/Sao_Paulo.");
        }

        if (command.Description is { Length: > 256 })
        {
            throw new ProjectionValidationException("description deve ter no máximo 256 caracteres.");
        }

        try
        {
            return new DailyBalanceContribution(
                MerchantId.Create(command.MerchantId),
                BusinessDate.Create(businessDate),
                FinancialEntryTypeParser.Parse(command.Type),
                Money.CreatePositive(amount),
                command.OccurredAt.ToUniversalTime());
        }
        catch (DomainValidationException exception)
        {
            throw new ProjectionValidationException(exception.Message);
        }
    }

    private static DateOnly BusinessDateFromOccurredAt(DateTimeOffset occurredAt)
    {
        var localDateTime = TimeZoneInfo.ConvertTime(occurredAt.ToUniversalTime(), SaoPauloTimeZone);

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

    [GeneratedRegex(AmountPattern, RegexOptions.CultureInvariant)]
    private static partial Regex AmountRegex();
}
