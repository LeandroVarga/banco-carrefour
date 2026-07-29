variable "environment" {
  type = string
  validation {
    condition     = contains(["development", "staging", "production"], var.environment)
    error_message = "environment deve ser exatamente 'development', 'staging' ou 'production'."
  }
}

variable "name_prefix" {
  description = "Prefixo dos nomes dos alarmes (ex.: banco-carrefour-production-ledger-api) - cada workload/ambiente tem seu próprio conjunto, nunca compartilhado entre serviços."
  type        = string
}

variable "alarms" {
  description = <<-EOT
    Mapa de definições de alarme (chave = sufixo do nome). Cada entrada vira
    exatamente um aws_cloudwatch_metric_alarm - este módulo é uma fábrica
    genérica de alarmes (nunca hardcoda métricas de um serviço específico),
    reaproveitado tanto pelos alarmes de ALB (5xx, latência) das APIs
    canary quanto pelos alarmes de fila (DLQ, idade de mensagem) do Worker
    e Publisher.
  EOT
  type = map(object({
    namespace           = string
    metric_name         = string
    statistic           = string
    period_seconds      = number
    evaluation_periods  = number
    threshold           = number
    comparison_operator = string
    dimensions          = map(string)
    treat_missing_data  = optional(string, "notBreaching")
    description         = optional(string, "")
  }))
}

variable "alarm_actions" {
  description = "ARNs de ação em ALARM (ex.: tópico SNS de notificação) - opcional, o gate de rollback do ECS/workflow consome o NOME do alarme diretamente, não depende de uma action aqui."
  type        = list(string)
  default     = []
}

variable "tags" {
  type    = map(string)
  default = {}
}
