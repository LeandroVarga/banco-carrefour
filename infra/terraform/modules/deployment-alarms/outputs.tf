output "alarm_names" {
  description = "Nomes reais dos alarmes criados - consumidos por aws_ecs_service.alarms.alarm_names (rollback automático) e pelos gates de promoção dos workflows."
  value       = [for a in aws_cloudwatch_metric_alarm.this : a.alarm_name]
}

output "alarm_arns" {
  value = { for k, a in aws_cloudwatch_metric_alarm.this : k => a.arn }
}
