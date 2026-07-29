variable "parameters" {
  description = "Mapa nome-do-parametro => configuração. Uso restrito a configuração NÃO sensível (issuer OIDC, audiences, nomes de recursos) — nunca senha, client secret, token, connection string ou private key (ver ADR-0009). Todos os parâmetros são criados como tipo String."
  type = map(object({
    value       = string
    description = optional(string, "")
  }))
}

variable "tags" {
  type    = map(string)
  default = {}
}
