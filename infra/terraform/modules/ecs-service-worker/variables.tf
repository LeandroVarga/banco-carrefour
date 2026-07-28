variable "environment" {
  type = string
  validation {
    condition     = contains(["development", "staging", "production"], var.environment)
    error_message = "environment deve ser exatamente 'development', 'staging' ou 'production'."
  }
}

variable "service_name" {
  description = "Nome curto do workload (consolidation-worker)."
  type        = string
  default     = "consolidation-worker"
}

variable "aws_region" {
  type = string
}

variable "cluster_arn" {
  type = string
}

variable "cpu" {
  type = number
}

variable "memory" {
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

# --- Capacity canary (ver ADR-0014): sem ALB, o ECS não tem
# uma estratégia nativa de traffic-shift para consumidores de fila - o
# padrão aqui é DOIS aws_ecs_service independentes (primary + canary)
# consumindo a MESMA fila, com a fração de mensagens processadas pelo
# canário sendo aproximada e dirigida por CAPACIDADE relativa (desired_count),
# nunca uma porcentagem exata de tráfego como no ALB. A promoção real
# (canary vira primary, serviço canary é removido) é uma mudança de
# variáveis conduzida pelo workflow de deploy (promote_canary=true),
# nunca uma chamada AWS ad-hoc fora do Terraform.
variable "primary_image" {
  description = "Imagem ECR (qualificada por digest) da revisão APROVADA e estável, atualmente servindo 100% da capacidade normal."
  type        = string

  validation {
    condition     = can(regex("^[0-9]{12}\\.dkr\\.ecr\\.[a-z0-9-]+\\.amazonaws\\.com/banco-carrefour/consolidation-worker@sha256:[0-9a-f]{64}$", var.primary_image))
    error_message = "primary_image deve ser uma referência ECR qualificada por digest do repositório consolidation-worker."
  }
}

variable "primary_desired_count" {
  type = number
}

variable "canary_image" {
  description = "Imagem candidata em avaliação - null/mesma que primary_image quando não há canário ativo (canary_desired_count deve ser 0 nesse caso)."
  type        = string
  default     = null
}

variable "canary_desired_count" {
  description = "Capacidade do serviço canário - 0 remove efetivamente o canário (nenhuma task rodando), sem destruir o serviço em si na maioria das transições (reduz custo/latência de recriação entre avaliações sucessivas)."
  type        = number
  default     = 0
}

variable "min_capacity" {
  description = "Aplicado apenas ao serviço primary - o canário nunca tem autoscaling próprio (capacidade sempre explicitamente controlada pelo workflow de deploy)."
  type        = number
}

variable "max_capacity" {
  type = number
}

variable "alarm_names" {
  description = "Alarmes (profundidade de DLQ, idade da mensagem mais antiga, atraso de projeção, falhas de processamento) aplicados a AMBOS os serviços (primary e canary) - se dispararem durante uma avaliação de canário, o circuit breaker reverte automaticamente o serviço afetado."
  type        = list(string)
}

variable "tags" {
  type    = map(string)
  default = {}
}
