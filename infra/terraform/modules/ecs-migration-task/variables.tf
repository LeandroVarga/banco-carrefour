variable "environment" {
  type = string
  validation {
    condition     = contains(["development", "staging", "production"], var.environment)
    error_message = "environment deve ser exatamente 'development', 'staging' ou 'production'."
  }
}

variable "boundary" {
  description = "Fronteira desta instancia do modulo - cada fronteira ganha sua PROPRIA task definition/family, security group e task role, nunca compartilhados entre Ledger e Consolidation."
  type        = string
  validation {
    condition     = contains(["ledger", "consolidation"], var.boundary)
    error_message = "boundary deve ser exatamente 'ledger' ou 'consolidation'."
  }
}

variable "aws_region" {
  type = string
}

variable "vpc_id" {
  type = string
}

variable "private_subnet_ids" {
  type = list(string)
}

variable "rds_security_group_id" {
  description = "Security group do RDS (compartilhado por Ledger e Consolidation neste ambiente) - destino do egress de PostgreSQL desta task."
  type        = string
}

variable "vpc_endpoints_security_group_id" {
  description = "Security group dos VPC endpoints de interface (Secrets Manager, ECR, CloudWatch Logs) - null quando o ambiente não usa VPC endpoints (NAT Gateway cobre o egress nesse caso, sem exigir uma regra de ingress adicional)."
  type        = string
  default     = null
}

variable "cpu" {
  type    = number
  default = 512
}

variable "memory" {
  type    = number
  default = 1024
}

variable "ephemeral_storage_gib" {
  description = "Armazenamento efêmero do Fargate - mínimo suportado é 21 GiB (ver AWS Fargate task storage)."
  type        = number
  default     = 21
}

variable "image" {
  description = "Imagem ECR do migration-runner, qualificada por digest - nunca uma tag mutável, nunca 'latest'."
  type        = string

  validation {
    condition     = can(regex("@sha256:[0-9a-f]{64}$", var.image))
    error_message = "image deve ser uma referência ECR qualificada por digest (...@sha256:<64 hex>), nunca uma tag mutável."
  }
}

variable "non_root_user" {
  type    = string
  default = "1654:1654"
}

variable "task_execution_role_arn" {
  type = string
}

variable "task_role_arn" {
  type = string
}

variable "log_group_name" {
  type = string
}

variable "environment_variables" {
  description = "Variáveis de ambiente não sensíveis (ex.: ConnectionStrings__Ledger, ConnectionStrings__Consolidation - host/porta/database nunca são segredo) - a fase (migrate, nunca contract) é decidida pelo comando/overrides do 'aws ecs run-task', nunca fixada na task definition."
  type        = map(string)
  default     = {}
}

variable "secrets" {
  description = "Mapa nome-da-variável -> ARN do secret (Secrets Manager) para injeção NATIVA do ECS (via execution role) - vazio por padrão, já que o MigrationRunner resolve o secret de conexão ele mesmo via SDK (usando a permissão da task role, --secret-name da CLI) e nunca depende deste mecanismo. Se usado, deve conter APENAS o secret desta fronteira (ledger-migration OU consolidation-migration, nunca ambos -, seção 6)."
  type        = map(string)
  default     = {}
}

variable "tags" {
  type    = map(string)
  default = {}
}

variable "alarm_actions" {
  description = "ARNs de acao em ALARM (ex.: topico SNS) para os 2 alarmes de observabilidade de migracao deste modulo - opcional, mesma convencao de infra/terraform/modules/deployment-alarms (o nome do alarme, nao a action, e o contrato consumido por auditoria/documentacao)."
  type        = list(string)
  default     = []
}

variable "task_lifecycle_log_retention_days" {
  description = "Retencao do log group dedicado (/aws/events/...) que recebe os eventos brutos 'ECS Task State Change' via EventBridge - baixa por padrao, pois esses eventos ja sao resumidos pelo alarme; o log completo de execucao da migracao (StructuredLog) fica no log group de aplicacao, com sua propria retencao."
  type        = number
  default     = 14
}
