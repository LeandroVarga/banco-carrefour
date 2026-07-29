variable "environment" {
  type = string
  validation {
    condition     = contains(["development", "staging", "production"], var.environment)
    error_message = "environment deve ser exatamente 'development', 'staging' ou 'production'."
  }
}

variable "enable_container_insights" {
  description = "Habilita Container Insights (métricas detalhadas de CPU/memória por task/serviço) - custo adicional de CloudWatch, recomendado para Staging/Production."
  type        = bool
  default     = true
}

variable "tags" {
  type    = map(string)
  default = {}
}
