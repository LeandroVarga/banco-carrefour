variable "aws_region" {
  type    = string
  default = "us-east-1"
}

variable "localstack_endpoint" {
  type    = string
  default = "http://localhost:4566"
}

variable "oidc_issuer" {
  description = "Issuer OIDC do realm banco-carrefour (Keycloak) - configuração não sensível, guardada em SSM Parameter Store."
  type        = string
  default     = "https://keycloak.localhost:8443/realms/banco-carrefour"
}
