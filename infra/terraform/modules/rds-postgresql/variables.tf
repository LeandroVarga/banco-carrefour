variable "identifier" {
  description = "Identificador da instância RDS (ex.: banco-carrefour-development-ledger)."
  type        = string
}

variable "engine_version" {
  description = "Versão do PostgreSQL - fixada explicitamente (nunca 'latest' implícito) para reprodutibilidade entre ambientes."
  type        = string
  default     = "16.4"
}

variable "instance_class" {
  description = "Classe de instância RDS - dimensionada por ambiente (menor em Development, maior em Production). Sem default de proposito: forçar uma escolha explícita e revisável por ambiente."
  type        = string
}

variable "allocated_storage_gb" {
  type    = number
  default = 20
}

variable "max_allocated_storage_gb" {
  description = "Teto de storage autoscaling - 0 desabilita (nunca crescimento ilimitado sem teto)."
  type        = number
  default     = 100
}

variable "database_name" {
  type = string
}

variable "master_username" {
  description = "Usuário mestre RDS - nunca usado pela aplicação em runtime (a aplicação usa roles de menor privilégio criadas via migrations/bootstrap, mesmo modelo do ADR-0009). Usado apenas para provisionamento inicial e rotação administrativa."
  type        = string
  default     = "postgres"
}

variable "master_password_secret_arn" {
  description = "ARN de um secret do Secrets Manager JÁ EXISTENTE contendo a senha mestre (nunca uma senha em texto plano em variável Terraform/state) - ver módulo secrets. RDS-managed master password (manage_master_user_password) é usado quando esta variável for null."
  type        = string
  default     = null
}

variable "multi_az" {
  description = "Multi-AZ real (standby síncrono em outra AZ, failover automático) - obrigatório para Production, opcional para Staging (avaliação de custo/paridade com Production), tipicamente false em Development."
  type        = bool
  default     = false
}

variable "backup_retention_days" {
  type    = number
  default = 7

  validation {
    condition     = var.backup_retention_days >= 1 && var.backup_retention_days <= 35
    error_message = "backup_retention_days deve estar entre 1 e 35 (limite do RDS para backups automatizados)."
  }
}

variable "deletion_protection" {
  description = "Bloqueia 'terraform destroy'/console delete sem desabilitar primeiro - obrigatório para Production."
  type        = bool
  default     = false
}

variable "skip_final_snapshot" {
  description = "Se true, NÃO cria snapshot final ao destruir - nunca true em Production (perda de dados irreversível)."
  type        = bool
  default     = false
}

variable "performance_insights_enabled" {
  type    = bool
  default = true
}

variable "performance_insights_retention_days" {
  description = "7 dias é o tier gratuito; retenções maiores (ex.: 731 dias) têm custo adicional - avaliar por ambiente."
  type        = number
  default     = 7
}

variable "monitoring_interval_seconds" {
  description = "Enhanced Monitoring - 0 desabilita. Valores válidos: 0, 1, 5, 10, 15, 30, 60."
  type        = number
  default     = 60
}

variable "monitoring_role_arn" {
  description = "ARN da role IAM para Enhanced Monitoring - obrigatório quando monitoring_interval_seconds > 0."
  type        = string
  default     = null
}

variable "vpc_security_group_ids" {
  type = list(string)
}

variable "subnet_ids" {
  description = "Subnets PRIVADAS para o DB Subnet Group - RDS nunca em subnet pública."
  type        = list(string)
}

variable "apply_immediately" {
  description = "Se false (recomendado para Production), alterações esperam a próxima maintenance window em vez de aplicar imediatamente (evita reinício inesperado em horário de pico)."
  type        = bool
  default     = false
}

variable "parameters" {
  description = "Parâmetros do DB Parameter Group (family postgres16) - mapa nome->valor."
  type        = map(string)
  default     = {}
}

variable "tags" {
  type    = map(string)
  default = {}
}
