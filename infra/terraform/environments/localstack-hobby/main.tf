module "messaging" {
  source = "../../modules/messaging"

  queue_name                    = "financial-entry-registered"
  dlq_name                      = "financial-entry-registered-dlq"
  max_receive_count             = 3
  visibility_timeout_seconds    = 5
  receive_wait_time_seconds     = 10
  message_retention_seconds     = 345600
  dlq_message_retention_seconds = 1209600
  tags = {
    project     = "banco-carrefour"
    environment = "localstack-hobby"
    managed_by  = "terraform"
  }
}

module "kms" {
  source = "../../modules/kms"

  description = "Chave de criptografia dos secrets do banco-carrefour "
  alias_name  = "banco-carrefour/secrets"
  tags = {
    project     = "banco-carrefour"
    environment = "localstack-hobby"
    managed_by  = "terraform"
  }
}

module "secrets" {
  source = "../../modules/secrets"

  kms_key_id = module.kms.key_id
  secrets = {
    "banco-carrefour/ledger-api/db-credentials" = {
      description = "Credenciais PostgreSQL da role ledger_api (ADR-0009/ADR-0009). Valor gravado por secret-value-bootstrap, nunca pelo Terraform."
    }
    "banco-carrefour/ledger-outbox-publisher/db-credentials" = {
      description = "Credenciais PostgreSQL da role ledger_outbox_publisher (ADR-0009/ADR-0009). Valor gravado por secret-value-bootstrap, nunca pelo Terraform."
    }
    "banco-carrefour/consolidation-api/db-credentials" = {
      description = "Credenciais PostgreSQL da role consolidation_api_readonly (ADR-0009/ADR-0009). Valor gravado por secret-value-bootstrap, nunca pelo Terraform."
    }
    "banco-carrefour/consolidation-worker/db-credentials" = {
      description = "Credenciais PostgreSQL da role consolidation_worker (ADR-0009/ADR-0009). Valor gravado por secret-value-bootstrap, nunca pelo Terraform."
    }
  }
  tags = {
    project     = "banco-carrefour"
    environment = "localstack-hobby"
    managed_by  = "terraform"
  }
}

module "parameters" {
  source = "../../modules/parameters"

  parameters = {
    "/banco-carrefour/oidc/issuer" = {
      value       = var.oidc_issuer
      description = "Issuer OIDC do realm banco-carrefour (Keycloak) - configuração não sensível."
    }
    "/banco-carrefour/oidc/ledger-audience" = {
      value       = "ledger-api"
      description = "Audience esperada pelo Ledger.Api - configuração não sensível."
    }
    "/banco-carrefour/oidc/consolidation-audience" = {
      value       = "consolidation-api"
      description = "Audience esperada pelo Consolidation.Api - configuração não sensível."
    }
  }
  tags = {
    project     = "banco-carrefour"
    environment = "localstack-hobby"
    managed_by  = "terraform"
  }
}

module "iam" {
  source = "../../modules/iam"

  roles = {
    "banco-carrefour-ledger-api" = {
      secret_arns = [module.secrets.secret_arns["banco-carrefour/ledger-api/db-credentials"]]
      parameter_arns = [
        module.parameters.parameter_arns["/banco-carrefour/oidc/issuer"],
        module.parameters.parameter_arns["/banco-carrefour/oidc/ledger-audience"],
      ]
      kms_key_arns = [module.kms.key_arn]
    }
    "banco-carrefour-ledger-outbox-publisher" = {
      secret_arns  = [module.secrets.secret_arns["banco-carrefour/ledger-outbox-publisher/db-credentials"]]
      kms_key_arns = [module.kms.key_arn]
    }
    "banco-carrefour-consolidation-api" = {
      secret_arns = [module.secrets.secret_arns["banco-carrefour/consolidation-api/db-credentials"]]
      parameter_arns = [
        module.parameters.parameter_arns["/banco-carrefour/oidc/issuer"],
        module.parameters.parameter_arns["/banco-carrefour/oidc/consolidation-audience"],
      ]
      kms_key_arns = [module.kms.key_arn]
    }
    "banco-carrefour-consolidation-worker" = {
      secret_arns  = [module.secrets.secret_arns["banco-carrefour/consolidation-worker/db-credentials"]]
      kms_key_arns = [module.kms.key_arn]
    }
  }
  tags = {
    project     = "banco-carrefour"
    environment = "localstack-hobby"
    managed_by  = "terraform"
  }
}
