variable "name" {
  description = "Nome completo do repositorio ECR (ex.: banco-carrefour/ledger-api)."
  type        = string
}

variable "image_tag_mutability" {
  description = "IMMUTABLE (recomendado para repositorios de referencia de producao - a tag SHA canonica nunca pode ser sobrescrita) ou MUTABLE."
  type        = string
  default     = "IMMUTABLE"

  validation {
    condition     = contains(["IMMUTABLE", "MUTABLE"], var.image_tag_mutability)
    error_message = "image_tag_mutability deve ser \"IMMUTABLE\" ou \"MUTABLE\"."
  }
}

variable "scan_on_push" {
  description = "Habilita o scan basico de vulnerabilidade do proprio ECR no push (defesa em profundidade - nao substitui o scan Trivy que já bloqueia antes do push)."
  type        = bool
  default     = true
}

variable "encryption_type" {
  description = "AES256 (padrao gerenciado pela AWS/S3, sem KMS) ou KMS (server-side encryption via AWS KMS - com kms_key_arn nulo usa a chave GERENCIADA PELA AWS \"aws/ecr\"; com um ARN explicito usa uma chave gerenciada pelo cliente ja existente)."
  type        = string
  default     = "KMS"

  validation {
    condition     = contains(["AES256", "KMS"], var.encryption_type)
    error_message = "encryption_type deve ser \"AES256\" ou \"KMS\"."
  }
}

variable "kms_key_arn" {
  description = "ARN de uma chave KMS gerenciada pelo cliente (ja existente, criada fora deste modulo) para usar em vez da chave gerenciada pela AWS. So tem efeito quando encryption_type=\"KMS\". Se null (default), usa a chave gerenciada pela AWS (\"aws/ecr\")."
  type        = string
  default     = null
}

variable "lifecycle_policy" {
  description = "Documento JSON da politica de lifecycle do ECR (ver aws_ecr_lifecycle_policy). Se null, nenhuma politica de lifecycle e criada para este repositorio."
  type        = string
  default     = null
}

variable "repository_policy" {
  description = "Documento JSON de resource policy do repositorio (ver aws_ecr_repository_policy) - usado para permitir pull cross-account (contas de workload Development/Staging/Production lendo desta conta Artifacts, ver ADR-0011). Se null, nenhuma resource policy e criada (repositorio acessivel apenas por principals da propria conta)."
  type        = string
  default     = null
}

variable "tags" {
  type    = map(string)
  default = {}
}
