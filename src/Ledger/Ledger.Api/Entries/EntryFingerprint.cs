using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BancoCarrefour.Ledger.Api.Entries;

internal static class EntryFingerprint
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static string Calculate(
        string merchantId,
        string type,
        decimal amount,
        string currency,
        DateTimeOffset occurredAt,
        string? description)
    {
        var canonical = new
        {
            merchantId,
            type = type.ToUpperInvariant(),
            amount = FormatAmount(amount),
            currency = currency.ToUpperInvariant(),
            occurredAt = occurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            description = NormalizeDescription(description)
        };

        var json = JsonSerializer.Serialize(canonical, SerializerOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));

        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string FormatAmount(decimal amount)
    {
        return amount.ToString("0.00", CultureInfo.InvariantCulture);
    }

    public static string? NormalizeDescription(string? description)
    {
        if (description is null)
        {
            return null;
        }

        var normalized = description.Trim();

        return normalized.Length == 0 ? null : normalized;
    }
}
