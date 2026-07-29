output "primary_service_name" {
  value = aws_ecs_service.this["primary"].name
}

output "canary_service_name" {
  value = try(aws_ecs_service.this["canary"].name, null)
}

output "task_definition_arns" {
  value = { for k, td in aws_ecs_task_definition.this : k => td.arn }
}
