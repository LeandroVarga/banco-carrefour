output "log_group_names" {
  value = { for k, lg in aws_cloudwatch_log_group.this : k => lg.name }
}

output "log_group_arns" {
  value = { for k, lg in aws_cloudwatch_log_group.this : k => lg.arn }
}
