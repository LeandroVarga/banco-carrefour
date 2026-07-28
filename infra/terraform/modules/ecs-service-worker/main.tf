# Consolidation.Worker - capacity canary (ver ADR-0014): dois
# aws_ecs_service independentes (primary + canary) consumindo a MESMA fila
# SQS sob o mesmo modelo de idempotência (ADR-0004), nunca dois consumidores
# com semânticas diferentes. O canário recebe uma fração APROXIMADA da
# carga, proporcional a canary_desired_count/(primary_desired_count+canary_desired_count) -
# nunca uma porcentagem exata como no ALB (SQS não tem conceito de
# weighted routing).
locals {
  common_tags = merge(var.tags, {
    project     = "banco-carrefour"
    environment = var.environment
    managed_by  = "terraform"
    workload    = var.service_name
  })

  # release_version: dimensão real usada nas métricas/logs para distinguir
  # primary de canary durante uma avaliação (ver módulo deployment-alarms) -
  # derivada do digest da imagem (últimos 12 caracteres), nunca de um
  # contador arbitrário.
  services = merge(
    {
      primary = {
        image         = var.primary_image
        desired_count = var.primary_desired_count
        release_label = "primary"
      }
    },
    var.canary_image != null ? {
      canary = {
        image         = var.canary_image
        desired_count = var.canary_desired_count
        release_label = "canary"
      }
    } : {}
  )
}

resource "aws_ecs_task_definition" "this" {
  for_each = local.services

  family                   = "banco-carrefour-${var.environment}-${var.service_name}-${each.key}"
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
      image     = each.value.image
      essential = true
      user      = var.non_root_user

      readonlyRootFilesystem = true
      linuxParameters = {
        capabilities = {
          drop = ["ALL"]
        }
      }

      environment = [
        for k, v in merge(var.environment_variables, {
          OTEL_EXPORTER_OTLP_ENDPOINT = var.otel_endpoint
          OTEL_EXPORTER_OTLP_PROTOCOL = "grpc"
          RELEASE_LABEL               = each.value.release_label
          RELEASE_VERSION             = substr(each.value.image, length(each.value.image) - 12, 12)
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
          "awslogs-stream-prefix" = "${var.service_name}-${each.key}"
        }
      }
    }
  ])

  tags = merge(local.common_tags, { release_label = each.value.release_label })
}

resource "aws_ecs_service" "this" {
  for_each = local.services

  name            = "${var.service_name}-${each.key}"
  cluster         = var.cluster_arn
  task_definition = aws_ecs_task_definition.this[each.key].arn
  desired_count   = each.value.desired_count

  # ROLLING e correto aqui (nunca CANARY/BLUE_GREEN): o "canary" desta
  # capacidade e o proprio par de SERVICOS (primary+canary), nao um
  # deployment_configuration dentro de um unico servico - nao ha ALB para
  # essas duas estrategias operarem.
  deployment_configuration {
    strategy = "ROLLING"
  }

  deployment_circuit_breaker {
    enable   = true
    rollback = true
  }

  # Alarmes aplicados a ambos os servicos (primary/canary) - detecta
  # regressao de qualquer um dos dois durante uma avaliacao de capacity
  # canary (profundidade de DLQ, idade da mensagem mais antiga, atraso de
  # projecao, falhas de processamento).
  alarms {
    alarm_names = var.alarm_names
    enable      = true
    rollback    = true
  }

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

  tags = merge(local.common_tags, { release_label = each.value.release_label })

  lifecycle {
    ignore_changes = [
      desired_count, # Autoscaling gerencia o "primary" apos o primeiro apply; o "canary" e sempre controlado explicitamente pelo workflow de deploy.
    ]
  }
}

resource "aws_appautoscaling_target" "primary" {
  max_capacity       = var.max_capacity
  min_capacity       = var.min_capacity
  resource_id        = "service/${split("/", var.cluster_arn)[1]}/${aws_ecs_service.this["primary"].name}"
  scalable_dimension = "ecs:service:DesiredCount"
  service_namespace  = "ecs"
}

resource "aws_appautoscaling_policy" "primary_cpu" {
  name               = "${var.service_name}-primary-cpu"
  policy_type        = "TargetTrackingScaling"
  resource_id        = aws_appautoscaling_target.primary.resource_id
  scalable_dimension = aws_appautoscaling_target.primary.scalable_dimension
  service_namespace  = aws_appautoscaling_target.primary.service_namespace

  target_tracking_scaling_policy_configuration {
    predefined_metric_specification {
      predefined_metric_type = "ECSServiceAverageCPUUtilization"
    }
    target_value       = 65
    scale_in_cooldown  = 180
    scale_out_cooldown = 60
  }
}
