# Provider AWS "real" (nao LocalStack, sem endpoints customizados, sem
# credenciais fixas de nenhum tipo) - diferente de
# infra/terraform/environments/localstack-hobby/provider.tf, que aponta para
# LocalStack. Este ambiente nunca foi aplicado.
#
# As variaveis skip_* abaixo tem default "false" (comportamento normal e
# seguro de um provider real). Elas so devem ser sobrescritas para "true"
# em uma sessao de validacao LOCAL e OFFLINE (terraform validate/plan sem
# credenciais reais, sem nunca aplicar) - nunca em uso real contra a conta
# AWS de referencia.
provider "aws" {
  region                      = var.aws_region
  skip_credentials_validation = var.skip_credentials_validation
  skip_region_validation      = var.skip_region_validation
  skip_requesting_account_id  = var.skip_requesting_account_id
}
