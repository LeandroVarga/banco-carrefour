# Ambiente de referencia AWS para identidade de release e publicacao de
# imagens (ADR-0013). NUNCA aplicado contra uma conta AWS real nesta sessao -
# apenas "terraform validate"/"plan" offline. Nao substitui
# infra/terraform/environments/localstack-hobby (ECR nao suportado no
# LocalStack Community usado por este projeto, confirmado por capability
# spike - ver ADR-0013).
#
# Layout de 4 repositorios ECR independentes (um por unidade implantavel),
# em vez de 1 repositorio compartilhado com tags por componente: preserva o
# escopo de IAM por repositorio (a role de publicacao usa ARNs especificos,
# nao um curinga sobre todos os repositorios), permite lifecycle policy e
# blast radius independentes por componente, e mantem rastreabilidade clara
# entre unidade implantavel e registry - ver ADR-0013 para o trade-off
# completo frente ao repositorio unico.
locals {
  # 4 workloads de negocio (ver ADR-0006) + 1 artefato operacional
  # ("migration-runner",
  # Closure). O migration-runner NUNCA e um 5o workload no modelo de
  # dominio arquitetural (nunca aparece como ECS Service, nunca nos
  # diagramas C4 de container/deployment como unidade de negocio) - e uma
  # imagem de container publicada e versionada exatamente como as 4
  # imagens de negocio (mesmo commit de origem, mesmo digest, mesmo
  # repositorio ECR generico), porque a task ECS one-off que a executa
  # (modulo ecs-migration-task) tambem precisa de uma imagem
  # qualificada por digest, nunca "latest".
  components = [
    "ledger-api",
    "ledger-outbox-publisher",
    "consolidation-api",
    "consolidation-worker",
  ]

  operational_components = [
    "migration-runner",
  ]

  all_published_components = concat(local.components, local.operational_components)

  common_tags = merge(var.tags, {
    project     = "banco-carrefour"
    environment = "aws-reference"
    managed_by  = "terraform"
  })

  publisher_role_name = "banco-carrefour-ecr-publisher"

  # Provedor OIDC do GitHub Actions: criado por este ambiente quando
  # create_oidc_provider=true (unica conta AWS deste caso, sem organizacao
  # multi-conta documentada) - se um provedor organizacional ja existir em
  # outro lugar, defina create_oidc_provider=false e este ambiente apenas
  # referencia o ARN convencional (uma conta AWS so pode ter um provedor
  # OIDC por URL).
  oidc_provider_arn = var.create_oidc_provider ? aws_iam_openid_connect_provider.github[0].arn : "arn:aws:iam::${var.aws_account_id}:oidc-provider/token.actions.githubusercontent.com"

  # Politica de lifecycle identica para os 4 repositorios: expira imagens
  # SEM tag (nunca referenciadas por nenhum deploy - tipicamente builds que
  # falharam no scan antes do push) apos N dias, e mantem as ultimas M
  # imagens com a tag canonica "sha-" (candidatas a rollback e forense de
  # vulnerabilidade) - nunca apaga agressivamente todas as imagens antigas
  # com tag (ver ADR-0013, secao de lifecycle). A regra so seleciona
  # imagens com prefixo de tag "sha-" ou sem tag - uma futura tag de
  # release/promocao com outro prefixo (ex.: "v1.2.3", "prod") nunca e
  # selecionada por nenhuma regra aqui, logo nunca e apagada
  # automaticamente por este lifecycle.
  #
  # LIMITACAO CONHECIDA (documentada, nao resolvida neste bloco): o ECR
  # nao tem conhecimento de quais imagens estao ativas em uma task
  # definition do ECS - a regra "manter as ultimas M com tag sha-" e
  # contada por PUSH, nao por uso real em producao. Se uma imagem
  # permanecer implantada por mais tempo do que leva para M imagens novas
  # serem publicadas, ela pode ser expirada mesmo estando ativa. Este
  # bloco so prepara a fundacao de release/ECR - a
  # validacao final de retencao (imagem ativa + candidatos a rollback
  # sempre "puxaveis") fica para o bloco de deploy no ECS, quando o
  # modelo real de retencao/rollback for definido (ver ADR-0013).
  ecr_lifecycle_policy = jsonencode({
    rules = [
      {
        rulePriority = 1
        description  = "Expirar imagens sem tag (nunca publicadas com sucesso ou nunca referenciadas) apos ${var.ecr_lifecycle_untagged_expire_days} dias"
        selection = {
          tagStatus   = "untagged"
          countType   = "sinceImagePushed"
          countUnit   = "days"
          countNumber = var.ecr_lifecycle_untagged_expire_days
        }
        action = { type = "expire" }
      },
      {
        rulePriority = 2
        description  = "Manter as ultimas ${var.ecr_lifecycle_keep_last_tagged} imagens com a tag canonica sha- (rollback e forense de vulnerabilidade)"
        selection = {
          tagStatus     = "tagged"
          tagPrefixList = ["sha-"]
          countType     = "imageCountMoreThan"
          countNumber   = var.ecr_lifecycle_keep_last_tagged
        }
        action = { type = "expire" }
      }
    ]
  })

  # Cross-account pull (ADR-0011): as contas de workload
  # (Development/Staging/Production) leem estes 4 repositorios
  # diretamente - nunca escrevem, nunca gerenciam. Sem principals
  # configurados (workload_account_ids vazio), nenhuma policy e criada e
  # o repositorio permanece acessivel somente por esta propria conta.
  cross_account_pull_policy = length(var.workload_account_ids) > 0 ? jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Sid    = "AllowWorkloadAccountsPull"
        Effect = "Allow"
        Principal = {
          AWS = [for id in var.workload_account_ids : "arn:aws:iam::${id}:root"]
        }
        Action = [
          "ecr:GetDownloadUrlForLayer",
          "ecr:BatchGetImage",
          "ecr:BatchCheckLayerAvailability",
        ]
      }
    ]
  }) : null
}

# --- Criptografia dos repositorios ECR ---
# Decisao: usar
# encryption_type=KMS com a chave GERENCIADA PELA AWS ("aws/ecr", criada
# automaticamente pela propria AWS), NAO uma chave gerenciada pelo cliente
# (CMK) dedicada. Motivo: nao existe nenhum driver concreto de
# regulacao/governanca documentado neste repositorio que exija uma CMK
# especificamente para as imagens de container (diferente dos secrets,
# ADR-0009, cuja CMK dedicada tem justificativa propria de
# fronteira de auditoria de segredo); uma CMK dedicada exigiria
# administrar key policy, grants e janela de delecao, alem de custo
# mensal por chave - sem ganho de seguranca adicional relevante frente a
# "KMS com chave gerenciada pela AWS" para este caso de referencia, que ja
# e auditavel via CloudTrail e mais controlado que o AES256/S3-managed
# padrao. O modulo aceita um ARN de CMK externo (var.ecr_kms_key_arn) caso
# um driver concreto surja no futuro - nao cria nem administra uma CMK
# neste ambiente. Preco exato atual da AWS Pricing MCP nao pode ser
# confirmado nesta sessao (token SSO do perfil de pricing expirado durante
# a auditoria) - a decisao se apoia na diferenca qualitativa de
# complexidade/custo administrativo (chave dedicada = key policy propria +
# grants + janela de delecao + tarifa mensal por chave; chave gerenciada
# pela AWS = zero administracao, zero tarifa de chave dedicada), nao em um
# numero exato.
# --- 5 repositorios ECR independentes (4 workloads de negocio + 1 artefato operacional de migracao) ---
module "ecr" {
  for_each = toset(local.all_published_components)
  source   = "../../modules/ecr"

  name                 = "banco-carrefour/${each.value}"
  image_tag_mutability = "IMMUTABLE"
  scan_on_push         = true
  encryption_type      = "KMS"
  kms_key_arn          = var.ecr_kms_key_arn
  lifecycle_policy     = local.ecr_lifecycle_policy
  repository_policy    = local.cross_account_pull_policy

  tags = merge(local.common_tags, { component = each.value })
}

# --- Identidade OIDC do GitHub Actions ---
resource "aws_iam_openid_connect_provider" "github" {
  count = var.create_oidc_provider ? 1 : 0

  url            = "https://token.actions.githubusercontent.com"
  client_id_list = ["sts.amazonaws.com"]
  # thumbprint_list omitido de proposito: para o provedor do GitHub, a AWS
  # usa sua propria biblioteca de CAs confiaveis para validacao, nao o
  # thumbprint configurado (confirmado via schema oficial do provider
  # aws_iam_openid_connect_provider - Terraform MCP, hashicorp/aws ~> 5.0).

  tags = local.common_tags
}

# --- Role de publicacao (menor privilegio, sem credenciais estaticas) ---
data "aws_iam_policy_document" "ecr_publisher_assume_role" {
  statement {
    sid     = "AllowGitHubActionsOIDC"
    effect  = "Allow"
    actions = ["sts:AssumeRoleWithWebIdentity"]

    principals {
      type        = "Federated"
      identifiers = [local.oidc_provider_arn]
    }

    # Restringe a exatamente este repositorio E este ref - nunca um
    # curinga que libere qualquer repositorio ou qualquer branch (ver
    # ADR-0013). Tags de release, se adotadas no futuro, devem usar uma
    # role dedicada em vez de ampliar esta condicao.
    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:aud"
      values   = ["sts.amazonaws.com"]
    }

    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:sub"
      values   = ["repo:${var.github_repository}:ref:${var.github_ref}"]
    }
  }
}

resource "aws_iam_role" "ecr_publisher" {
  name               = local.publisher_role_name
  assume_role_policy = data.aws_iam_policy_document.ecr_publisher_assume_role.json
  tags               = local.common_tags
}

# Policy de menor privilegio: somente as acoes ECR necessarias para build
# once -> push -> verificar digest, escopadas aos 4 ARNs de repositorio
# (nunca "ecr:*" nem Resource "*" para as acoes escopaveis). Sem ECS, sem
# IAM write, sem Terraform apply, sem Secrets Manager, sem SSM, sem RDS,
# sem AdministratorAccess (ver ADR-0013 e WorkflowGovernanceArchitectureTests).
data "aws_iam_policy_document" "ecr_publisher_permissions" {
  statement {
    sid    = "PushAndInspectOwnedRepositories"
    effect = "Allow"
    actions = [
      "ecr:BatchCheckLayerAvailability",
      "ecr:InitiateLayerUpload",
      "ecr:UploadLayerPart",
      "ecr:CompleteLayerUpload",
      "ecr:PutImage",
      "ecr:BatchGetImage",
      "ecr:DescribeImages",
      "ecr:DescribeRepositories",
    ]
    resources = [for c in local.all_published_components : module.ecr[c].repository_arn]
  }

  # ecr:GetAuthorizationToken so e suportada com Resource "*" - a AWS nao
  # oferece escopo por repositorio para esta acao (confirmado via
  # documentacao oficial do IAM/ECR - AWS Documentation MCP).
  statement {
    sid       = "GetAuthorizationToken"
    effect    = "Allow"
    actions   = ["ecr:GetAuthorizationToken"]
    resources = ["*"]
  }

  # Nenhuma permissao KMS e concedida a esta role de proposito: o proprio servico ECR cifra/decifra em nome do
  # principal chamador usando grants que ele mesmo cria na chave configurada
  # (gerenciada pela AWS por padrao - ver comentario acima de "module ecr"),
  # sem exigir nenhuma policy KMS adicional da nossa parte (a key policy da
  # chave gerenciada pela AWS e mantida pela propria AWS). A role de
  # publicacao so precisa interagir com a API do ECR.
}

resource "aws_iam_role_policy" "ecr_publisher" {
  name   = "ecr-publish-least-privilege"
  role   = aws_iam_role.ecr_publisher.id
  policy = data.aws_iam_policy_document.ecr_publisher_permissions.json
}
