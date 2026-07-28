variable "environment" {
  type = string
  validation {
    condition     = contains(["development", "staging", "production"], var.environment)
    error_message = "environment deve ser exatamente 'development', 'staging' ou 'production'."
  }
}

variable "vpc_id" {
  type = string
}

variable "private_subnet_ids" {
  description = "Subnets privadas para o ALB interno e para as ENIs do VPC Link V2 (ambos internos - somente a API Gateway REST tem endpoint público, atrás do WAF; ver ADR-0008 para a cadeia completa client->WAF->API Gateway REST->VPC Link V2->ALB interno->ECS, sem NLB intermediário)."
  type        = list(string)
}

variable "alb_security_group_id" {
  type = string
}

variable "certificate_arn" {
  description = "ARN de um certificado ACM JÁ EXISTENTE e validado para o domínio interno do ALB - este módulo nunca solicita/valida um certificado (fora do escopo de Terraform de aplicação, ver seção de responsabilidades da landing zone)."
  type        = string
}

variable "waf_rate_limit_per_5min" {
  description = "Limite de requisições por IP a cada 5 minutos na regra de rate-based do WAF."
  type        = number
  default     = 2000
}

variable "log_retention_days" {
  type    = number
  default = 30
}

variable "tags" {
  type    = map(string)
  default = {}
}
