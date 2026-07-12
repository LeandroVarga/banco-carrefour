param(
    [string]$MerchantId = "merchant-001",
    [string]$SigningKey = "ledger-local-development-signing-key-32-bytes",
    [string]$Issuer = "banco-carrefour-local",
    [string]$Audience = "banco-carrefour-api"
)

function ConvertTo-Base64Url {
    param([byte[]]$Bytes)

    return [Convert]::ToBase64String($Bytes).TrimEnd("=").Replace("+", "-").Replace("/", "_")
}

$header = @{
    alg = "HS256"
    typ = "JWT"
} | ConvertTo-Json -Compress

$now = [DateTimeOffset]::UtcNow
$payload = @{
    sub = "local-user"
    iss = $Issuer
    aud = $Audience
    merchant_id = $MerchantId
    iat = $now.ToUnixTimeSeconds()
    exp = $now.AddHours(8).ToUnixTimeSeconds()
} | ConvertTo-Json -Compress

$encoding = [Text.Encoding]::UTF8
$unsignedToken = "$(ConvertTo-Base64Url $encoding.GetBytes($header)).$(ConvertTo-Base64Url $encoding.GetBytes($payload))"

$hmac = [Security.Cryptography.HMACSHA256]::new($encoding.GetBytes($SigningKey))
$signature = ConvertTo-Base64Url $hmac.ComputeHash($encoding.GetBytes($unsignedToken))

Write-Output "$unsignedToken.$signature"
