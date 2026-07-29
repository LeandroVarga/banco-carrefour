# Task ECS/Fargate one-off de migração (ver ADR-0015) - NUNCA um aws_ecs_service (nenhum
# recurso desse tipo é declarado neste módulo). Uma instância deste módulo
# por FRONTEIRA (var.boundary) - nunca uma única task definition/role
# compartilhada entre Ledger e Consolidation. Executada exclusivamente via
# "aws ecs run-task" pelos workflows de deploy, com overrides de comando
# escolhendo apenas a fase (migrate, nunca contract - ver
# ContractCommand/MigrationRunner); a fronteira já está fixada pela própria
# task definition/family desta instância, nunca por um override de
# comando. NUNCA aparece como um 5º workload de negócio nos diagramas
# C4/deployment - é um artefato operacional, exatamente como os scripts
# de CI.
locals {
  common_tags = merge(var.tags, {
    project     = "banco-carrefour"
    environment = var.environment
    managed_by  = "terraform"
    workload    = "migration-runner"
    boundary    = var.boundary
  })
}

# Security group dedicado a ESTA fronteira - NUNCA compartilhado com o
# security group dos 4 workloads de negócio (aws_security_group.ecs_tasks
# do módulo network), nem com a outra fronteira de migração, mesmo que o
# destino de rede (RDS) seja o mesmo objeto de security group hoje neste
# ambiente (ver docs/decisions - o isolamento real e comprovável entre
# fronteiras é o de IAM/secret, seção "Ledger e Consolidation IAM
# isolation"). Sem ingress (a task nunca recebe conexões - só origina).
resource "aws_security_group" "migration_task" {
  name_prefix = "banco-carrefour-${var.environment}-migration-${var.boundary}-"
  description = "Task ECS one-off de migracao (${var.boundary}) - sem ingress (nunca recebe conexoes), egress restrito a RDS e VPC endpoints/NAT."
  vpc_id      = var.vpc_id

  egress {
    description     = "PostgreSQL para o RDS desta fronteira (${var.boundary})."
    from_port       = 5432
    to_port         = 5432
    protocol        = "tcp"
    security_groups = [var.rds_security_group_id]
  }

  egress {
    description = "HTTPS para Secrets Manager/ECR/CloudWatch Logs via VPC endpoints ou NAT Gateway."
    from_port   = 443
    to_port     = 443
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = merge(local.common_tags, { Name = "banco-carrefour-${var.environment}-migration-${var.boundary}-sg" })

  lifecycle {
    create_before_destroy = true
  }
}

# aws_ecs_task_definition.this: registra apenas a revisao de BOOTSTRAP
# (var.image, geralmente o digest aprovado mais recente conhecido no
# momento do "terraform apply" de infraestrutura estavel). A cada release,
# o workflow de deploy registra uma NOVA revisao desta MESMA family
# diretamente via API ECS (scripts/ci/run-migration-task.sh,
# "aws ecs register-task-definition") - nunca via "terraform apply
# -target" (ver ADR-0015). "ignore_changes" em container_definitions garante que
# um "terraform apply" de rotina (que atualiza os 4 servicos de aplicacao)
# nunca reverte/sobrescreve a revisao mais recente registrada pelo
# workflow - Terraform so gerencia a EXISTENCIA da family e dos recursos
# estaveis (roles, security group, log group), nunca a imagem por release.
resource "aws_ecs_task_definition" "this" {
  family                   = "banco-carrefour-${var.environment}-migration-${var.boundary}"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = var.cpu
  memory                   = var.memory
  execution_role_arn       = var.task_execution_role_arn
  task_role_arn            = var.task_role_arn

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64"
  }

  ephemeral_storage {
    size_in_gib = var.ephemeral_storage_gib
  }

  container_definitions = jsonencode([
    {
      name      = "migration-runner"
      image     = var.image
      essential = true
      user      = var.non_root_user

      readonlyRootFilesystem = true
      linuxParameters = {
        capabilities = {
          drop = ["ALL"]
        }
      }

      # Sem "command" fixo aqui de propósito: o comando real
      # (["migrate"|"contract", ...]) é sempre informado via
      # containerOverrides no "aws ecs run-task" - esta task definition
      # nunca é executada sem overrides explícitos. A fronteira (Ledger ou
      # Consolidation) já está fixada por esta family/instância do módulo,
      # nunca por um override.
      environment = [
        for k, v in var.environment_variables : { name = k, value = v }
      ]

      secrets = [
        for k, arn in var.secrets : { name = k, valueFrom = arn }
      ]

      logConfiguration = {
        logDriver = "awslogs"
        options = {
          "awslogs-group"         = var.log_group_name
          "awslogs-region"        = var.aws_region
          "awslogs-stream-prefix" = "migration-${var.boundary}"
        }
      }
    }
  ])

  tags = local.common_tags

  lifecycle {
    ignore_changes = [container_definitions]
  }
}
