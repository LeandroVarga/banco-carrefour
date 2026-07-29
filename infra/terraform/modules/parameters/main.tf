# SSM Parameter Store para configuração NÃO sensível apenas (issuer OIDC,
# audiences, nomes de recursos). Nenhum parâmetro SecureString é criado por
# este módulo — se um valor sensível precisar de gestão via SSM no futuro,
# a decisão deve ser reavaliada explicitamente (ver ADR-0009), não adicionada
# aqui por conveniência.
resource "aws_ssm_parameter" "this" {
  for_each = var.parameters

  name        = each.key
  type        = "String"
  value       = each.value.value
  description = each.value.description
  tags        = var.tags
}
