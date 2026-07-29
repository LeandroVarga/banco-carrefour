output "ecr_repository_urls" {
  description = "URL de cada um dos 5 repositorios ECR (4 workloads de negocio + migration-runner operacional - nao sensivel, apenas identificadores)."
  value       = { for c, m in module.ecr : c => m.repository_url }
}

output "ecr_repository_arns" {
  description = "ARN de cada um dos 5 repositorios ECR (4 workloads de negocio + migration-runner operacional - nao sensivel, apenas identificadores)."
  value       = { for c, m in module.ecr : c => m.repository_arn }
}

output "ecr_publisher_role_arn" {
  description = "ARN da role de publicacao assumida via OIDC pelo workflow do GitHub Actions (nao sensivel - a role so pode ser assumida via OIDC, sem credenciais estaticas)."
  value       = aws_iam_role.ecr_publisher.arn
}

output "github_oidc_provider_arn" {
  description = "ARN do provedor OIDC do GitHub Actions usado pela role de publicacao (nao sensivel)."
  value       = local.oidc_provider_arn
}
