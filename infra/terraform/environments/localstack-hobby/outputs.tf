output "queue_url" {
  value = module.messaging.queue_url
}

output "queue_arn" {
  value = module.messaging.queue_arn
}

output "dlq_url" {
  value = module.messaging.dlq_url
}

output "dlq_arn" {
  value = module.messaging.dlq_arn
}

output "secret_names" {
  description = "Nomes dos secrets criados (não sensível - apenas identificadores)."
  value       = module.secrets.secret_names
}

output "secret_arns" {
  description = "ARNs dos secrets criados (não sensível - apenas identificadores)."
  value       = module.secrets.secret_arns
}

output "parameter_names" {
  value = module.parameters.parameter_names
}

output "kms_key_id" {
  value = module.kms.key_id
}

output "kms_key_arn" {
  value = module.kms.key_arn
}

output "kms_alias_name" {
  value = module.kms.alias_name
}

output "iam_role_names" {
  value = module.iam.role_names
}

output "iam_role_arns" {
  value = module.iam.role_arns
}
