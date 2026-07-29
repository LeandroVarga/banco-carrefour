using Xunit;

namespace BancoCarrefour.Architecture.Tests;

/// <summary>
/// Governança estrutural da infraestrutura Terraform de identidade de
/// release e publicação de imagens: módulo
/// ECR, ambiente aws-reference (repositórios, OIDC, IAM de publicação).
/// Não existe parser HCL disponível neste repositório - as checagens abaixo
/// são recortes textuais deliberadamente escopados aos blocos de recurso
/// exatos (nunca uma varredura genérica do texto inteiro), para não
/// confundir comentário com declaração real nem virar um motor de política
/// genérico.
/// </summary>
public sealed class ReleaseInfrastructureGovernanceArchitectureTests
{
    private static readonly string[] ExpectedComponents =
    [
        "ledger-api",
        "ledger-outbox-publisher",
        "consolidation-api",
        "consolidation-worker",
    ];

    private static string AwsReferenceDirectory =>
        Path.Combine(RepositoryRoot, "infra", "terraform", "environments", "aws-reference");

    private static string EcrModuleDirectory =>
        Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "ecr");

    private static string AwsReferenceMainTf => File.ReadAllText(Path.Combine(AwsReferenceDirectory, "main.tf"));

    private static string EcrModuleMainTf => File.ReadAllText(Path.Combine(EcrModuleDirectory, "main.tf"));

    private static string EcrModuleVariablesTf => File.ReadAllText(Path.Combine(EcrModuleDirectory, "variables.tf"));

    [Fact]
    public void Ambiente_aws_reference_deve_existir_e_nunca_ter_sido_aplicado_localmente()
    {
        Assert.True(Directory.Exists(AwsReferenceDirectory), "infra/terraform/environments/aws-reference deve existir.");
        Assert.False(
            Directory.Exists(Path.Combine(AwsReferenceDirectory, "terraform.tfstate")),
            "aws-reference nao deve ter um terraform.tfstate - este ambiente nunca foi aplicado.");
    }

    [Fact]
    public void Ambiente_aws_reference_deve_declarar_os_4_componentes_implantaveis()
    {
        var content = AwsReferenceMainTf;
        foreach (var component in ExpectedComponents)
        {
            Assert.Contains($"\"{component}\"", content);
        }
    }

    [Fact]
    public void Modulo_ECR_deve_ter_tag_imutavel_como_padrao()
    {
        Assert.Contains("default     = \"IMMUTABLE\"", EcrModuleVariablesTf, StringComparison.Ordinal);
    }

    [Fact]
    public void Ambiente_aws_reference_nao_deve_sobrescrever_a_tag_para_mutavel()
    {
        Assert.DoesNotContain("image_tag_mutability = \"MUTABLE\"", AwsReferenceMainTf, StringComparison.Ordinal);
        Assert.Contains("image_tag_mutability = \"IMMUTABLE\"", AwsReferenceMainTf, StringComparison.Ordinal);
    }

    [Fact]
    public void Modulo_ECR_deve_suportar_lifecycle_policy()
    {
        Assert.Contains("resource \"aws_ecr_lifecycle_policy\" \"this\"", EcrModuleMainTf, StringComparison.Ordinal);
    }

    [Fact]
    public void Ambiente_aws_reference_deve_fornecer_lifecycle_policy_para_os_repositorios()
    {
        Assert.Contains("ecr_lifecycle_policy", AwsReferenceMainTf, StringComparison.Ordinal);
        Assert.Contains("lifecycle_policy     = local.ecr_lifecycle_policy", AwsReferenceMainTf, StringComparison.Ordinal);
    }

    [Fact]
    public void Lifecycle_policy_nao_deve_apagar_agressivamente_todas_as_imagens_com_tag()
    {
        // A regra sobre imagens COM tag so deve expirar acima de um numero
        // minimo de imagens mantidas (rollback/forense) - nunca uma regra
        // "tagStatus: tagged" sem "countType: imageCountMoreThan" (o que
        // apagaria tudo de uma vez).
        var content = AwsReferenceMainTf;
        Assert.Contains("tagStatus     = \"tagged\"", content, StringComparison.Ordinal);
        Assert.Contains("countType     = \"imageCountMoreThan\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Trust_policy_OIDC_deve_restringir_audience_repositorio_e_ref_sem_curinga()
    {
        var content = AwsReferenceMainTf;
        Assert.Contains("token.actions.githubusercontent.com:aud", content, StringComparison.Ordinal);
        Assert.Contains("token.actions.githubusercontent.com:sub", content, StringComparison.Ordinal);
        Assert.Contains("var.github_repository", content, StringComparison.Ordinal);
        Assert.Contains("var.github_ref", content, StringComparison.Ordinal);
        Assert.DoesNotContain("repo:*", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Policy_do_publicador_ECR_deve_ser_escopada_aos_ARNs_dos_5_repositorios()
    {
        // 4 de negocio + 1 operacional (migration-runner) - local.all_published_components
        // e o concat dos dois, ver local.operational_components no mesmo arquivo.
        var block = ExtractPublisherPermissionsBlock();
        Assert.Contains("resources = [for c in local.all_published_components : module.ecr[c].repository_arn]", block, StringComparison.Ordinal);
    }

    [Fact]
    public void Policy_do_publicador_ECR_nao_deve_conceder_ECS_IAM_write_RDS_Secrets_SSM_ou_admin_amplo()
    {
        var block = ExtractPublisherPermissionsBlock();

        string[] forbiddenSubstrings =
        [
            "ecs:",
            "iam:Create",
            "iam:Put",
            "iam:Attach",
            "iam:Delete",
            "iam:Update",
            "rds:",
            "secretsmanager:",
            "ssm:",
            "AdministratorAccess",
        ];

        foreach (var forbidden in forbiddenSubstrings)
        {
            Assert.DoesNotContain(forbidden, block, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Somente_GetAuthorizationToken_deve_usar_Resource_curinga_na_policy_do_publicador()
    {
        var block = ExtractPublisherPermissionsBlock();

        // GetAuthorizationToken so e suportada com Resource "*" (a AWS nao
        // permite escopo por repositorio para essa acao especifica) - ver
        // docs/security/dependencias-e-supply-chain.md e AWS Documentation
        // MCP. Nenhuma outra declaracao nesta policy deve usar Resource "*".
        var wildcardOccurrences = System.Text.RegularExpressions.Regex.Matches(block, "resources\\s*=\\s*\\[\"\\*\"\\]");
        Assert.True(wildcardOccurrences.Count <= 1, "no maximo uma declaracao (GetAuthorizationToken) deve usar Resource curinga na policy do publicador ECR.");
        Assert.Contains("GetAuthorizationToken", block, StringComparison.Ordinal);
    }

    [Fact]
    public void Ambiente_aws_reference_nao_deve_criar_uma_CMK_dedicada_para_o_ECR()
    {
        // a CMK dedicada ao ECR foi removida -
        // nao ha driver concreto de regulacao/governanca que justifique o
        // custo/complexidade administrativa frente a chave GERENCIADA PELA
        // AWS ("aws/ecr"). O modulo "ecr" so aceita um ARN de CMK EXTERNA
        // (var.ecr_kms_key_arn, default null) - este ambiente nunca cria
        // nem administra uma chave KMS para as imagens de container.
        var content = AwsReferenceMainTf;
        Assert.DoesNotContain("module \"ecr_kms\"", content, StringComparison.Ordinal);
        Assert.DoesNotContain("resource \"aws_kms_key\"", content, StringComparison.Ordinal);
        Assert.DoesNotContain("resource \"aws_kms_alias\"", content, StringComparison.Ordinal);
        Assert.Contains("kms_key_arn          = var.ecr_kms_key_arn", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Modulo_ECR_deve_suportar_3_estados_de_criptografia_via_encryption_type_e_kms_key_arn()
    {
        // AES256 (S3-managed), KMS com chave gerenciada pela AWS (kms_key_arn
        // nulo) e KMS com CMK do cliente (kms_key_arn explicito) sao 3
        // estados distintos - o bloco encryption_configuration tem que ser
        // SEMPRE emitido (nunca omitido dinamicamente), senao "kms_key_arn
        // nulo" seria confundido com AES256 por omissao do bloco inteiro
        // (defeito real encontrado e corrigido na auditoria do Bloco 3).
        var variablesContent = EcrModuleVariablesTf;
        Assert.Contains("variable \"encryption_type\"", variablesContent, StringComparison.Ordinal);
        Assert.Contains("contains([\"AES256\", \"KMS\"], var.encryption_type)", variablesContent, StringComparison.Ordinal);
        Assert.Contains("variable \"kms_key_arn\"", variablesContent, StringComparison.Ordinal);

        var mainContent = EcrModuleMainTf;
        Assert.DoesNotContain("dynamic \"encryption_configuration\"", mainContent, StringComparison.Ordinal);
        Assert.Contains("encryption_configuration {", mainContent, StringComparison.Ordinal);
        Assert.Contains("kms_key         = var.encryption_type == \"KMS\" ? var.kms_key_arn : null", mainContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Modulo_KMS_generico_nao_deve_ter_sido_estendido_com_variavel_de_policy_nao_utilizada()
    {
        // A extensao anterior do modulo KMS (variavel "policy" opcional,
        // adicionada para a CMK dedicada do ECR) foi revertida junto com a
        // remocao dessa CMK - nenhum consumidor deste modulo (nem o novo
        // ambiente aws-reference, nem o localstack-hobby ) usa
        // "policy" hoje, entao o modulo generico nao deve declara-la.
        var kmsModuleDirectory = Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "kms");
        var kmsVariablesContent = File.ReadAllText(Path.Combine(kmsModuleDirectory, "variables.tf"));
        var kmsMainContent = File.ReadAllText(Path.Combine(kmsModuleDirectory, "main.tf"));

        Assert.DoesNotContain("variable \"policy\"", kmsVariablesContent, StringComparison.Ordinal);
        Assert.DoesNotContain("var.policy", kmsMainContent, StringComparison.Ordinal);

        var localstackHobbyMainTf = File.ReadAllText(Path.Combine(
            RepositoryRoot, "infra", "terraform", "environments", "localstack-hobby", "main.tf"));
        Assert.DoesNotContain("policy      =", localstackHobbyMainTf, StringComparison.Ordinal);
    }

    [Fact]
    public void Policy_do_publicador_ECR_nao_deve_conceder_nenhuma_permissao_KMS()
    {
        // o proprio ECR cifra/decifra em nome
        // do principal chamador usando grants que ele mesmo cria na chave
        // configurada (documentacao oficial "Encryption at rest" do ECR,
        // AWS Documentation MCP) - a role de publicacao nunca precisa de
        // nenhuma permissao KMS direta.
        var block = ExtractPublisherPermissionsBlock();
        Assert.DoesNotContain("kms:", block, StringComparison.Ordinal);
    }

    [Fact]
    public void Provedor_OIDC_deve_ser_condicional_para_suportar_ownership_externo()
    {
        var content = AwsReferenceMainTf;
        Assert.Contains("resource \"aws_iam_openid_connect_provider\" \"github\"", content, StringComparison.Ordinal);
        Assert.Contains("count = var.create_oidc_provider ? 1 : 0", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Local_oidc_provider_arn_deve_cobrir_ambos_os_modos_de_ownership()
    {
        // create_oidc_provider=true -> usa o ARN do recurso criado por este
        // ambiente; =false -> apenas referencia o ARN convencional de um
        // provedor ja existente (gerenciado por outro ambiente/organizacao)
        // - nunca uma "data source" que exigiria acesso real a AWS durante
        // validacao offline.
        var content = AwsReferenceMainTf;
        Assert.Contains(
            "oidc_provider_arn = var.create_oidc_provider ? aws_iam_openid_connect_provider.github[0].arn : \"arn:aws:iam::${var.aws_account_id}:oidc-provider/token.actions.githubusercontent.com\"",
            content,
            StringComparison.Ordinal);
        Assert.DoesNotContain("data \"aws_iam_openid_connect_provider\"", content, StringComparison.Ordinal);
    }

    private static string ExtractPublisherPermissionsBlock()
    {
        var content = AwsReferenceMainTf;
        const string startMarker = "data \"aws_iam_policy_document\" \"ecr_publisher_permissions\"";
        var start = content.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, "bloco 'data aws_iam_policy_document ecr_publisher_permissions' nao encontrado em aws-reference/main.tf.");

        var end = content.IndexOf("\nresource \"aws_iam_role_policy\"", start, StringComparison.Ordinal);
        Assert.True(end > start, "fim do bloco de policy do publicador nao encontrado.");

        return content[start..end];
    }

    private static string RepositoryRoot { get; } = LocateRepositoryRoot();

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BancoCarrefour.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Não foi possível localizar a raiz do repositório.");
    }
}
