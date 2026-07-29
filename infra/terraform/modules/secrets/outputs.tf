output "secret_arns" {
  description = "Mapa nome-do-secret => ARN. Não sensível (identificador, não valor)."
  value       = { for name, secret in aws_secretsmanager_secret.this : name => secret.arn }
}

output "secret_names" {
  description = "Mapa nome-do-secret => nome (eco), útil para compor a chave de leitura na aplicação."
  value       = { for name, secret in aws_secretsmanager_secret.this : name => secret.name }
}
