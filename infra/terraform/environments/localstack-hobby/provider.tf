provider "aws" {
  region                      = var.aws_region
  access_key                  = "test"
  secret_key                  = "test"
  skip_credentials_validation = true
  skip_metadata_api_check     = true
  skip_region_validation      = true
  skip_requesting_account_id  = true
  s3_use_path_style           = true

  endpoints {
    sqs            = var.localstack_endpoint
    secretsmanager = var.localstack_endpoint
    ssm            = var.localstack_endpoint
    kms            = var.localstack_endpoint
    iam            = var.localstack_endpoint
  }
}
