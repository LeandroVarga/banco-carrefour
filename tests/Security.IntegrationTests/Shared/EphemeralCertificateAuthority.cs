using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace BancoCarrefour.Security.IntegrationTests.Shared;

/// <summary>
/// CA e certificados-folha efêmeros, gerados em memória por execução de teste
/// (nunca persistidos em arquivo, nunca versionados). Usado para dar TLS real
/// ao Keycloak e ao edge-proxy dentro dos testes, sem depender dos
/// certificados reais de <c>.local/security/certs/</c> (esses são gerados
/// pelo bootstrap local e não pertencem ao projeto de testes).
/// </summary>
internal sealed class EphemeralCertificateAuthority : IDisposable
{
    private readonly RSA caKey;
    private readonly DateTimeOffset notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
    private readonly DateTimeOffset notAfter = DateTimeOffset.UtcNow.AddHours(6);

    public X509Certificate2 CaCertificate { get; }

    public EphemeralCertificateAuthority()
    {
        caKey = RSA.Create(2048);

        var request = new CertificateRequest(
            "CN=BancoCarrefour Security.IntegrationTests CA",
            caKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));

        // notBefore/notAfter fixados uma única vez (não recalculados por
        // chamada) — o certificado-folha reusa os MESMOS limites, evitando
        // que "agora" avance entre a criação da CA e a emissão da folha e
        // torne notAfter da folha posterior ao da CA (violação inválida).
        CaCertificate = request.CreateSelfSigned(notBefore, notAfter);
    }

    public LeafCertificate IssueLeafCertificate(params string[] subjectAlternativeNames)
    {
        var leafKey = RSA.Create(2048);

        var request = new CertificateRequest(
            $"CN={subjectAlternativeNames[0]}",
            leafKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var sanBuilder = new SubjectAlternativeNameBuilder();
        foreach (var name in subjectAlternativeNames)
        {
            if (IPAddress.TryParse(name, out var address))
            {
                sanBuilder.AddIpAddress(address);
            }
            else
            {
                sanBuilder.AddDnsName(name);
            }
        }

        request.CertificateExtensions.Add(sanBuilder.Build());
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, critical: false));

        var serial = new byte[16];
        RandomNumberGenerator.Fill(serial);

        using var leafCertificate = request.Create(
            CaCertificate,
            notBefore,
            notAfter,
            serial);

        var certificatePem = leafCertificate.ExportCertificatePem();
        var privateKeyPem = leafKey.ExportRSAPrivateKeyPem();

        return new LeafCertificate(certificatePem, privateKeyPem);
    }

    /// <summary>
    /// Callback de validação de servidor TLS que confia exclusivamente nesta
    /// CA efêmera (nunca "aceitar qualquer certificado") — equivalente de
    /// teste ao <c>update-ca-certificates</c> usado em produção
    /// (docker/api-entrypoint.sh), sem alterar RequireHttpsMetadata nem a
    /// validação de chave de assinatura do token.
    /// </summary>
    public bool ValidateServerCertificate(
        System.Net.Http.HttpRequestMessage _,
        X509Certificate2? certificate,
        X509Chain? __,
        System.Net.Security.SslPolicyErrors ___)
    {
        if (certificate is null)
        {
            return false;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
        chain.ChainPolicy.ExtraStore.Add(CaCertificate);
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(CaCertificate);

        return chain.Build(certificate);
    }

    public void Dispose()
    {
        caKey.Dispose();
        CaCertificate.Dispose();
    }
}

internal sealed record LeafCertificate(string CertificatePem, string PrivateKeyPem);
