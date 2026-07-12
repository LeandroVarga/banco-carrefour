using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

const string DefaultSigningKey = "ledger-local-development-signing-key-32-bytes";
const string DefaultSubject = "local-user";
const int DefaultExpiresInMinutes = 480;
const int MerchantIdMaxLength = 64;
const int MinExpiresInMinutes = 1;
const int MaxExpiresInMinutes = 1440;

try
{
    var options = ParseArguments(args);
    var token = CreateJwtToken(options.MerchantId, options.ExpiresInMinutes);

    Console.WriteLine(token);
    return 0;
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

static LocalJwtOptions ParseArguments(string[] args)
{
    string? merchantId = null;
    var expiresInMinutes = DefaultExpiresInMinutes;

    for (var index = 0; index < args.Length; index++)
    {
        var argument = args[index];

        switch (argument)
        {
            case "--merchant-id":
                merchantId = ReadOptionValue(args, ref index, argument);
                break;

            case "--expires-in-minutes":
                var rawExpiresInMinutes = ReadOptionValue(args, ref index, argument);
                if (!int.TryParse(rawExpiresInMinutes, NumberStyles.None, CultureInfo.InvariantCulture, out expiresInMinutes))
                {
                    throw new ArgumentException("--expires-in-minutes deve ser um numero inteiro.");
                }

                break;

            case "-h":
            case "--help":
                throw new ArgumentException(
                    "Uso: local-jwt --merchant-id merchant-001 [--expires-in-minutes 120]");

            default:
                throw new ArgumentException($"Argumento desconhecido: {argument}");
        }
    }

    if (string.IsNullOrWhiteSpace(merchantId))
    {
        throw new ArgumentException("--merchant-id e obrigatorio e nao pode ser vazio.");
    }

    merchantId = merchantId.Trim();

    if (merchantId.Length > MerchantIdMaxLength)
    {
        throw new ArgumentException($"--merchant-id deve ter no maximo {MerchantIdMaxLength} caracteres.");
    }

    if (expiresInMinutes is < MinExpiresInMinutes or > MaxExpiresInMinutes)
    {
        throw new ArgumentException(
            $"--expires-in-minutes deve estar entre {MinExpiresInMinutes} e {MaxExpiresInMinutes}.");
    }

    return new LocalJwtOptions(merchantId, expiresInMinutes);
}

static string ReadOptionValue(string[] args, ref int index, string optionName)
{
    if (index + 1 >= args.Length)
    {
        throw new ArgumentException($"{optionName} exige um valor.");
    }

    var value = args[++index];

    if (value.StartsWith("--", StringComparison.Ordinal))
    {
        throw new ArgumentException($"{optionName} exige um valor.");
    }

    return value;
}

static string CreateJwtToken(string merchantId, int expiresInMinutes)
{
    var now = DateTimeOffset.UtcNow;
    var header = new Dictionary<string, object>
    {
        ["alg"] = "HS256",
        ["typ"] = "JWT"
    };
    var payload = new Dictionary<string, object>
    {
        ["sub"] = DefaultSubject,
        ["merchant_id"] = merchantId,
        ["iat"] = now.ToUnixTimeSeconds(),
        ["exp"] = now.AddMinutes(expiresInMinutes).ToUnixTimeSeconds()
    };

    var unsignedToken = string.Create(
        CultureInfo.InvariantCulture,
        $"{Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header))}.{Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload))}");

    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(DefaultSigningKey));
    var signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(unsignedToken));

    return string.Create(CultureInfo.InvariantCulture, $"{unsignedToken}.{Base64UrlEncode(signature)}");
}

static string Base64UrlEncode(byte[] value)
{
    return Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}

internal sealed record LocalJwtOptions(string MerchantId, int ExpiresInMinutes);
