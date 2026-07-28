# --- Identidade da conta/ambiente ---
variable "aws_region" {
  type    = string
  default = "us-east-1"
}

variable "aws_account_id" {
  description = "Account ID da conta de WORKLOAD Development (distinta da conta Artifacts/Tooling de infra/terraform/environments/aws-reference e das contas Staging/Production) - sem default de propósito, nunca commitar um Account ID real."
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
  default = "10.22.0.0/16"
}

variable "availability_zones" {
  type    = list(string)
  default = ["us-east-1a", "us-east-1b"]
}

variable "public_subnet_cidrs" {
  type    = list(string)
  default = ["10.22.0.0/24", "10.22.1.0/24"]
}

variable "private_subnet_cidrs" {
  type    = list(string)
  default = ["10.22.10.0/24", "10.22.11.0/24"]
}

variable "single_nat_gateway" {
  description = "Development usa um único NAT Gateway compartilhado (custo mínimo - indisponibilidade de egress é aceitável neste ambiente)."
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
  description = "Nome do GitHub Environment que autoriza deploys em Development - deve ser exatamente 'development'. Diferente de Staging/Production, não exige revisores obrigatórios (deploy automático após publicação real no ECR, ver ADR-0014)."
  type        = string
  default     = "development"

  validation {
    condition     = var.github_environment == "development"
    error_message = "github_environment deste ambiente deve ser exatamente 'development'."
  }
}

# --- Release aprovada (mesmos digests recém-publicados no ECR, nunca reconstruídos) ---
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
  description = "Development normalmente não usa capacity canary (deploy direto do primary) - mantido por consistência estrutural com Staging/Production."
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

# --- Dimensionamento (mínimo viável - sem paridade com Production) ---
variable "ledger_api_cpu" {
  type    = number
  default = 512
}
variable "ledger_api_memory" {
  type    = number
  default = 1024
}
variable "ledger_api_desired_count" {
  type    = number
  default = 1
}
variable "ledger_api_min_capacity" {
  type    = number
  default = 1
}
variable "ledger_api_max_capacity" {
  type    = number
  default = 2
}

variable "consolidation_api_cpu" {
  type    = number
  default = 512
}
variable "consolidation_api_memory" {
  type    = number
  default = 1024
}
variable "consolidation_api_desired_count" {
  type    = number
  default = 1
}
variable "consolidation_api_min_capacity" {
  type    = number
  default = 1
}
variable "consolidation_api_max_capacity" {
  type    = number
  default = 2
}

variable "consolidation_worker_cpu" {
  type    = number
  default = 256
}
variable "consolidation_worker_memory" {
  type    = number
  default = 512
}
variable "consolidation_worker_primary_desired_count" {
  type    = number
  default = 1
}
variable "consolidation_worker_min_capacity" {
  type    = number
  default = 1
}
variable "consolidation_worker_max_capacity" {
  type    = number
  default = 2
}

variable "ledger_outbox_publisher_cpu" {
  type    = number
  default = 256
}
variable "ledger_outbox_publisher_memory" {
  type    = number
  default = 512
}
variable "ledger_outbox_publisher_desired_count" {
  type    = number
  default = 1
}
variable "ledger_outbox_publisher_min_capacity" {
  type    = number
  default = 1
}
variable "ledger_outbox_publisher_max_capacity" {
  type    = number
  default = 2
}

# --- RDS ---
variable "rds_ledger_instance_class" {
  type    = string
  default = "db.t4g.micro"
}
variable "rds_consolidation_instance_class" {
  type    = string
  default = "db.t4g.micro"
}
variable "rds_multi_az" {
  type    = bool
  default = false
}
variable "rds_deletion_protection" {
  type    = bool
  default = false
}
variable "rds_backup_retention_days" {
  type    = number
  default = 3
}

# --- Observabilidade ---
variable "log_retention_days" {
  type    = number
  default = 7
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
