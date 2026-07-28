# Provider AWS real, sem credenciais estáticas (autenticação via OIDC do
# GitHub Actions no workflow real - ver ADR-0011) e sem endpoints
# customizados. Nunca aplicado nesta sessão (IaC-materializado mas não
# provisionado - ver classificação de evidência).
provider "aws" {
  region                      = var.aws_region
  skip_credentials_validation = var.skip_credentials_validation
  skip_region_validation      = var.skip_region_validation
  skip_requesting_account_id  = var.skip_requesting_account_id

  default_tags {
    tags = {
      project     = "banco-carrefour"
      environment = "production"
      managed_by  = "terraform"
    }
  }
}
