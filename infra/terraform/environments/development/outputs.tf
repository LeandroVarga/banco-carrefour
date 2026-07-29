output "vpc_id" {
  value = module.network.vpc_id
}

output "ecs_cluster_name" {
  value = module.ecs_cluster.cluster_name
}

output "api_gateway_invoke_url" {
  value = module.edge.api_gateway_invoke_url
}

output "ledger_db_endpoint" {
  value = module.rds_ledger.endpoint
}

output "consolidation_db_endpoint" {
  value = module.rds_consolidation.endpoint
}

output "queue_url" {
  value = module.messaging.queue_url
}

output "deploy_role_arn" {
  description = "Role assumida pelo workflow de deploy a Development via OIDC (ver .github/workflows/deploy-development.yml)."
  value       = aws_iam_role.deploy.arn
}

# Uma família/task definition/security group DEDICADOS por fronteira -
# nunca uma
# única task definition compartilhada. "migration_task_render_params_*" é
# o contrato consumido por scripts/ci/run-migration-task.sh para registrar
# uma NOVA revisão a cada release via API ECS (nunca via terraform apply
# -target, ver ADR-0015): os mesmos parâmetros que o Terraform usou na
# revisão de bootstrap, com apenas a imagem substituída pelo digest real
# da release.
output "migration_task_definition_family_ledger" {
  value = module.ecs_migration_task_ledger.task_definition_family
}

output "migration_task_definition_family_consolidation" {
  value = module.ecs_migration_task_consolidation.task_definition_family
}

output "migration_task_security_group_id_ledger" {
  value = module.ecs_migration_task_ledger.security_group_id
}

output "migration_task_security_group_id_consolidation" {
  value = module.ecs_migration_task_consolidation.security_group_id
}

output "migration_task_render_params_ledger" {
  description = "Parâmetros para 'aws ecs register-task-definition' (Ledger) - JSON consumido por scripts/ci/run-migration-task.sh."
  sensitive   = true
  value = jsonencode({
    family               = module.ecs_migration_task_ledger.task_definition_family
    taskExecutionRoleArn = module.ecs_migration_task_ledger.task_execution_role_arn
    taskRoleArn          = module.ecs_migration_task_ledger.task_role_arn
    environmentVariables = module.ecs_migration_task_ledger.environment_variables
    secrets              = module.ecs_migration_task_ledger.secrets
    logGroupName         = module.ecs_migration_task_ledger.log_group_name
    awsRegion            = var.aws_region
    cpu                  = module.ecs_migration_task_ledger.cpu
    memory               = module.ecs_migration_task_ledger.memory
    ephemeralStorageGib  = module.ecs_migration_task_ledger.ephemeral_storage_gib
    nonRootUser          = module.ecs_migration_task_ledger.non_root_user
  })
}

output "migration_task_render_params_consolidation" {
  description = "Parâmetros para 'aws ecs register-task-definition' (Consolidation) - JSON consumido por scripts/ci/run-migration-task.sh."
  sensitive   = true
  value = jsonencode({
    family               = module.ecs_migration_task_consolidation.task_definition_family
    taskExecutionRoleArn = module.ecs_migration_task_consolidation.task_execution_role_arn
    taskRoleArn          = module.ecs_migration_task_consolidation.task_role_arn
    environmentVariables = module.ecs_migration_task_consolidation.environment_variables
    secrets              = module.ecs_migration_task_consolidation.secrets
    logGroupName         = module.ecs_migration_task_consolidation.log_group_name
    awsRegion            = var.aws_region
    cpu                  = module.ecs_migration_task_consolidation.cpu
    memory               = module.ecs_migration_task_consolidation.memory
    ephemeralStorageGib  = module.ecs_migration_task_consolidation.ephemeral_storage_gib
    nonRootUser          = module.ecs_migration_task_consolidation.non_root_user
  })
}

output "private_subnet_ids" {
  value = module.network.private_subnet_ids
}
