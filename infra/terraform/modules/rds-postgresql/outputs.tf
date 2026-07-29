output "endpoint" {
  value = aws_db_instance.this.endpoint
}

output "address" {
  value = aws_db_instance.this.address
}

output "port" {
  value = aws_db_instance.this.port
}

output "instance_id" {
  value = aws_db_instance.this.id
}

output "instance_arn" {
  value = aws_db_instance.this.arn
}

output "master_user_secret_arn" {
  description = "ARN do secret gerenciado pela RDS (manage_master_user_password) - null quando um secret externo foi fornecido via master_password_secret_arn."
  value       = try(aws_db_instance.this.master_user_secret[0].secret_arn, null)
}
