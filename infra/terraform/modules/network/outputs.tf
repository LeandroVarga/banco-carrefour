output "vpc_id" {
  value = aws_vpc.this.id
}

output "vpc_cidr" {
  value = aws_vpc.this.cidr_block
}

output "public_subnet_ids" {
  value = aws_subnet.public[*].id
}

output "private_subnet_ids" {
  value = aws_subnet.private[*].id
}

output "alb_security_group_id" {
  value = aws_security_group.alb.id
}

output "ecs_tasks_security_group_id" {
  value = aws_security_group.ecs_tasks.id
}

output "rds_security_group_id" {
  value = aws_security_group.rds.id
}

output "vpc_endpoints_security_group_id" {
  description = "ID do security group dos VPC endpoints de interface - null quando enable_interface_vpc_endpoints=false. Usado por consumidores externos (ex.: modulo ecs-migration-task) que precisam de uma regra de ingress simetrica adicionada de fora deste modulo, sem dependencia circular."
  value       = try(aws_security_group.vpc_endpoints[0].id, null)
}

output "nat_gateway_ids" {
  value = aws_nat_gateway.this[*].id
}
