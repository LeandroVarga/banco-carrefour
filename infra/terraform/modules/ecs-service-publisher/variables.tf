variable "environment" {
  type = string
  validation {
    condition     = contains(["development", "staging", "production"], var.environment)
    error_message = "environment deve ser exatamente 'development', 'staging' ou 'production'."
  }
}

variable "service_name" {
  type    = string
  default = "ledger-outbox-publisher"
}

variable "aws_region" {
  type = string
}

variable "cluster_arn" {
  type = string
}

variable "image" {
  type = string
  validation {
    condition     = can(regex("^[0-9]{12}\\.dkr\\.ecr\\.[a-z0-9-]+\\.amazonaws\\.com/banco-carrefour/ledger-outbox-publisher@sha256:[0-9a-f]{64}$", var.image))
    error_message = "image deve ser uma referência ECR qualificada por digest do repositório ledger-outbox-publisher."
  }
}

variable "cpu" {
  type = number
}

variable "memory" {
  type = number
}

variable "desired_count" {
  type = number
}

variable "min_capacity" {
  type = number
}

variable "max_capacity" {
  type = number
}

variable "private_subnet_ids" {
  type = list(string)
}

variable "security_group_id" {
  type = string
}

variable "task_execution_role_arn" {
  type = string
}

variable "task_role_arn" {
  type = string
}

variable "environment_variables" {
  type    = map(string)
  default = {}
}

variable "secrets" {
  type    = map(string)
  default = {}
}

variable "log_group_name" {
  type = string
}

variable "non_root_user" {
  type    = string
  default = "1654:1654"
}

variable "otel_endpoint" {
  type = string
}

# --- Rolling deployment controlado (ver ADR-0014) ---
variable "minimum_healthy_percent" {
  description = "Percentual mínimo de tasks saudáveis durante o rollout - nunca 0 (garante que a capacidade nunca zera durante um deploy)."
  type        = number
  default     = 100
}

variable "maximum_percent" {
  description = "Percentual máximo de tasks (saudáveis + novas) durante o rollout - 200 permite substituição completa sem esperar a antiga sair primeiro."
  type        = number
  default     = 200
}

variable "stop_timeout_seconds" {
  description = "Tempo entre SIGTERM e SIGKILL - deve ser maior que o pior caso de: parar de aceitar novos claims + concluir ou liberar com segurança o trabalho já reivindicado (graceful shutdown real da aplicação, ver ADR-0004)."
  type        = number
  default     = 120

  validation {
    condition     = var.stop_timeout_seconds >= 30 && var.stop_timeout_seconds <= 120
    error_message = "stop_timeout_seconds deve estar entre 30 e 120 (limite máximo suportado pelo ECS para SIGKILL após SIGTERM)."
  }
}

variable "alarm_names" {
  description = "Alarmes (backlog da Outbox, idade do item mais antigo, falhas de publicação) que acionam rollback automático via deployment_circuit_breaker + alarms."
  type        = list(string)
}

variable "tags" {
  type    = map(string)
  default = {}
}
