# --- Identidade da conta/ambiente ---
variable "aws_region" {
  type    = string
  default = "us-east-1"
}

variable "aws_account_id" {
  description = "Account ID da conta de WORKLOAD Staging (distinta da conta Artifacts/Tooling de infra/terraform/environments/aws-reference e da conta Production) - sem default de propósito, nunca commitar um Account ID real."
  type        = string
}

variable "artifacts_account_id" {
  description = "Account ID da conta Artifacts/Tooling (onde os 4 repositórios ECR residem - infra/terraform/environments/aws-reference) - usado para montar a policy de pull cross-account."
  type        = string
}

variable "ecr_region" {
  description = "Região do Amazon ECR na conta Artifacts (pode diferir da região de workload, embora o padrão seja a mesma)."
  type        = string
  default     = "us-east-1"
}

# --- Rede ---
variable "vpc_cidr" {
  type    = string
  default = "10.21.0.0/16"
}

variable "availability_zones" {
  type    = list(string)
  default = ["us-east-1a", "us-east-1b"]
}

variable "public_subnet_cidrs" {
  type    = list(string)
  default = ["10.21.0.0/24", "10.21.1.0/24"]
}

variable "private_subnet_cidrs" {
  type    = list(string)
  default = ["10.21.10.0/24", "10.21.11.0/24"]
}

variable "single_nat_gateway" {
  description = "Staging usa um único NAT Gateway compartilhado por padrão (custo) - avalie override para false quando uma avaliação de paridade completa de disponibilidade com Production for necessária."
  type        = bool
  default     = true
}

variable "certificate_arn" {
  description = "ARN de um certificado ACM já existente e validado para o domínio interno do ALB - nunca gerenciado por este ambiente."
  type        = string
}

# --- GitHub OIDC / deploy ---
variable "github_repository" {
  type    = string
  default = "LeandroVarga/banco-carrefour"
}

variable "github_environment" {
  description = "Nome do GitHub Environment que autoriza deploys em Staging - deve ser exatamente 'staging' (revisão opcional/mais leve que Production, configurada externamente, ver ADR-0014)."
  type        = string
  default     = "staging"

  validation {
    condition     = var.github_environment == "staging"
    error_message = "github_environment deste ambiente deve ser exatamente 'staging'."
  }
}

# --- Release aprovada (mesmos digests ECR de Staging, nunca reconstruídos) ---
variable "ledger_api_image" {
  type = string
}

variable "ledger_outbox_publisher_image" {
  type = string
}

variable "consolidation_api_image" {
  type = string
}

variable "consolidation_worker_primary_image" {
  type = string
}

variable "consolidation_worker_canary_image" {
  description = "Imagem candidata em avaliação de capacity canary - null quando não há canário ativo."
  type        = string
  default     = null
}

variable "consolidation_worker_canary_desired_count" {
  type    = number
  default = 0
}

variable "migration_runner_image" {
  description = "Imagem ECR do migration-runner, qualificada por digest - mesmo commit de origem das 4 imagens de negocio, nunca latest."
  type        = string
}

# --- Dimensionamento (escala reduzida mas representativa - topologia igual a Production) ---
variable "ledger_api_cpu" {
  type    = number
  default = 1024
}
variable "ledger_api_memory" {
  type    = number
  default = 2048
}
variable "ledger_api_desired_count" {
  type    = number
  default = 2
}
variable "ledger_api_min_capacity" {
  type    = number
  default = 2
}
variable "ledger_api_max_capacity" {
  type    = number
  default = 6
}

variable "consolidation_api_cpu" {
  type    = number
  default = 1024
}
variable "consolidation_api_memory" {
  type    = number
  default = 2048
}
variable "consolidation_api_desired_count" {
  type    = number
  default = 2
}
variable "consolidation_api_min_capacity" {
  type    = number
  default = 2
}
variable "consolidation_api_max_capacity" {
  type    = number
  default = 6
}

variable "consolidation_worker_cpu" {
  type    = number
  default = 512
}
variable "consolidation_worker_memory" {
  type    = number
  default = 1024
}
variable "consolidation_worker_primary_desired_count" {
  type    = number
  default = 2
}
variable "consolidation_worker_min_capacity" {
  type    = number
  default = 2
}
variable "consolidation_worker_max_capacity" {
  type    = number
  default = 6
}

variable "ledger_outbox_publisher_cpu" {
  type    = number
  default = 512
}
variable "ledger_outbox_publisher_memory" {
  type    = number
  default = 1024
}
variable "ledger_outbox_publisher_desired_count" {
  type    = number
  default = 2
}
variable "ledger_outbox_publisher_min_capacity" {
  type    = number
  default = 2
}
variable "ledger_outbox_publisher_max_capacity" {
  type    = number
  default = 6
}

# --- RDS ---
variable "rds_ledger_instance_class" {
  type    = string
  default = "db.t4g.large"
}
variable "rds_consolidation_instance_class" {
  type    = string
  default = "db.t4g.large"
}
variable "rds_multi_az" {
  description = "Opcional em Staging (avaliação de paridade com Production) - default false por custo; recomenda-se true em pelo menos uma rodada de rehearsal de rollback antes de uma promoção real."
  type        = bool
  default     = false
}
variable "rds_deletion_protection" {
  type    = bool
  default = false
}
variable "rds_backup_retention_days" {
  type    = number
  default = 14
}

# --- Observabilidade ---
variable "log_retention_days" {
  type    = number
  default = 30
}

# --- Validação offline local (nunca true em uso real) ---
variable "skip_credentials_validation" {
  type    = bool
  default = false
}
variable "skip_region_validation" {
  type    = bool
  default = false
}
variable "skip_requesting_account_id" {
  type    = bool
  default = false
}
