# Workload ECS/Fargate com canary de trafego NATIVO do ECS (ver ADR-0014 -
# deployment_configuration.strategy=CANARY, confirmado via documentacao
# oficial e fonte real do provider AWS antes de implementar, nunca
# assumido). Reutilizado identicamente por Ledger.Api e Consolidation.Api -
# a diferenca entre os dois workloads e inteiramente parametrica
# (variables), nunca logica condicional dentro deste modulo.
locals {
  common_tags = merge(var.tags, {
    project     = "banco-carrefour"
    environment = var.environment
    managed_by  = "terraform"
    workload    = var.service_name
  })
}

resource "aws_lb_target_group" "this" {
  name        = "bc-${var.environment}-${var.service_name}"
  port        = var.container_port
  protocol    = "HTTP"
  vpc_id      = var.vpc_id
  target_type = "ip"

  health_check {
    path                = var.health_check_path
    matcher             = "200"
    healthy_threshold   = 3
    unhealthy_threshold = 3
    interval            = 15
    timeout             = 5
  }

  # Desregistro gradual para nao derrubar requisicoes em voo durante o
  # canary/blue-green (o ECS gerencia o ciclo de vida do target group
  # durante o deployment nativo, mas o deregistration_delay ainda protege
  # encerramentos normais de task/scale-in).
  deregistration_delay = 30

  tags = local.common_tags

  lifecycle {
    create_before_destroy = true
  }
}

resource "aws_lb_listener_rule" "this" {
  listener_arn = var.alb_https_listener_arn
  priority     = var.listener_rule_priority

  action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.this.arn
  }

  condition {
    path_pattern {
      values = [var.path_pattern]
    }
  }

  tags = local.common_tags
}

# Alarmes de ALB (5xx, latencia p99) SEMPRE criados aqui dentro (nunca
# recebidos como input externo): dependem do arn_suffix do target group
# que este MESMO modulo acabou de criar - trazer isso de fora criaria uma
# dependencia circular (o alarme precisaria do target group, o servico
# precisaria do alarme). Alarmes adicionais (ex.: de negocio, emitidos
# pela propria aplicacao) podem ser somados via var.extra_alarm_names.
module "alb_alarms" {
  source = "../deployment-alarms"

  environment = var.environment
  name_prefix = "banco-carrefour-${var.environment}-${var.service_name}"

  alarms = {
    http-5xx = {
      namespace           = "AWS/ApplicationELB"
      metric_name         = "HTTPCode_Target_5XX_Count"
      statistic           = "Sum"
      period_seconds      = 60
      evaluation_periods  = 3
      threshold           = var.http_5xx_threshold
      comparison_operator = "GreaterThanThreshold"
      dimensions          = { TargetGroup = aws_lb_target_group.this.arn_suffix }
      description         = "Taxa de erro 5xx do target group de ${var.service_name} acima do aceitavel durante o deployment."
    }
    p99-latency = {
      namespace           = "AWS/ApplicationELB"
      metric_name         = "TargetResponseTime"
      statistic           = "p99"
      period_seconds      = 60
      evaluation_periods  = 3
      threshold           = var.p99_latency_threshold_seconds
      comparison_operator = "GreaterThanThreshold"
      dimensions          = { TargetGroup = aws_lb_target_group.this.arn_suffix }
      description         = "Latencia p99 do target group de ${var.service_name} acima do aceitavel durante o deployment."
    }
  }

  tags = local.common_tags
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

      # Nunca roda como root - a imagem ja define USER nao-root
      # (org.opencontainers.image.*, ver scripts/ci/build-images-for-supply-chain.sh);
      # "user" aqui e um cinto-e-suspensorio explicito contra qualquer
      # regressao futura do Dockerfile.
      user = var.non_root_user

      readonlyRootFilesystem = true
      linuxParameters = {
        capabilities = {
          drop = ["ALL"]
        }
      }

      portMappings = [
        {
          containerPort = var.container_port
          protocol      = "tcp"
        }
      ]

      environment = [
        for k, v in merge(var.environment_variables, {
          OTEL_EXPORTER_OTLP_ENDPOINT = var.otel_endpoint
          OTEL_EXPORTER_OTLP_PROTOCOL = "grpc"
          # Dimensao de release/task-definition para metricas de negocio
          # customizadas emitidas pela propria aplicacao (ver dashboard de
          # deployment, ADR-0014) - derivada do digest da imagem (unico
          # identificador real e verificavel da revisao em execucao,
          # nunca um contador arbitrario).
          RELEASE_VERSION = substr(var.image, length(var.image) - 12, 12)
        }) : { name = k, value = v }
      ]

      secrets = [
        for k, arn in var.secrets : { name = k, valueFrom = arn }
      ]

      healthCheck = {
        command     = ["CMD-SHELL", "curl -f http://localhost:${var.container_port}${var.health_check_path} || exit 1"]
        interval    = 15
        timeout     = 5
        retries     = 3
        startPeriod = 30
      }

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

  load_balancer {
    target_group_arn = aws_lb_target_group.this.arn
    container_name   = var.service_name
    container_port   = var.container_port
  }

  health_check_grace_period_seconds = 60

  # Estrategia de deployment nativa do ECS (ver ADR-0014): CANARY desloca
  # uma fracao pequena do trafego primeiro, observa por
  # canary_bake_time_minutes, so entao completa para 100% e observa por
  # bake_time_minutes antes de encerrar a revisao anterior. Nunca chamamos
  # isso de "rolling" - a estrategia ROLLING (padrao historico do ECS) e
  # deliberadamente NAO usada para Ledger.Api/Consolidation.Api.
  deployment_configuration {
    strategy             = "CANARY"
    bake_time_in_minutes = var.bake_time_minutes

    canary_configuration {
      canary_percent              = var.canary_percent
      canary_bake_time_in_minutes = var.canary_bake_time_minutes
    }
  }

  # Rollback automatico real: se qualquer alarme desta lista disparar
  # durante o bake time (canario ou pos-producao), o ECS reverte para a
  # revisao anterior sem intervencao manual.
  alarms {
    alarm_names = concat(module.alb_alarms.alarm_names, var.extra_alarm_names)
    enable      = true
    rollback    = true
  }

  tags = local.common_tags

  lifecycle {
    ignore_changes = [
      desired_count, # Autoscaling (Application Auto Scaling) e o dono real deste campo apos o primeiro apply.
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
    target_value       = 60
    scale_in_cooldown  = 120
    scale_out_cooldown = 60
  }
}

# Autoscaling por ALBRequestCountPerTarget (guardrail de latencia/carga
# por requisicao, alem de CPU) e um candidato natural aqui, mas exige o
# "resource_label" no formato exato "app/<lb-name>/<lb-id>/targetgroup/<tg-name>/<tg-id>",
# derivado do arn_suffix do PROPRIO ALB - este modulo so recebe o ARN do
# listener HTTPS (nao o recurso aws_lb inteiro), entao construir esse
# label aqui exigiria parsing de string fragil (nunca aceitavel). Deixado
# como extensao futura: passar var.alb_arn_suffix explicito a partir do
# modulo edge quando essa politica for realmente necessaria.
