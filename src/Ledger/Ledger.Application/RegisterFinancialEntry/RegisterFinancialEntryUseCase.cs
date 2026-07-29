using System.Globalization;
using System.Text.RegularExpressions;
using BancoCarrefour.Ledger.Domain;

namespace BancoCarrefour.Ledger.Application.RegisterFinancialEntry;

public sealed partial class RegisterFinancialEntryUseCase(
    IFinancialEntryRegistrationStore store,
    TimeProvider timeProvider) : IRegisterFinancialEntryUseCase
{
    private const string AmountPattern = "^(?!0+(\\.0{1,2})?$)[0-9]{1,16}(\\.[0-9]{1,2})?$";

    public async Task<RegisterFinancialEntryResult> RegisterAsync(
        RegisterFinancialEntryCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var errors = Validate(command);
        if (errors.Count > 0)
        {
            throw new RegisterFinancialEntryValidationException(errors);
        }

        var merchantId = MerchantId.Create(command.MerchantId);
        var entryType = EntryTypeParser.Parse(command.Type);
        var amount = decimal.Parse(command.Amount, NumberStyles.Number, CultureInfo.InvariantCulture);
        var money = Money.Create(amount, command.Currency);
        var description = EntryDescription.Create(command.Description);
        var registeredAt = timeProvider.GetUtcNow();
        var entry = FinancialEntry.Register(
            EntryId.New(),
            merchantId,
            entryType,
            money,
            command.OccurredAt,
            registeredAt,
            description);

        var response = ToRegisteredEntry(entry, command.IdempotencyKey);
        var integrationEvent = new FinancialEntryRegisteredIntegrationEvent(
            Guid.NewGuid(),
            FinancialEntryRegisteredIntegrationEvent.TypeName,
            FinancialEntryRegisteredIntegrationEvent.Version,
            entry.OccurredAt,
            entry.RegisteredAt,
            command.CorrelationId,
            entry.EntryId.Value,
            entry.MerchantId.Value,
            response.BusinessDate,
            response.Type,
            response.Amount,
            response.Currency,
            entry.Description.Value);

        var registration = new FinancialEntryRegistration(
            entry,
            command.IdempotencyKey,
            EntryFingerprint.Calculate(entry),
            integrationEvent);

        return await store.RegisterAsync(registration, cancellationToken);
    }

    public static RegisteredFinancialEntry ToRegisteredEntry(
        FinancialEntry entry,
        string idempotencyKey)
    {
        return new RegisteredFinancialEntry(
            entry.EntryId.Value,
            entry.MerchantId.Value,
            entry.BusinessDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            entry.Type.ToContractValue(),
            entry.Money.Format(),
            entry.Money.Currency,
            entry.OccurredAt,
            entry.RegisteredAt,
            idempotencyKey);
    }

    private static List<string> Validate(RegisterFinancialEntryCommand command)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            errors.Add("Idempotency-Key é obrigatório.");
        }
        else if (command.IdempotencyKey.Length is < 8 or > 128)
        {
            errors.Add("Idempotency-Key deve ter entre 8 e 128 caracteres.");
        }

        if (command.Type is not ("CREDIT" or "DEBIT"))
        {
            errors.Add("type deve ser CREDIT ou DEBIT.");
        }

        if (string.IsNullOrWhiteSpace(command.Amount) || !AmountRegex().IsMatch(command.Amount))
        {
            errors.Add("amount deve ser monetário positivo conforme contrato.");
        }
        else if (!decimal.TryParse(command.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            errors.Add("amount deve ser decimal válido.");
        }

        if (command.Currency != Money.SupportedCurrency)
        {
            errors.Add("currency deve ser BRL.");
        }

        if (command.OccurredAt == default)
        {
            errors.Add("occurredAt é obrigatório.");
        }

        if (command.Description is { Length: > EntryDescription.MaxLength })
        {
            errors.Add("description deve ter no máximo 256 caracteres.");
        }

        return errors;
    }

    [GeneratedRegex(AmountPattern, RegexOptions.CultureInvariant)]
    private static partial Regex AmountRegex();
}
