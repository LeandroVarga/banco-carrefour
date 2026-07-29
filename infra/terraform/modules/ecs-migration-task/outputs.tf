output "task_definition_arn" {
  description = "ARN da revisao de BOOTSTRAP registrada pelo Terraform (var.image) - o workflow de deploy registra uma revisao NOVA desta MESMA family a cada release via API ECS, nunca via terraform apply -target (ver comentario em main.tf)."
  value       = aws_ecs_task_definition.this.arn
}

output "task_definition_family" {
  value = aws_ecs_task_definition.this.family
}

output "security_group_id" {
  value = aws_security_group.migration_task.id
}

# Passthrough dos parametros usados para renderizar a task definition -
# consumidos por scripts/ci/run-migration-task.sh para registrar uma NOVA
# revisao por release (mesmos valores que o Terraform usou para a
# revisao de bootstrap, nunca divergentes) via "aws ecs
# register-task-definition", substituindo apenas a imagem pelo digest
# real da release (
# Closure, secao 5).
output "task_execution_role_arn" {
  value = var.task_execution_role_arn
}

output "task_role_arn" {
  value = var.task_role_arn
}

output "environment_variables" {
  value = var.environment_variables
}

output "secrets" {
  value     = var.secrets
  sensitive = true
}

output "log_group_name" {
  value = var.log_group_name
}

output "cpu" {
  value = var.cpu
}

output "memory" {
  value = var.memory
}

output "ephemeral_storage_gib" {
  value = var.ephemeral_storage_gib
}

output "non_root_user" {
  value = var.non_root_user
}

output "migration_runner_failures_alarm_name" {
  description = "Alarme sobre o metric filter de falhas reportadas via StructuredLog (comando 'migrate') - cada instanciacao deste modulo declara exatamente 1 alarme, daí o indice fixo [0]."
  value       = module.migration_runner_failures_alarm.alarm_names[0]
}

output "task_failed_to_start_alarm_name" {
  description = "Alarme sobre o evento nativo ECS Task State Change (stopCode=TaskFailedToStart) desta task."
  value       = module.task_failed_to_start_alarm.alarm_names[0]
}

output "task_failed_to_start_event_rule_name" {
  value = aws_cloudwatch_event_rule.task_failed_to_start.name
}

output "task_lifecycle_log_group_name" {
  description = "Log group dedicado que recebe o evento bruto ECS Task State Change (nunca os logs de aplicacao do MigrationRunner, que ficam em var.log_group_name)."
  value       = aws_cloudwatch_log_group.task_lifecycle_events.name
}
