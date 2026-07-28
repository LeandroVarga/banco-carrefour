terraform {
  required_version = ">= 1.9.0"

  required_providers {
    aws = {
      source = "hashicorp/aws"
      # ECS deployment_configuration.strategy (CANARY/LINEAR/BLUE_GREEN) e
      # o bloco alarms sao recursos recentes (ver ADR-0014) - nenhuma nota
      # de versao minima foi encontrada na documentacao oficial do
      # provider no momento desta pesquisa (WebFetch contra
      # raw.githubusercontent.com/hashicorp/terraform-provider-aws, ja que
      # o Terraform MCP configurado neste repositorio nao estava
      # alcancavel nesta sessao) - fixamos uma constraint conservadora e
      # documentamos que a versao exata minima deve ser confirmada por um
      # "terraform init" real antes do primeiro "terraform plan" contra
      # uma conta AWS real.
      version = ">= 6.0"
    }
  }
}
