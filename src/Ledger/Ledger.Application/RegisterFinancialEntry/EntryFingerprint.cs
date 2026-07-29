using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BancoCarrefour.Ledger.Domain;

namespace BancoCarrefour.Ledger.Application.RegisterFinancialEntry;

public static class EntryFingerprint
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static string Calculate(FinancialEntry entry)
    {
        var canonical = new
        {
            merchantId = entry.MerchantId.Value,
            type = entry.Type.ToContractValue(),
            amount = entry.Money.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            currency = entry.Money.Currency,
            occurredAt = entry.OccurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            description = entry.Description.Value
        };

        var json = JsonSerializer.Serialize(canonical, SerializerOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));

        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
