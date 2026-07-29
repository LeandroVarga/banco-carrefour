variable "environment" {
  type = string
  validation {
    condition     = contains(["development", "staging", "production"], var.environment)
    error_message = "environment deve ser exatamente 'development', 'staging' ou 'production'."
  }
}

variable "log_groups" {
  description = "Mapa nome-curto -> retenção em dias, um /aws/ecs/banco-carrefour-<environment>/<nome-curto> por workload (Ledger.Api, Ledger.OutboxPublisher, Consolidation.Api, Consolidation.Worker) - retenção maior em Production, menor em Development (custo)."
  type        = map(number)
}

variable "tags" {
  type    = map(string)
  default = {}
}
