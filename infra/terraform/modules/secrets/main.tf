# Cria apenas os containers lógicos (metadados) dos secrets — nome, descrição,
# tags e associação opcional com uma chave KMS. Nenhum valor de secret passa
# por este módulo: o recurso Terraform que gravaria uma versão com valor
# nunca é declarado aqui (ver testes de governança de secrets).
# Os valores reais são gravados depois do apply, via secret-value-bootstrap
# (PutSecretValue pelo AWS SDK), reaproveitando as senhas já geradas pelo
# bootstrap local (ADR-0009) — nunca duplicadas, nunca gravadas no state.
resource "aws_secretsmanager_secret" "this" {
  for_each = var.secrets

  name        = each.key
  description = each.value.description
  kms_key_id  = var.kms_key_id
  tags        = var.tags
}
