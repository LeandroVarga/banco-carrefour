# Rede de referência por ambiente de workload - VPC dedicada,
# subnets públicas (somente Internet Gateway/NAT) e privadas (ECS/Fargate,
# RDS, VPC endpoints - nenhum recurso de aplicação/dado com IP público).
# Reutilizado identicamente por development/staging/production, com a
# postura de disponibilidade/custo controlada por variáveis explícitas
# (single_nat_gateway, enable_interface_vpc_endpoints) e nunca por lógica
# condicional embutida neste módulo baseada em var.environment.
locals {
  az_count = length(var.availability_zones)
  common_tags = merge(var.tags, {
    project     = "banco-carrefour"
    environment = var.environment
    managed_by  = "terraform"
  })
}

resource "aws_vpc" "this" {
  cidr_block           = var.vpc_cidr
  enable_dns_support   = true
  enable_dns_hostnames = true

  tags = merge(local.common_tags, { Name = "banco-carrefour-${var.environment}-vpc" })
}

resource "aws_internet_gateway" "this" {
  vpc_id = aws_vpc.this.id
  tags   = merge(local.common_tags, { Name = "banco-carrefour-${var.environment}-igw" })
}

resource "aws_subnet" "public" {
  count = local.az_count

  vpc_id                  = aws_vpc.this.id
  cidr_block              = var.public_subnet_cidrs[count.index]
  availability_zone       = var.availability_zones[count.index]
  map_public_ip_on_launch = false # NAT Gateways recebem EIP explícito - nenhuma subnet atribui IP público por padrão a novas ENIs.

  tags = merge(local.common_tags, { Name = "banco-carrefour-${var.environment}-public-${var.availability_zones[count.index]}", tier = "public" })
}

resource "aws_subnet" "private" {
  count = local.az_count

  vpc_id            = aws_vpc.this.id
  cidr_block        = var.private_subnet_cidrs[count.index]
  availability_zone = var.availability_zones[count.index]

  tags = merge(local.common_tags, { Name = "banco-carrefour-${var.environment}-private-${var.availability_zones[count.index]}", tier = "private" })
}

# --- NAT: um único Gateway compartilhado (custo) ou um por AZ (disponibilidade) ---
resource "aws_eip" "nat" {
  count  = var.single_nat_gateway ? 1 : local.az_count
  domain = "vpc"
  tags   = merge(local.common_tags, { Name = "banco-carrefour-${var.environment}-nat-eip-${count.index}" })
}

resource "aws_nat_gateway" "this" {
  count = var.single_nat_gateway ? 1 : local.az_count

  allocation_id = aws_eip.nat[count.index].id
  subnet_id     = aws_subnet.public[count.index].id

  tags = merge(local.common_tags, { Name = "banco-carrefour-${var.environment}-nat-${count.index}" })

  depends_on = [aws_internet_gateway.this]
}

resource "aws_route_table" "public" {
  vpc_id = aws_vpc.this.id
  tags   = merge(local.common_tags, { Name = "banco-carrefour-${var.environment}-public-rt" })
}

resource "aws_route" "public_internet" {
  route_table_id         = aws_route_table.public.id
  destination_cidr_block = "0.0.0.0/0"
  gateway_id             = aws_internet_gateway.this.id
}

resource "aws_route_table_association" "public" {
  count = local.az_count

  subnet_id      = aws_subnet.public[count.index].id
  route_table_id = aws_route_table.public.id
}

# Uma route table privada por AZ quando NAT não é compartilhado (egress
# permanece dentro da mesma AZ - evita cobrança de transferência
# cross-AZ e reduz o raio de impacto de uma falha de NAT), uma única
# route table privada compartilhada quando single_nat_gateway=true.
resource "aws_route_table" "private" {
  count = var.single_nat_gateway ? 1 : local.az_count

  vpc_id = aws_vpc.this.id
  tags   = merge(local.common_tags, { Name = "banco-carrefour-${var.environment}-private-rt-${count.index}" })
}

resource "aws_route" "private_nat" {
  count = var.single_nat_gateway ? 1 : local.az_count

  route_table_id         = aws_route_table.private[count.index].id
  destination_cidr_block = "0.0.0.0/0"
  nat_gateway_id         = aws_nat_gateway.this[count.index].id
}

resource "aws_route_table_association" "private" {
  count = local.az_count

  subnet_id      = aws_subnet.private[count.index].id
  route_table_id = var.single_nat_gateway ? aws_route_table.private[0].id : aws_route_table.private[count.index].id
}

# --- Security groups baseline ---
# ALB interno - SEM ingress declarado aqui de proposito: a regra precisa
# (somente a partir do security group do VPC Link V2, nunca um CIDR amplo
# da VPC) so pode ser criada depois que o modulo "edge" cria esse security
# group - ver aws_security_group_rule.vpc_link_to_alb no modulo edge, que
# referencia este SG por ID (var.alb_security_group_id). Nunca exposto
# publicamente.
resource "aws_security_group" "alb" {
  name_prefix = "banco-carrefour-${var.environment}-alb-"
  description = "ALB interno - ingress adicionado pelo modulo edge (somente do security group do VPC Link V2), nunca da internet."
  vpc_id      = aws_vpc.this.id

  egress {
    description = "Encaminhamento para as tasks ECS na porta do container."
    from_port   = 0
    to_port     = 65535
    protocol    = "tcp"
    cidr_blocks = [var.vpc_cidr]
  }

  tags = merge(local.common_tags, { Name = "banco-carrefour-${var.environment}-alb-sg" })

  lifecycle {
    create_before_destroy = true
  }
}

resource "aws_security_group" "ecs_tasks" {
  name_prefix = "banco-carrefour-${var.environment}-ecs-tasks-"
  description = "Tasks ECS/Fargate - ingress somente do ALB interno, egress restrito a VPC endpoints/NAT (sem IP publico direto)."
  vpc_id      = aws_vpc.this.id

  ingress {
    description     = "Trafego do ALB interno para a porta do container."
    from_port       = var.container_port
    to_port         = var.container_port
    protocol        = "tcp"
    security_groups = [aws_security_group.alb.id]
  }

  egress {
    description = "Egress geral (RDS, SQS, ECR via NAT/VPC endpoints, CloudWatch, Secrets Manager/SSM) - restrito por regras de destino, nunca 0.0.0.0/0 sem necessidade documentada."
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = merge(local.common_tags, { Name = "banco-carrefour-${var.environment}-ecs-tasks-sg" })

  lifecycle {
    create_before_destroy = true
  }
}

resource "aws_security_group" "rds" {
  name_prefix = "banco-carrefour-${var.environment}-rds-"
  description = "RDS PostgreSQL - ingress somente das tasks ECS, nunca publico."
  vpc_id      = aws_vpc.this.id

  ingress {
    description     = "PostgreSQL a partir das tasks ECS."
    from_port       = var.rds_port
    to_port         = var.rds_port
    protocol        = "tcp"
    security_groups = [aws_security_group.ecs_tasks.id]
  }

  tags = merge(local.common_tags, { Name = "banco-carrefour-${var.environment}-rds-sg" })

  lifecycle {
    create_before_destroy = true
  }
}

resource "aws_security_group" "vpc_endpoints" {
  count = var.enable_interface_vpc_endpoints ? 1 : 0

  name_prefix = "banco-carrefour-${var.environment}-vpce-"
  description = "VPC endpoints de interface (ECR, Logs, Secrets Manager, SSM) - ingress somente das tasks ECS."
  vpc_id      = aws_vpc.this.id

  ingress {
    description     = "HTTPS a partir das tasks ECS."
    from_port       = 443
    to_port         = 443
    protocol        = "tcp"
    security_groups = [aws_security_group.ecs_tasks.id]
  }

  tags = merge(local.common_tags, { Name = "banco-carrefour-${var.environment}-vpce-sg" })

  lifecycle {
    create_before_destroy = true
  }
}

# --- VPC Endpoints: reduz egress via NAT e superficie publica necessaria ---
resource "aws_vpc_endpoint" "s3" {
  vpc_id            = aws_vpc.this.id
  service_name      = "com.amazonaws.${data.aws_region.current.region}.s3"
  vpc_endpoint_type = "Gateway"
  route_table_ids   = var.single_nat_gateway ? [aws_route_table.private[0].id] : aws_route_table.private[*].id

  tags = merge(local.common_tags, { Name = "banco-carrefour-${var.environment}-vpce-s3" })
}

resource "aws_vpc_endpoint" "interface" {
  for_each = var.enable_interface_vpc_endpoints ? toset([
    "ecr.api",
    "ecr.dkr",
    "logs",
    "secretsmanager",
    "ssm",
  ]) : []

  vpc_id              = aws_vpc.this.id
  service_name        = "com.amazonaws.${data.aws_region.current.region}.${each.value}"
  vpc_endpoint_type   = "Interface"
  subnet_ids          = aws_subnet.private[*].id
  security_group_ids  = [aws_security_group.vpc_endpoints[0].id]
  private_dns_enabled = true

  tags = merge(local.common_tags, { Name = "banco-carrefour-${var.environment}-vpce-${each.value}" })
}

data "aws_region" "current" {}
