output "parameter_arns" {
  value = { for name, parameter in aws_ssm_parameter.this : name => parameter.arn }
}

output "parameter_names" {
  value = { for name, parameter in aws_ssm_parameter.this : name => parameter.name }
}
