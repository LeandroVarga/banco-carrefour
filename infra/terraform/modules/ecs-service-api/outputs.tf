output "service_name" {
  value = aws_ecs_service.this.name
}

output "task_definition_arn" {
  value = aws_ecs_task_definition.this.arn
}

output "target_group_arn" {
  value = aws_lb_target_group.this.arn
}

output "target_group_arn_suffix" {
  description = "Formato app/<lb>/<lb-id>/targetgroup/<tg>/<tg-id> - usado como dimensão TargetGroup em widgets de dashboard/alarme de ALB."
  value       = aws_lb_target_group.this.arn_suffix
}

output "alarm_arns" {
  description = "ARNs reais dos alarmes de ALB (5xx, p99) criados internamente por este módulo (module.alb_alarms) - usados por widgets de dashboard type=alarm no ambiente chamador."
  value       = module.alb_alarms.alarm_arns
}
