using BancoCarrefour.Ledger.Domain;

namespace BancoCarrefour.Ledger.Application.RegisterFinancialEntry;

public sealed record FinancialEntryRegistration(
    FinancialEntry Entry,
    string IdempotencyKey,
    string PayloadFingerprint,
    FinancialEntryRegisteredIntegrationEvent IntegrationEvent);
