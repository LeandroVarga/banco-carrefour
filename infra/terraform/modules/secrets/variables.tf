variable "secrets" {
  description = "Mapa nome-do-secret => descrição. Apenas metadados: nenhum valor de secret é aceito por esta variável (ADR-0009 — o valor é gravado depois via secret-value-bootstrap, nunca pelo Terraform)."
  type = map(object({
    description = string
  }))
}

variable "kms_key_id" {
  description = "ARN ou ID da chave KMS usada para criptografar os valores armazenados nestes secrets. Null usa a chave gerenciada padrão do Secrets Manager (aws/secretsmanager)."
  type        = string
  default     = null
}

variable "tags" {
  type    = map(string)
  default = {}
}
