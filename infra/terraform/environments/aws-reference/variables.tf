variable "aws_region" {
  description = "Regiao AWS de referencia para ECR/IAM/KMS."
  type        = string
  default     = "us-east-1"
}

variable "aws_account_id" {
  description = "Account ID da conta AWS de referencia, usado para construir ARNs (KMS key policy, provedor OIDC convencional). Sem default de proposito - nunca commitar um Account ID real neste repositorio; forneça via -var ou TF_VAR_aws_account_id no momento de um plan/apply real."
  type        = string
}

variable "github_repository" {
  description = "owner/repo do GitHub cujo token OIDC pode assumir a role de publicacao de imagens (claim 'repository' / 'sub')."
  type        = string
  default     = "LeandroVarga/banco-carrefour"
}

variable "github_ref" {
  description = "Git ref (branch) ao qual a role de publicacao inicial fica restrita - nunca um wildcard que libere qualquer branch. Tags de release podem exigir uma role dedicada futura em vez de ampliar esta."
  type        = string
  default     = "refs/heads/main"
}

variable "create_oidc_provider" {
  description = "Se true, este ambiente cria o provedor OIDC do GitHub Actions (token.actions.githubusercontent.com) nesta conta. Se false, assume que um provedor OIDC ja existe (gerenciado por outro ambiente/organizacao) e apenas referencia o ARN convencional para essa conta."
  type        = bool
  default     = true
}

variable "ecr_kms_key_arn" {
  description = "ARN de uma chave KMS gerenciada pelo cliente, JA EXISTENTE (criada fora deste ambiente), para os 4 repositorios ECR. Se null (default), os repositorios usam encryption_type=KMS com a chave GERENCIADA PELA AWS (\"aws/ecr\") - nenhuma CMK e criada por este ambiente (ver ADR-0013, decisao de criptografia reavaliada em auditoria: sem driver concreto de regulacao/governanca que justifique o custo/complexidade de uma CMK dedicada para as imagens)."
  type        = string
  default     = null
}

variable "ecr_lifecycle_keep_last_tagged" {
  description = "Numero de imagens com a tag canonica sha- a manter por repositorio (candidatas a rollback / forense de vulnerabilidade) antes de expirar as mais antigas."
  type        = number
  default     = 20
}

variable "ecr_lifecycle_untagged_expire_days" {
  description = "Dias apos o push para expirar imagens SEM tag (nunca referenciadas por nenhum deploy)."
  type        = number
  default     = 14
}

variable "workload_account_ids" {
  description = "Account IDs das contas de workload (Development/Staging/Production - ) autorizadas a fazer PULL cross-account dos 4 repositorios ECR desta conta Artifacts - nunca push, nunca gerenciamento. Lista vazia (default) nao concede nenhum acesso cross-account."
  type        = list(string)
  default     = []
}

variable "tags" {
  type    = map(string)
  default = {}
}

# --- Variaveis exclusivas de validacao offline local (nunca usar em uso real) ---

variable "skip_credentials_validation" {
  description = "Uso exclusivo de 'terraform validate/plan' local e offline, sem credenciais reais. Nunca deve ser true em uso real contra a conta AWS de referencia."
  type        = bool
  default     = false
}

variable "skip_region_validation" {
  description = "Uso exclusivo de validacao offline local."
  type        = bool
  default     = false
}

variable "skip_requesting_account_id" {
  description = "Uso exclusivo de validacao offline local. Nunca deve ser true em uso real (o provider precisa confirmar a conta AWS de destino real)."
  type        = bool
  default     = false
}
