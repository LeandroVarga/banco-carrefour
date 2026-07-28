variable "environment" {
  description = "Nome do ambiente (development|staging|production) - usado em nomes/tags de recursos, nunca em lógica condicional de segurança (a diferença real de postura vem de outras variáveis explícitas, como single_nat_gateway/enable_vpc_endpoints)."
  type        = string

  validation {
    condition     = contains(["development", "staging", "production"], var.environment)
    error_message = "environment deve ser exatamente 'development', 'staging' ou 'production'."
  }
}

variable "vpc_cidr" {
  description = "Bloco CIDR da VPC deste ambiente - nunca sobreposto ao de outro ambiente/conta quando peering ou Transit Gateway forem introduzidos no futuro."
  type        = string
}

variable "availability_zones" {
  description = "Lista de Availability Zones a usar (mínimo 2, para ALB/RDS Multi-AZ e ECS distribuído). Fornecida explicitamente (nunca 'data aws_availability_zones' com slice implícito) para que o plano seja determinístico e revisável."
  type        = list(string)

  validation {
    condition     = length(var.availability_zones) >= 2
    error_message = "Forneça pelo menos 2 Availability Zones."
  }
}

variable "public_subnet_cidrs" {
  description = "CIDRs das subnets públicas, um por AZ (mesma ordem de availability_zones) - usadas apenas pelo Internet Gateway/NAT Gateways, nunca por ECS/RDS."
  type        = list(string)
}

variable "private_subnet_cidrs" {
  description = "CIDRs das subnets privadas, um por AZ (mesma ordem de availability_zones) - onde ECS/Fargate, RDS e os VPC endpoints residem. Nenhum recurso de aplicação/dado tem IP público."
  type        = list(string)
}

variable "single_nat_gateway" {
  description = "Se true, cria um único NAT Gateway compartilhado entre todas as AZs privadas (custo menor, ponto único de falha de egress aceitável para Development). Se false, cria um NAT Gateway por AZ (alta disponibilidade de egress - recomendado para Staging/Production)."
  type        = bool
  default     = true
}

variable "enable_interface_vpc_endpoints" {
  description = "Se true, cria VPC endpoints de interface para ECR (api+dkr), CloudWatch Logs, Secrets Manager e SSM - reduz egress via NAT e a superfície pública necessária para pull de imagem/acesso a secrets. Tem custo por hora e por GB processado; avaliar trade-off de custo por ambiente (ver docs/decisions sobre custo)."
  type        = bool
  default     = true
}

variable "container_port" {
  description = "Porta de container exposta pelos serviços ECS atrás do ALB interno - usada para a regra de security group do ALB para as tasks."
  type        = number
  default     = 8080
}

variable "rds_port" {
  description = "Porta do PostgreSQL usada pela regra de security group das tasks ECS para o RDS."
  type        = number
  default     = 5432
}

variable "tags" {
  type    = map(string)
  default = {}
}
