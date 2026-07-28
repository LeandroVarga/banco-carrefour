# Repositorio ECR de referencia. Tag imutavel por padrao -
# a tag canonica (sha-<commit completo>) nunca pode ser sobrescrita depois de
# publicada; uma tentativa de publicar o mesmo nome de tag com conteudo
# diferente é rejeitada pelo proprio ECR (comportamento nativo de
# IMMUTABLE), nao por logica deste modulo. Scan-on-push do proprio ECR e
# mantido como defesa em profundidade - o gate real e bloqueante continua
# sendo o Trivy (scripts/ci/scan-images.sh), executado ANTES do push (ver
# docs/security/dependencias-e-supply-chain.md e ADR-0013).
resource "aws_ecr_repository" "this" {
  name                 = var.name
  image_tag_mutability = var.image_tag_mutability

  image_scanning_configuration {
    scan_on_push = var.scan_on_push
  }

  # encryption_type="KMS" com kms_key nulo usa a chave GERENCIADA PELA AWS
  # (alias "aws/ecr", criada automaticamente pela propria AWS na primeira
  # vez que um repositorio com KMS habilitado e criado na conta) - nao uma
  # chave gerenciada pelo cliente. Isso ja e mais controlado que o padrao
  # AES256/S3-managed (auditavel via CloudTrail, rotacionado pela AWS) sem
  # exigir administracao de key policy nem custo de chave dedicada (ver
  # ADR-0013, secao de decisao de criptografia). Um ARN explicito em
  # var.kms_key_arn usa uma chave gerenciada pelo cliente (existente,
  # criada fora deste modulo) quando um driver concreto de governanca
  # justificar o custo/complexidade adicional.
  encryption_configuration {
    encryption_type = var.encryption_type
    kms_key         = var.encryption_type == "KMS" ? var.kms_key_arn : null
  }

  tags = var.tags
}

resource "aws_ecr_lifecycle_policy" "this" {
  count = var.lifecycle_policy != null ? 1 : 0

  repository = aws_ecr_repository.this.name
  policy     = var.lifecycle_policy
}

# Resource policy cross-account: permite que as contas de
# workload (Development/Staging/Production) façam PULL direto desta conta
# Artifacts - nunca push (só a role de publicação desta própria conta
# publica, ver ADR-0013) e nunca gerenciamento (nenhuma ação de delete/
# lifecycle/policy concedida a outra conta).
resource "aws_ecr_repository_policy" "this" {
  count = var.repository_policy != null ? 1 : 0

  repository = aws_ecr_repository.this.name
  policy     = var.repository_policy
}
