variable "environment" {
  type = string
  validation {
    condition     = contains(["development", "staging", "production"], var.environment)
    error_message = "environment deve ser exatamente 'development', 'staging' ou 'production'."
  }
}

variable "service_name" {
  description = "Nome curto do workload (ex.: ledger-api, consolidation-api) - usado em nomes de recursos e como container_name da task definition."
  type        = string
}

variable "aws_region" {
  type = string
}

variable "cluster_arn" {
  type = string
}

variable "image" {
  description = "Referência de imagem QUALIFICADA POR DIGEST no Amazon ECR (<conta>.dkr.ecr.<região>.amazonaws.com/banco-carrefour/<componente>@sha256:<digest>) - nunca uma tag, nunca 'latest'. Vem do manifesto de release real (schema 4.0.0), nunca hardcoded."
  type        = string

  validation {
    condition     = can(regex("^[0-9]{12}\\.dkr\\.ecr\\.[a-z0-9-]+\\.amazonaws\\.com/banco-carrefour/[a-z-]+@sha256:[0-9a-f]{64}$", var.image))
    error_message = "image deve ser uma referência ECR qualificada por digest (registry/repositorio@sha256:...), nunca uma tag."
  }
}

variable "cpu" {
  type = number
}

variable "memory" {
  type = number
}

variable "container_port" {
  type    = number
  default = 8080
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

variable "vpc_id" {
  type = string
}

variable "alb_https_listener_arn" {
  type = string
}

variable "path_pattern" {
  description = "Padrão de path da listener_rule do ALB (ex.: /ledger/*)."
  type        = string
}

variable "listener_rule_priority" {
  type = number
}

variable "health_check_path" {
  type = string
}

variable "task_execution_role_arn" {
  description = "Role usada pelo ECS para fazer pull da imagem, escrever logs e RESOLVER os 'secrets' da container definition (Secrets Manager/SSM) - específica deste workload, nunca compartilhada com os outros 3 (ver módulo iam)."
  type        = string
}

variable "task_role_arn" {
  description = "Role assumida pela APLICAÇÃO em runtime (permissões de negócio: SQS, Secrets Manager, SSM) - específica deste workload, nunca compartilhada com os outros 3."
  type        = string
}

variable "environment_variables" {
  type    = map(string)
  default = {}
}

variable "secrets" {
  description = "Mapa NOME_DA_VAR -> ARN completo (Secrets Manager secret ARN ou SSM parameter ARN) injetado via 'secrets' da container definition - nunca um valor literal aqui."
  type        = map(string)
  default     = {}
}

variable "log_group_name" {
  type = string
}

variable "non_root_user" {
  description = "UID:GID não-root da imagem (ver scripts/ci/build-images-for-supply-chain.sh) - a task definition nunca sobrescreve para root."
  type        = string
  default     = "1654:1654"
}

# --- Estratégia de deployment nativa do ECS (ver ADR-0014) ---
variable "canary_percent" {
  type    = number
  default = 10.0
}

variable "canary_bake_time_minutes" {
  type    = number
  default = 10
}

variable "bake_time_minutes" {
  description = "Tempo de observação após 100% do tráfego migrar, antes de encerrar a revisão anterior."
  type        = number
  default     = 15
}

variable "http_5xx_threshold" {
  description = "Contagem de respostas 5xx do target group (por período de 60s, 3 avaliações) acima da qual o alarme de rollback dispara."
  type        = number
  default     = 10
}

variable "p99_latency_threshold_seconds" {
  description = "Latência p99 do target group acima da qual o alarme de rollback dispara."
  type        = number
  default     = 2
}

variable "extra_alarm_names" {
  description = "Nomes de alarmes CloudWatch adicionais (criados fora deste módulo) a incluir no gate de rollback automático do ECS, além dos alarmes de ALB (5xx/latência) que este módulo já cria internamente (o target group só existe DEPOIS deste módulo rodar - por isso os alarmes de ALB são sempre internos, nunca recebidos como input, evitando uma dependência circular)."
  type        = list(string)
  default     = []
}

variable "otel_endpoint" {
  type = string
}

variable "tags" {
  type    = map(string)
  default = {}
}
