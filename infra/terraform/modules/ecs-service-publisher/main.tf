# Ledger.OutboxPublisher - rolling deployment controlado (ver ADR-0014,
# seção 12.4). Sem ALB (não expõe endpoint HTTP) - portanto nenhuma
# estratégia CANARY/LINEAR/BLUE_GREEN nativa do ECS se aplica (todas
# dependem de Application Load Balancer). O gate de segurança real é
# deployment_circuit_breaker + alarms (backlog da Outbox, idade do item
# mais antigo, falhas de publicação) combinado com
# minimum_healthy_percent=100 (nunca reduz capacidade durante o rollout).
locals {
  common_tags = merge(var.tags, {
    project     = "banco-carrefour"
    environment = var.environment
    managed_by  = "terraform"
    workload    = var.service_name
  })
}

resource "aws_ecs_task_definition" "this" {
  family                   = "banco-carrefour-${var.environment}-${var.service_name}"
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

  container_definitions = jsonencode([
    {
      name      = var.service_name
      image     = var.image
      essential = true
      user      = var.non_root_user

      readonlyRootFilesystem = true
      linuxParameters = {
        capabilities = {
          drop = ["ALL"]
        }
      }

      # stopTimeout: o ECS envia SIGTERM e aguarda ate stopTimeout antes
      # de SIGKILL - a aplicacao (Ledger.OutboxPublisher) deve, ao
      # receber SIGTERM: (1) parar de reivindicar novos itens da Outbox,
      # (2) concluir ou liberar com seguranca o trabalho ja reivindicado
      # dentro dessa janela, (3) nunca manter uma transacao de claim
      # aberta durante a publicacao no SQS (ADR-0004) - este valor deve
      # ser maior que o pior caso observado desse ciclo.
      stopTimeout = var.stop_timeout_seconds

      environment = [
        for k, v in merge(var.environment_variables, {
          OTEL_EXPORTER_OTLP_ENDPOINT = var.otel_endpoint
          OTEL_EXPORTER_OTLP_PROTOCOL = "grpc"
          RELEASE_VERSION             = substr(var.image, length(var.image) - 12, 12)
        }) : { name = k, value = v }
      ]

      secrets = [
        for k, arn in var.secrets : { name = k, valueFrom = arn }
      ]

      logConfiguration = {
        logDriver = "awslogs"
        options = {
          "awslogs-group"         = var.log_group_name
          "awslogs-region"        = var.aws_region
          "awslogs-stream-prefix" = var.service_name
        }
      }
    }
  ])

  tags = local.common_tags
}

resource "aws_ecs_service" "this" {
  name            = var.service_name
  cluster         = var.cluster_arn
  task_definition = aws_ecs_task_definition.this.arn
  desired_count   = var.desired_count

  capacity_provider_strategy {
    capacity_provider = "FARGATE"
    weight            = 1
    base              = 0
  }

  network_configuration {
    subnets          = var.private_subnet_ids
    security_groups  = [var.security_group_id]
    assign_public_ip = false
  }

  deployment_configuration {
    strategy = "ROLLING"
  }

  deployment_minimum_healthy_percent = var.minimum_healthy_percent
  deployment_maximum_percent         = var.maximum_percent

  deployment_circuit_breaker {
    enable   = true
    rollback = true
  }

  alarms {
    alarm_names = var.alarm_names
    enable      = true
    rollback    = true
  }

  tags = local.common_tags

  lifecycle {
    ignore_changes = [
      desired_count,
    ]
  }
}

resource "aws_appautoscaling_target" "this" {
  max_capacity       = var.max_capacity
  min_capacity       = var.min_capacity
  resource_id        = "service/${split("/", var.cluster_arn)[1]}/${aws_ecs_service.this.name}"
  scalable_dimension = "ecs:service:DesiredCount"
  service_namespace  = "ecs"
}

resource "aws_appautoscaling_policy" "cpu" {
  name               = "${var.service_name}-cpu"
  policy_type        = "TargetTrackingScaling"
  resource_id        = aws_appautoscaling_target.this.resource_id
  scalable_dimension = aws_appautoscaling_target.this.scalable_dimension
  service_namespace  = aws_appautoscaling_target.this.service_namespace

  target_tracking_scaling_policy_configuration {
    predefined_metric_specification {
      predefined_metric_type = "ECSServiceAverageCPUUtilization"
    }
    target_value       = 65
    scale_in_cooldown  = 180
    scale_out_cooldown = 60
  }
}
