# Ambiente de workload Production (ver ADR-0011). Consome
# os MESMOS 4 digests ECR já promovidos por Staging (nunca reconstrói,
# nunca copia imagem para outro registry) - conta AWS distinta da conta
# Artifacts (infra/terraform/environments/aws-reference), consistente com o
# modelo multi-conta documentado em deploy/ecs/README.md. NUNCA aplicado
# nesta sessão - IaC-materializado mas não provisionado.
locals {
  environment = "production"

  common_tags = {
    project     = "banco-carrefour"
    environment = local.environment
    managed_by  = "terraform"
  }

  ecr_repository_arns = {
    ledger-api              = "arn:aws:ecr:${var.ecr_region}:${var.artifacts_account_id}:repository/banco-carrefour/ledger-api"
    ledger-outbox-publisher = "arn:aws:ecr:${var.ecr_region}:${var.artifacts_account_id}:repository/banco-carrefour/ledger-outbox-publisher"
    consolidation-api       = "arn:aws:ecr:${var.ecr_region}:${var.artifacts_account_id}:repository/banco-carrefour/consolidation-api"
    consolidation-worker    = "arn:aws:ecr:${var.ecr_region}:${var.artifacts_account_id}:repository/banco-carrefour/consolidation-worker"
    migration-runner        = "arn:aws:ecr:${var.ecr_region}:${var.artifacts_account_id}:repository/banco-carrefour/migration-runner"
  }

  otel_endpoint = "http://aspire-dashboard.banco-carrefour.internal:18889" # Placeholder de referência - o coletor ADOT/OTel real deste ambiente é definido pela conta Security/Observability compartilhada (fora do escopo deste ambiente de workload).
}

# --- Rede ---
module "network" {
  source = "../../modules/network"

  environment                    = local.environment
  vpc_cidr                       = var.vpc_cidr
  availability_zones             = var.availability_zones
  public_subnet_cidrs            = var.public_subnet_cidrs
  private_subnet_cidrs           = var.private_subnet_cidrs
  single_nat_gateway             = var.single_nat_gateway
  enable_interface_vpc_endpoints = true

  tags = local.common_tags
}

# --- Cluster ECS ---
module "ecs_cluster" {
  source = "../../modules/ecs-cluster"

  environment               = local.environment
  enable_container_insights = true

  tags = local.common_tags
}

# --- Borda: ALB interno + VPC Link V2 (sem NLB) + API Gateway REST + WAF ---
module "edge" {
  source = "../../modules/edge"

  environment             = local.environment
  vpc_id                  = module.network.vpc_id
  private_subnet_ids      = module.network.private_subnet_ids
  alb_security_group_id   = module.network.alb_security_group_id
  certificate_arn         = var.certificate_arn
  waf_rate_limit_per_5min = 2000
  log_retention_days      = var.log_retention_days

  tags = local.common_tags
}

# --- Mensageria (responsabilidade "sqs" - reaproveita o módulo messaging já validado) ---
module "messaging" {
  source = "../../modules/messaging"

  queue_name = "banco-carrefour-${local.environment}-financial-entry-registered"
  dlq_name   = "banco-carrefour-${local.environment}-financial-entry-registered-dlq"

  max_receive_count          = 5
  visibility_timeout_seconds = 30
  receive_wait_time_seconds  = 10

  tags = local.common_tags
}

# --- Persistência: RDS PostgreSQL independentes (Ledger, Consolidation) ---
module "rds_ledger" {
  source = "../../modules/rds-postgresql"

  identifier             = "banco-carrefour-${local.environment}-ledger"
  instance_class         = var.rds_ledger_instance_class
  database_name          = "ledger"
  multi_az               = var.rds_multi_az
  deletion_protection    = var.rds_deletion_protection
  skip_final_snapshot    = false
  backup_retention_days  = var.rds_backup_retention_days
  vpc_security_group_ids = [module.network.rds_security_group_id]
  subnet_ids             = module.network.private_subnet_ids
  apply_immediately      = false

  tags = merge(local.common_tags, { boundary = "ledger" })
}

module "rds_consolidation" {
  source = "../../modules/rds-postgresql"

  identifier             = "banco-carrefour-${local.environment}-consolidation"
  instance_class         = var.rds_consolidation_instance_class
  database_name          = "consolidation"
  multi_az               = var.rds_multi_az
  deletion_protection    = var.rds_deletion_protection
  skip_final_snapshot    = false
  backup_retention_days  = var.rds_backup_retention_days
  vpc_security_group_ids = [module.network.rds_security_group_id]
  subnet_ids             = module.network.private_subnet_ids
  apply_immediately      = false

  tags = merge(local.common_tags, { boundary = "consolidation" })
}

# --- Secrets (somente metadados - valores gravados fora do Terraform, ADR-0009) ---
module "secrets" {
  source = "../../modules/secrets"

  secrets = {
    "banco-carrefour/${local.environment}/ledger-api/db-credentials"              = { description = "Credenciais de banco do Ledger.Api (${local.environment})." }
    "banco-carrefour/${local.environment}/ledger-outbox-publisher/db-credentials" = { description = "Credenciais de banco do Ledger.OutboxPublisher (${local.environment})." }
    "banco-carrefour/${local.environment}/consolidation-api/db-credentials"       = { description = "Credenciais de banco do Consolidation.Api (${local.environment}, somente leitura)." }
    "banco-carrefour/${local.environment}/consolidation-worker/db-credentials"    = { description = "Credenciais de banco do Consolidation.Worker (${local.environment})." }
    # Credenciais dedicadas de MIGRAÇÃO (papel de banco com privilégio de
    # DDL, distinto do papel runtime das APIs/Worker/Publisher - mesma
    # convenção já usada localmente via docker-compose.yml, usuários
    # "ledger_migration"/"consolidation_migration") - usadas exclusivamente
    # pela task ECS one-off do MigrationRunner, nunca pelos 4 workloads de negócio.
    "banco-carrefour/${local.environment}/ledger-migration/db-credentials"        = { description = "Credenciais de banco (papel de migração/DDL) do MigrationRunner - fronteira Ledger (${local.environment})." }
    "banco-carrefour/${local.environment}/consolidation-migration/db-credentials" = { description = "Credenciais de banco (papel de migração/DDL) do MigrationRunner - fronteira Consolidation (${local.environment})." }
  }

  tags = local.common_tags
}

# --- Observabilidade ---
module "observability" {
  source = "../../modules/observability"

  environment = local.environment

  log_groups = {
    "ledger-api"              = var.log_retention_days
    "ledger-outbox-publisher" = var.log_retention_days
    "consolidation-api"       = var.log_retention_days
    "consolidation-worker"    = var.log_retention_days
    "migration-runner"        = var.log_retention_days
  }

  tags = local.common_tags
}

# --- Dashboard operacional (ver ADR-0012 e docs/architecture/06-diagramas.md) ---
# Corpo real do dashboard CloudWatch - widgets concretos por seção
# (Ledger, Consolidation, APIs, Deployment), referenciando SOMENTE métricas
# que a instrumentação/serviço AWS proposto pode realmente produzir:
#   - AWS/ApplicationELB, AWS/ECS, ECS/ContainerInsights, AWS/SQS: métricas
#     nativas da AWS, confirmadas via AWS Documentation oficial
#     (Container-Insights-metrics-ECS.html, service_utilization.html).
#   - BancoCarrefour/Ledger e BancoCarrefour/Consolidation: métricas de
#     negócio customizadas emitidas via OpenTelemetry/Meter (ver
#     src/*/Observability.cs e docs/operations/observabilidade-sli-slo-e-recuperacao.md)
#     - namespace/nome exatos alinhados ao código real, nunca inventados.
# Métricas marcadas "NAO EMITIDA AINDA" são um CONTRATO declarado (namespace,
# nome, dimensões, componente emissor, unidade esperada) para um slot de
# widget que só terá dados quando a aplicação for instrumentada para
# emiti-las - a ausência de dado real é classificada como não executado,
# nunca ocultada ou fabricada (ver-Push Technical
# Closure, seção 7).
locals {
  dashboard_body_json = jsonencode({
    widgets = [
      # --- Ledger (Ledger.Api + Ledger.OutboxPublisher) ---
      {
        type       = "text", x = 0, y = 0, width = 24, height = 1
        properties = { markdown = "## Ledger.Api / Ledger.OutboxPublisher" }
      },
      {
        type = "metric", x = 0, y = 1, width = 8, height = 6
        properties = {
          title  = "Lançamentos aceitos vs. replays"
          view   = "timeSeries"
          region = var.aws_region
          period = 60
          stat   = "Sum"
          metrics = [
            ["BancoCarrefour.Ledger.Api", "ledger.entries.created", { label = "Aceitos" }],
            ["BancoCarrefour.Ledger.Api", "ledger.entries.replayed", { label = "Replays de idempotência" }],
          ]
        }
      },
      {
        type = "metric", x = 8, y = 1, width = 8, height = 6
        properties = {
          title  = "Lançamentos rejeitados"
          view   = "timeSeries"
          region = var.aws_region
          period = 60
          stat   = "Sum"
          metrics = [
            ["BancoCarrefour.Ledger.Api", "ledger.entries.validation_failed", { label = "Validação falhou" }],
            ["BancoCarrefour.Ledger.Api", "ledger.entries.idempotency_conflicts", { label = "Conflito de idempotência" }],
          ]
        }
      },
      {
        type = "metric", x = 16, y = 1, width = 8, height = 6
        properties = {
          title  = "Falhas de persistência (Ledger.Api)"
          view   = "timeSeries"
          region = var.aws_region
          period = 60
          stat   = "Sum"
          metrics = [
            ["BancoCarrefour.Ledger.Api", "ledger.entries.database_unavailable", { label = "Banco indisponível" }],
          ]
        }
      },
      {
        type = "metric", x = 0, y = 7, width = 8, height = 6
        properties = {
          title  = "Publicação da Outbox - tentativas/sucesso/falha"
          view   = "timeSeries"
          region = var.aws_region
          period = 60
          stat   = "Sum"
          metrics = [
            ["BancoCarrefour.Ledger.OutboxPublisher", "ledger.outbox.publish.attempts", { label = "Tentativas" }],
            ["BancoCarrefour.Ledger.OutboxPublisher", "ledger.outbox.messages.published", { label = "Publicados" }],
            ["BancoCarrefour.Ledger.OutboxPublisher", "ledger.outbox.messages.failed", { label = "Falhas" }],
          ]
        }
      },
      {
        type = "metric", x = 8, y = 7, width = 8, height = 6
        properties = {
          title  = "Atraso de publicação (ledger.outbox.publish.duration)"
          view   = "timeSeries"
          region = var.aws_region
          period = 60
          metrics = [
            ["BancoCarrefour.Ledger.OutboxPublisher", "ledger.outbox.publish.duration", { stat = "Average", label = "Média (ms)" }],
            ["BancoCarrefour.Ledger.OutboxPublisher", "ledger.outbox.publish.duration", { stat = "p99", label = "p99 (ms)" }],
          ]
        }
      },
      {
        # NAO EMITIDA AINDA: contrato declarado, nunca implementado no
        # código (Ledger.OutboxPublisher/Observability.cs não expõe uma
        # gauge de backlog). Componente emissor esperado:
        # Ledger.OutboxPublisher, a cada ciclo de PublishPendingEventsUseCase.
        # Unidade esperada: Count.
        type = "metric", x = 16, y = 7, width = 8, height = 6
        properties = {
          title  = "Outbox backlog (NAO EMITIDA AINDA - contrato de métrica)"
          view   = "timeSeries"
          region = var.aws_region
          period = 60
          stat   = "Maximum"
          metrics = [
            ["BancoCarrefour.Ledger.OutboxPublisher", "outbox_pending_events_total", { label = "Itens pendentes (contrato)" }],
          ]
        }
      },
      {
        # NAO EMITIDA AINDA: contrato declarado (idem acima). Unidade
        # esperada: Seconds.
        type = "metric", x = 0, y = 13, width = 12, height = 6
        properties = {
          title  = "Idade do item pendente mais antigo (NAO EMITIDA AINDA - contrato de métrica)"
          view   = "timeSeries"
          region = var.aws_region
          period = 60
          stat   = "Maximum"
          metrics = [
            ["BancoCarrefour.Ledger.OutboxPublisher", "outbox_oldest_pending_event_age_seconds", { label = "Idade (s) - contrato" }],
          ]
        }
      },

      # --- Consolidation (Consolidation.Api + Consolidation.Worker) ---
      {
        type       = "text", x = 0, y = 19, width = 24, height = 1
        properties = { markdown = "## Consolidation.Api / Consolidation.Worker" }
      },
      {
        type = "metric", x = 0, y = 20, width = 8, height = 6
        properties = {
          title  = "Eventos processados"
          view   = "timeSeries"
          region = var.aws_region
          period = 60
          stat   = "Sum"
          metrics = [
            ["BancoCarrefour.Consolidation.Worker", "consolidation.events.processed", { label = "Processados" }],
          ]
        }
      },
      {
        type = "metric", x = 8, y = 20, width = 8, height = 6
        properties = {
          title  = "Eventos duplicados"
          view   = "timeSeries"
          region = var.aws_region
          period = 60
          stat   = "Sum"
          metrics = [
            ["BancoCarrefour.Consolidation.Worker", "sqs_duplicate_events_total", { label = "Duplicados descartados" }],
          ]
        }
      },
      {
        type = "metric", x = 16, y = 20, width = 8, height = 6
        properties = {
          title  = "Falhas de processamento"
          view   = "timeSeries"
          region = var.aws_region
          period = 60
          stat   = "Sum"
          metrics = [
            ["BancoCarrefour.Consolidation.Worker", "sqs_processing_failed_total", { label = "Falhas" }],
          ]
        }
      },
      {
        # NAO EMITIDA AINDA: contrato declarado (idem Outbox). Componente
        # emissor esperado: Consolidation.Worker, calculado como
        # now - evento.timestamp no momento do upsert de DailyBalance.
        # Unidade esperada: Seconds.
        type = "metric", x = 0, y = 26, width = 8, height = 6
        properties = {
          title  = "Atraso de projeção (NAO EMITIDA AINDA - contrato de métrica)"
          view   = "timeSeries"
          region = var.aws_region
          period = 60
          stat   = "Average"
          metrics = [
            ["BancoCarrefour.Consolidation.Worker", "consolidation_lag_seconds", { label = "Lag (s) - contrato" }],
          ]
        }
      },
      {
        type = "metric", x = 8, y = 26, width = 8, height = 6
        properties = {
          title  = "Profundidade da DLQ (AWS/SQS real)"
          view   = "timeSeries"
          region = var.aws_region
          period = 60
          stat   = "Maximum"
          metrics = [
            ["AWS/SQS", "ApproximateNumberOfMessagesVisible", "QueueName", "banco-carrefour-${local.environment}-financial-entry-registered-dlq"],
          ]
        }
      },
      {
        type = "metric", x = 16, y = 26, width = 8, height = 6
        properties = {
          title  = "Idade do item mais antigo na fila principal (AWS/SQS real)"
          view   = "timeSeries"
          region = var.aws_region
          period = 60
          stat   = "Maximum"
          metrics = [
            ["AWS/SQS", "ApproximateAgeOfOldestMessage", "QueueName", "banco-carrefour-${local.environment}-financial-entry-registered"],
          ]
        }
      },
      {
        type = "metric", x = 0, y = 32, width = 12, height = 6
        properties = {
          title  = "Latência de consulta do consolidado (consolidation.daily_balance.query.duration)"
          view   = "timeSeries"
          region = var.aws_region
          period = 60
          metrics = [
            ["BancoCarrefour.Consolidation.Api", "consolidation.daily_balance.query.duration", { stat = "Average", label = "Média (ms)" }],
            ["BancoCarrefour.Consolidation.Api", "consolidation.daily_balance.query.duration", { stat = "p99", label = "p99 (ms)" }],
          ]
        }
      },

      # --- APIs (ALB + ECS - Ledger.Api e Consolidation.Api) ---
      {
        type       = "text", x = 0, y = 38, width = 24, height = 1
        properties = { markdown = "## APIs (ALB + ECS) - Ledger.Api / Consolidation.Api" }
      },
      {
        type = "metric", x = 0, y = 39, width = 12, height = 6
        properties = {
          title  = "Requisições e códigos de resposta - Ledger.Api"
          view   = "timeSeries"
          region = var.aws_region
          period = 60
          stat   = "Sum"
          metrics = [
            ["AWS/ApplicationELB", "RequestCountPerTarget", "TargetGroup", module.ledger_api.target_group_arn_suffix, { label = "Requisições" }],
            ["AWS/ApplicationELB", "HTTPCode_Target_2XX_Count", "TargetGroup", module.ledger_api.target_group_arn_suffix, { label = "2xx" }],
            ["AWS/ApplicationELB", "HTTPCode_Target_4XX_Count", "TargetGroup", module.ledger_api.target_group_arn_suffix, { label = "4xx" }],
            ["AWS/ApplicationELB", "HTTPCode_Target_5XX_Count", "TargetGroup", module.ledger_api.target_group_arn_suffix, { label = "5xx" }],
          ]
        }
      },
      {
        type = "metric", x = 12, y = 39, width = 12, height = 6
        properties = {
          title  = "Requisições e códigos de resposta - Consolidation.Api"
          view   = "timeSeries"
          region = var.aws_region
          period = 60
          stat   = "Sum"
          metrics = [
            ["AWS/ApplicationELB", "RequestCountPerTarget", "TargetGroup", module.consolidation_api.target_group_arn_suffix, { label = "Requisições" }],
            ["AWS/ApplicationELB", "HTTPCode_Target_2XX_Count", "TargetGroup", module.consolidation_api.target_group_arn_suffix, { label = "2xx" }],
            ["AWS/ApplicationELB", "HTTPCode_Target_4XX_Count", "TargetGroup", module.consolidation_api.target_group_arn_suffix, { label = "4xx" }],
            ["AWS/ApplicationELB", "HTTPCode_Target_5XX_Count", "TargetGroup", module.consolidation_api.target_group_arn_suffix, { label = "5xx" }],
          ]
        }
      },
      {
        type = "metric", x = 0, y = 45, width = 12, height = 6
        properties = {
          title  = "Tempo de resposta do target - Ledger.Api (p50/p95/p99)"
          view   = "timeSeries"
          region = var.aws_region
          period = 60
          metrics = [
            ["AWS/ApplicationELB", "TargetResponseTime", "TargetGroup", module.ledger_api.target_group_arn_suffix, { stat = "p50", label = "p50" }],
            ["AWS/ApplicationELB", "TargetResponseTime", "TargetGroup", module.ledger_api.target_group_arn_suffix, { stat = "p95", label = "p95" }],
            ["AWS/ApplicationELB", "TargetResponseTime", "TargetGroup", module.ledger_api.target_group_arn_suffix, { stat = "p99", label = "p99" }],
          ]
        }
      },
      {
        type = "metric", x = 12, y = 45, width = 12, height = 6
        properties = {
          title  = "Tempo de resposta do target - Consolidation.Api (p50/p95/p99)"
          view   = "timeSeries"
          region = var.aws_region
          period = 60
          metrics = [
            ["AWS/ApplicationELB", "TargetResponseTime", "TargetGroup", module.consolidation_api.target_group_arn_suffix, { stat = "p50", label = "p50" }],
            ["AWS/ApplicationELB", "TargetResponseTime", "TargetGroup", module.consolidation_api.target_group_arn_suffix, { stat = "p95", label = "p95" }],
            ["AWS/ApplicationELB", "TargetResponseTime", "TargetGroup", module.consolidation_api.target_group_arn_suffix, { stat = "p99", label = "p99" }],
          ]
        }
      },
      {
        type = "metric", x = 0, y = 51, width = 12, height = 6
        properties = {
          title  = "Targets não saudáveis (ambas as APIs)"
          view   = "timeSeries"
          region = var.aws_region
          period = 60
          stat   = "Maximum"
          metrics = [
            ["AWS/ApplicationELB", "UnHealthyHostCount", "TargetGroup", module.ledger_api.target_group_arn_suffix, { label = "Ledger.Api" }],
            ["AWS/ApplicationELB", "UnHealthyHostCount", "TargetGroup", module.consolidation_api.target_group_arn_suffix, { label = "Consolidation.Api" }],
          ]
        }
      },
      {
        type = "metric", x = 12, y = 51, width = 12, height = 6
        properties = {
          title  = "Task count / CPU / Memória (ECS - ambas as APIs)"
          view   = "timeSeries"
          region = var.aws_region
          period = 60
          metrics = [
            ["ECS/ContainerInsights", "RunningTaskCount", "ClusterName", module.ecs_cluster.cluster_name, "ServiceName", module.ledger_api.service_name, { stat = "Average", label = "Tasks - Ledger.Api" }],
            ["ECS/ContainerInsights", "RunningTaskCount", "ClusterName", module.ecs_cluster.cluster_name, "ServiceName", module.consolidation_api.service_name, { stat = "Average", label = "Tasks - Consolidation.Api" }],
            ["AWS/ECS", "CPUUtilization", "ClusterName", module.ecs_cluster.cluster_name, "ServiceName", module.ledger_api.service_name, { stat = "Average", label = "CPU % - Ledger.Api" }],
            ["AWS/ECS", "MemoryUtilization", "ClusterName", module.ecs_cluster.cluster_name, "ServiceName", module.ledger_api.service_name, { stat = "Average", label = "Memória % - Ledger.Api" }],
          ]
        }
      },

      # --- Deployment (estado de release/canary/alarme/rollback) ---
      {
        type       = "text", x = 0, y = 57, width = 24, height = 1
        properties = { markdown = "## Deployment - estratégia por workload e gates de rollback" }
      },
      {
        # Conteúdo real, injetado pelo Terraform a partir das mesmas
        # variáveis que efetivamente configuram deployment_configuration em
        # cada módulo ecs-service-* - nunca um valor "ao vivo" de uma
        # promoção em andamento (isso só é visível via ECS API/describe-services,
        # fora do escopo de um dashboard CloudWatch).
        type = "text", x = 0, y = 58, width = 24, height = 4
        properties = {
          markdown = join("\n", [
            "**Ledger.Api / Consolidation.Api**: CANARY nativo do ECS - revisão estável atual (últimos 12 chars do digest): Ledger.Api=`${substr(var.ledger_api_image, length(var.ledger_api_image) - 12, 12)}`, Consolidation.Api=`${substr(var.consolidation_api_image, length(var.consolidation_api_image) - 12, 12)}`.",
            "**Consolidation.Worker**: capacity canary - primary_desired_count=${var.consolidation_worker_primary_desired_count}, canary_desired_count=${var.consolidation_worker_canary_desired_count} (0 = nenhum canário ativo).",
            "**Ledger.OutboxPublisher**: rolling controlado, desired_count=${var.ledger_outbox_publisher_desired_count}.",
            "Estado de canário/rollback EM ANDAMENTO (revisão candidata, % de tráfego já deslocado) só é visível via `aws ecs describe-services` durante uma promoção real - não é um dado que um dashboard CloudWatch possa exibir ao vivo.",
          ])
        }
      },
      {
        type = "alarm", x = 0, y = 62, width = 12, height = 6
        properties = {
          title  = "Alarmes de ALB (gate de rollback do CANARY nativo - Ledger.Api/Consolidation.Api)"
          alarms = concat(values(module.ledger_api.alarm_arns), values(module.consolidation_api.alarm_arns))
        }
      },
      {
        type = "alarm", x = 12, y = 62, width = 12, height = 6
        properties = {
          title  = "Alarmes de Worker (capacity canary) e Publisher (rolling)"
          alarms = concat(values(module.alarms_consolidation_worker.alarm_arns), values(module.alarms_ledger_outbox_publisher.alarm_arns))
        }
      },
    ]
  })
}

resource "aws_cloudwatch_dashboard" "this" {
  dashboard_name = "banco-carrefour-${local.environment}"
  dashboard_body = local.dashboard_body_json
}

# --- IAM: task roles (aplicação) e task execution roles (pull ECR + logs + resolver secrets), uma dupla por workload - nunca compartilhadas ---
module "task_roles" {
  source = "../../modules/iam"

  assume_role_service_principal = "ecs-tasks.amazonaws.com"

  roles = {
    "banco-carrefour-${local.environment}-ledger-api-task" = {
      secret_arns = [module.secrets.secret_arns["banco-carrefour/${local.environment}/ledger-api/db-credentials"]]
    }
    "banco-carrefour-${local.environment}-ledger-outbox-publisher-task" = {
      secret_arns         = [module.secrets.secret_arns["banco-carrefour/${local.environment}/ledger-outbox-publisher/db-credentials"]]
      sqs_send_queue_arns = [module.messaging.queue_arn]
    }
    "banco-carrefour-${local.environment}-consolidation-api-task" = {
      secret_arns = [module.secrets.secret_arns["banco-carrefour/${local.environment}/consolidation-api/db-credentials"]]
    }
    "banco-carrefour-${local.environment}-consolidation-worker-task" = {
      secret_arns            = [module.secrets.secret_arns["banco-carrefour/${local.environment}/consolidation-worker/db-credentials"]]
      sqs_consume_queue_arns = [module.messaging.queue_arn, module.messaging.dlq_arn]
    }
    # Task roles do MigrationRunner: DUAS roles dedicadas e mutuamente
    # exclusivas (
    # Closure, seção 6) - nunca uma única role com acesso aos secrets de
    # AMBAS as fronteiras. A migração Ledger nunca pode ler o secret de
    # Consolidation, e vice-versa. Nenhuma das duas tem acesso a SQS (o
    # migration-runner nunca publica/consome mensagens).
    "banco-carrefour-${local.environment}-migration-ledger-task" = {
      secret_arns = [module.secrets.secret_arns["banco-carrefour/${local.environment}/ledger-migration/db-credentials"]]
    }
    "banco-carrefour-${local.environment}-migration-consolidation-task" = {
      secret_arns = [module.secrets.secret_arns["banco-carrefour/${local.environment}/consolidation-migration/db-credentials"]]
    }
  }

  tags = local.common_tags
}

module "task_execution_roles" {
  source = "../../modules/iam"

  assume_role_service_principal = "ecs-tasks.amazonaws.com"

  roles = {
    "banco-carrefour-${local.environment}-ledger-api-execution" = {
      secret_arns         = [module.secrets.secret_arns["banco-carrefour/${local.environment}/ledger-api/db-credentials"]]
      ecr_repository_arns = [local.ecr_repository_arns["ledger-api"]]
      log_group_arns      = [module.observability.log_group_arns["ledger-api"]]
    }
    "banco-carrefour-${local.environment}-ledger-outbox-publisher-execution" = {
      secret_arns         = [module.secrets.secret_arns["banco-carrefour/${local.environment}/ledger-outbox-publisher/db-credentials"]]
      ecr_repository_arns = [local.ecr_repository_arns["ledger-outbox-publisher"]]
      log_group_arns      = [module.observability.log_group_arns["ledger-outbox-publisher"]]
    }
    "banco-carrefour-${local.environment}-consolidation-api-execution" = {
      secret_arns         = [module.secrets.secret_arns["banco-carrefour/${local.environment}/consolidation-api/db-credentials"]]
      ecr_repository_arns = [local.ecr_repository_arns["consolidation-api"]]
      log_group_arns      = [module.observability.log_group_arns["consolidation-api"]]
    }
    "banco-carrefour-${local.environment}-consolidation-worker-execution" = {
      secret_arns         = [module.secrets.secret_arns["banco-carrefour/${local.environment}/consolidation-worker/db-credentials"]]
      ecr_repository_arns = [local.ecr_repository_arns["consolidation-worker"]]
      log_group_arns      = [module.observability.log_group_arns["consolidation-worker"]]
    }
    # Execution role genérica e compartilhada pelas DUAS task roles de
    # migração (Ledger e Consolidation) -, seção 6: "task
    # execution roles podem compartilhar apenas as permissões genéricas
    # mínimas necessárias para puxar a imagem e escrever logs, quando
    # justificado". Nunca precisa de secret_arns: o MigrationRunner resolve
    # o secret de conexão ele mesmo, via SDK, usando a permissão da TASK
    # role (nunca da execution role) - a execution role só existe para
    # puxar a imagem do ECR e escrever no CloudWatch Logs.
    "banco-carrefour-${local.environment}-migration-runner-execution" = {
      ecr_repository_arns = [local.ecr_repository_arns["migration-runner"]]
      log_group_arns      = [module.observability.log_group_arns["migration-runner"]]
    }
  }

  tags = local.common_tags
}

# --- Alarmes de deployment (gates reais de rollback automático) ---
# Ledger.Api e Consolidation.Api criam seus PRÓPRIOS alarmes de ALB
# (5xx/latência) internamente, dentro do módulo ecs-service-api - evita uma
# dependência circular (o alarme precisa do arn_suffix do target group,
# que só existe depois do próprio módulo criar o serviço). Apenas os
# alarmes de fila (Worker) e de negócio (Publisher), que não dependem de
# nenhum recurso criado pelos módulos de serviço, são criados aqui.
module "alarms_consolidation_worker" {
  source = "../../modules/deployment-alarms"

  environment = local.environment
  name_prefix = "banco-carrefour-${local.environment}-consolidation-worker"

  alarms = {
    dlq-depth = {
      namespace           = "AWS/SQS"
      metric_name         = "ApproximateNumberOfMessagesVisible"
      statistic           = "Maximum"
      period_seconds      = 60
      evaluation_periods  = 3
      threshold           = 1
      comparison_operator = "GreaterThanOrEqualToThreshold"
      dimensions          = { QueueName = "banco-carrefour-${local.environment}-financial-entry-registered-dlq" }
      description         = "Qualquer mensagem na DLQ durante uma avaliação de capacity canary do Consolidation.Worker."
    }
    oldest-message-age = {
      namespace           = "AWS/SQS"
      metric_name         = "ApproximateAgeOfOldestMessage"
      statistic           = "Maximum"
      period_seconds      = 60
      evaluation_periods  = 3
      threshold           = 300
      comparison_operator = "GreaterThanThreshold"
      dimensions          = { QueueName = "banco-carrefour-${local.environment}-financial-entry-registered" }
      description         = "Fila principal com item mais antigo aguardando mais de 5 minutos."
    }
  }

  tags = local.common_tags
}

module "alarms_ledger_outbox_publisher" {
  source = "../../modules/deployment-alarms"

  environment = local.environment
  name_prefix = "banco-carrefour-${local.environment}-ledger-outbox-publisher"

  alarms = {
    publish-failures = {
      namespace           = "BancoCarrefour/Ledger"
      metric_name         = "ledger.outbox.messages.failed"
      statistic           = "Sum"
      period_seconds      = 60
      evaluation_periods  = 3
      threshold           = 5
      comparison_operator = "GreaterThanThreshold"
      dimensions          = {}
      description         = "Falhas de publicação da Outbox acima do aceitável durante o rolling deployment (métrica emitida pela própria aplicação via OTel/ADOT)."
      treat_missing_data  = "notBreaching"
    }
  }

  tags = local.common_tags
}

# --- Ledger.Api (canary nativo do ECS) ---
module "ledger_api" {
  source = "../../modules/ecs-service-api"

  environment   = local.environment
  service_name  = "ledger-api"
  aws_region    = var.aws_region
  cluster_arn   = module.ecs_cluster.cluster_arn
  image         = var.ledger_api_image
  cpu           = var.ledger_api_cpu
  memory        = var.ledger_api_memory
  desired_count = var.ledger_api_desired_count
  min_capacity  = var.ledger_api_min_capacity
  max_capacity  = var.ledger_api_max_capacity

  private_subnet_ids = module.network.private_subnet_ids
  security_group_id  = module.network.ecs_tasks_security_group_id
  vpc_id             = module.network.vpc_id

  alb_https_listener_arn = module.edge.alb_https_listener_arn
  path_pattern           = "/ledger/*"
  listener_rule_priority = 10
  health_check_path      = "/healthz"

  task_execution_role_arn = module.task_execution_roles.role_arns["banco-carrefour-${local.environment}-ledger-api-execution"]
  task_role_arn           = module.task_roles.role_arns["banco-carrefour-${local.environment}-ledger-api-task"]

  environment_variables = {
    ASPNETCORE_URLS           = "http://0.0.0.0:8080"
    ConnectionStrings__Ledger = "Host=${module.rds_ledger.address};Port=${module.rds_ledger.port};Database=ledger"
  }
  secrets = {
    DB_CREDENTIALS_SECRET_ARN = module.secrets.secret_arns["banco-carrefour/${local.environment}/ledger-api/db-credentials"]
  }

  log_group_name = module.observability.log_group_names["ledger-api"]
  otel_endpoint  = local.otel_endpoint

  tags = merge(local.common_tags, { boundary = "ledger" })
}

# --- Consolidation.Api (canary nativo do ECS) ---
module "consolidation_api" {
  source = "../../modules/ecs-service-api"

  environment   = local.environment
  service_name  = "consolidation-api"
  aws_region    = var.aws_region
  cluster_arn   = module.ecs_cluster.cluster_arn
  image         = var.consolidation_api_image
  cpu           = var.consolidation_api_cpu
  memory        = var.consolidation_api_memory
  desired_count = var.consolidation_api_desired_count
  min_capacity  = var.consolidation_api_min_capacity
  max_capacity  = var.consolidation_api_max_capacity

  private_subnet_ids = module.network.private_subnet_ids
  security_group_id  = module.network.ecs_tasks_security_group_id
  vpc_id             = module.network.vpc_id

  alb_https_listener_arn = module.edge.alb_https_listener_arn
  path_pattern           = "/consolidation/*"
  listener_rule_priority = 20
  health_check_path      = "/healthz"

  task_execution_role_arn = module.task_execution_roles.role_arns["banco-carrefour-${local.environment}-consolidation-api-execution"]
  task_role_arn           = module.task_roles.role_arns["banco-carrefour-${local.environment}-consolidation-api-task"]

  environment_variables = {
    ASPNETCORE_URLS                  = "http://0.0.0.0:8080"
    ConnectionStrings__Consolidation = "Host=${module.rds_consolidation.address};Port=${module.rds_consolidation.port};Database=consolidation"
  }
  secrets = {
    DB_CREDENTIALS_SECRET_ARN = module.secrets.secret_arns["banco-carrefour/${local.environment}/consolidation-api/db-credentials"]
  }

  log_group_name = module.observability.log_group_names["consolidation-api"]
  otel_endpoint  = local.otel_endpoint

  tags = merge(local.common_tags, { boundary = "consolidation" })
}

# --- Consolidation.Worker (capacity canary: dois servicos independentes) ---
module "consolidation_worker" {
  source = "../../modules/ecs-service-worker"

  environment = local.environment
  aws_region  = var.aws_region
  cluster_arn = module.ecs_cluster.cluster_arn

  cpu    = var.consolidation_worker_cpu
  memory = var.consolidation_worker_memory

  primary_image         = var.consolidation_worker_primary_image
  primary_desired_count = var.consolidation_worker_primary_desired_count
  canary_image          = var.consolidation_worker_canary_image
  canary_desired_count  = var.consolidation_worker_canary_desired_count

  min_capacity = var.consolidation_worker_min_capacity
  max_capacity = var.consolidation_worker_max_capacity

  private_subnet_ids = module.network.private_subnet_ids
  security_group_id  = module.network.ecs_tasks_security_group_id

  task_execution_role_arn = module.task_execution_roles.role_arns["banco-carrefour-${local.environment}-consolidation-worker-execution"]
  task_role_arn           = module.task_roles.role_arns["banco-carrefour-${local.environment}-consolidation-worker-task"]

  environment_variables = {
    ConnectionStrings__Consolidation = "Host=${module.rds_consolidation.address};Port=${module.rds_consolidation.port};Database=consolidation"
    Sqs__QueueUrl                    = module.messaging.queue_url
  }
  secrets = {
    DB_CREDENTIALS_SECRET_ARN = module.secrets.secret_arns["banco-carrefour/${local.environment}/consolidation-worker/db-credentials"]
  }

  log_group_name = module.observability.log_group_names["consolidation-worker"]
  otel_endpoint  = local.otel_endpoint

  alarm_names = module.alarms_consolidation_worker.alarm_names

  tags = merge(local.common_tags, { boundary = "consolidation" })
}

# --- Ledger.OutboxPublisher (rolling controlado) ---
module "ledger_outbox_publisher" {
  source = "../../modules/ecs-service-publisher"

  environment = local.environment
  aws_region  = var.aws_region
  cluster_arn = module.ecs_cluster.cluster_arn

  image  = var.ledger_outbox_publisher_image
  cpu    = var.ledger_outbox_publisher_cpu
  memory = var.ledger_outbox_publisher_memory

  desired_count = var.ledger_outbox_publisher_desired_count
  min_capacity  = var.ledger_outbox_publisher_min_capacity
  max_capacity  = var.ledger_outbox_publisher_max_capacity

  private_subnet_ids = module.network.private_subnet_ids
  security_group_id  = module.network.ecs_tasks_security_group_id

  task_execution_role_arn = module.task_execution_roles.role_arns["banco-carrefour-${local.environment}-ledger-outbox-publisher-execution"]
  task_role_arn           = module.task_roles.role_arns["banco-carrefour-${local.environment}-ledger-outbox-publisher-task"]

  environment_variables = {
    ConnectionStrings__Ledger = "Host=${module.rds_ledger.address};Port=${module.rds_ledger.port};Database=ledger"
    Sqs__QueueUrl             = module.messaging.queue_url
  }
  secrets = {
    DB_CREDENTIALS_SECRET_ARN = module.secrets.secret_arns["banco-carrefour/${local.environment}/ledger-outbox-publisher/db-credentials"]
  }

  log_group_name = module.observability.log_group_names["ledger-outbox-publisher"]
  otel_endpoint  = local.otel_endpoint

  alarm_names = module.alarms_ledger_outbox_publisher.alarm_names

  tags = merge(local.common_tags, { boundary = "ledger" })
}

# --- Tasks ECS one-off de migração (Release Boundary and Deployment Safety
# Closure, seções 5-6) - NUNCA um aws_ecs_service, executadas
# exclusivamente via "aws ecs run-task". DUAS instâncias, uma por
# fronteira - NUNCA uma única task definition/role compartilhada entre
# Ledger e Consolidation (isolamento de IAM real e testável). Nunca um 5º
# workload de negócio no modelo de domínio arquitetural.
module "ecs_migration_task_ledger" {
  source = "../../modules/ecs-migration-task"

  environment = local.environment
  boundary    = "ledger"
  aws_region  = var.aws_region
  vpc_id      = module.network.vpc_id

  private_subnet_ids              = module.network.private_subnet_ids
  rds_security_group_id           = module.network.rds_security_group_id
  vpc_endpoints_security_group_id = module.network.vpc_endpoints_security_group_id

  image = var.migration_runner_image

  task_execution_role_arn = module.task_execution_roles.role_arns["banco-carrefour-${local.environment}-migration-runner-execution"]
  task_role_arn           = module.task_roles.role_arns["banco-carrefour-${local.environment}-migration-ledger-task"]

  environment_variables = {
    ConnectionStrings__Ledger = "Host=${module.rds_ledger.address};Port=${module.rds_ledger.port};Database=ledger"
  }

  log_group_name = module.observability.log_group_names["migration-runner"]

  tags = local.common_tags
}

module "ecs_migration_task_consolidation" {
  source = "../../modules/ecs-migration-task"

  environment = local.environment
  boundary    = "consolidation"
  aws_region  = var.aws_region
  vpc_id      = module.network.vpc_id

  private_subnet_ids              = module.network.private_subnet_ids
  rds_security_group_id           = module.network.rds_security_group_id
  vpc_endpoints_security_group_id = module.network.vpc_endpoints_security_group_id

  image = var.migration_runner_image

  task_execution_role_arn = module.task_execution_roles.role_arns["banco-carrefour-${local.environment}-migration-runner-execution"]
  task_role_arn           = module.task_roles.role_arns["banco-carrefour-${local.environment}-migration-consolidation-task"]

  environment_variables = {
    ConnectionStrings__Consolidation = "Host=${module.rds_consolidation.address};Port=${module.rds_consolidation.port};Database=consolidation"
  }

  log_group_name = module.observability.log_group_names["migration-runner"]

  tags = local.common_tags
}

# Regras simétricas de ingress (criadas FORA do módulo network para evitar
# dependência circular - o mesmo padrão já usado para o VPC Link V2 -> ALB,
# ver módulo edge): cada security group dedicado de task de migração
# precisa de acesso ao RDS e aos VPC endpoints, mas o módulo network não
# pode referenciar um security group criado por um módulo que só existe
# depois dele. rds_security_group_id permanece um ÚNICO objeto
# compartilhado por Ledger e Consolidation neste módulo de rede (não
# alterado por esta auditoria - fora do escopo de uma correção restrita) -
# o isolamento real e testável entre fronteiras de migração é o de
# IAM/secret (task roles dedicadas), nunca dependente só deste SG
# compartilhado.
resource "aws_security_group_rule" "migration_task_ledger_to_rds" {
  type                     = "ingress"
  security_group_id        = module.network.rds_security_group_id
  source_security_group_id = module.ecs_migration_task_ledger.security_group_id
  from_port                = 5432
  to_port                  = 5432
  protocol                 = "tcp"
  description              = "PostgreSQL a partir da task one-off de migracao Ledger."
}

resource "aws_security_group_rule" "migration_task_consolidation_to_rds" {
  type                     = "ingress"
  security_group_id        = module.network.rds_security_group_id
  source_security_group_id = module.ecs_migration_task_consolidation.security_group_id
  from_port                = 5432
  to_port                  = 5432
  protocol                 = "tcp"
  description              = "PostgreSQL a partir da task one-off de migracao Consolidation."
}

resource "aws_security_group_rule" "migration_task_ledger_to_vpc_endpoints" {
  count = module.network.vpc_endpoints_security_group_id != null ? 1 : 0

  type                     = "ingress"
  security_group_id        = module.network.vpc_endpoints_security_group_id
  source_security_group_id = module.ecs_migration_task_ledger.security_group_id
  from_port                = 443
  to_port                  = 443
  protocol                 = "tcp"
  description              = "HTTPS a partir da task one-off de migracao Ledger (Secrets Manager, ECR, CloudWatch Logs)."
}

resource "aws_security_group_rule" "migration_task_consolidation_to_vpc_endpoints" {
  count = module.network.vpc_endpoints_security_group_id != null ? 1 : 0

  type                     = "ingress"
  security_group_id        = module.network.vpc_endpoints_security_group_id
  source_security_group_id = module.ecs_migration_task_consolidation.security_group_id
  from_port                = 443
  to_port                  = 443
  protocol                 = "tcp"
  description              = "HTTPS a partir da task one-off de migracao Consolidation (Secrets Manager, ECR, CloudWatch Logs)."
}

# --- IAM/OIDC: role de deploy assumida pelo workflow de promocao a Production (ver ADR-0014) ---
data "aws_iam_policy_document" "deploy_assume_role" {
  statement {
    sid     = "AllowGitHubActionsOIDC"
    effect  = "Allow"
    actions = ["sts:AssumeRoleWithWebIdentity"]

    principals {
      type        = "Federated"
      identifiers = ["arn:aws:iam::${var.aws_account_id}:oidc-provider/token.actions.githubusercontent.com"]
    }

    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:aud"
      values   = ["sts.amazonaws.com"]
    }

    # Restrito ao GitHub Environment "production" (nunca a um branch/ref
    # generico) - a claim "environment:production" so existe no token OIDC
    # quando o job realmente roda sob a protecao do GitHub Environment
    # correspondente (aprovacao obrigatoria configurada externamente,
    # nunca por este Terraform).
    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:sub"
      values   = ["repo:${var.github_repository}:environment:${var.github_environment}"]
    }
  }
}

resource "aws_iam_role" "deploy" {
  name               = "banco-carrefour-${local.environment}-deploy"
  assume_role_policy = data.aws_iam_policy_document.deploy_assume_role.json
  tags               = local.common_tags
}

data "aws_iam_policy_document" "deploy_permissions" {
  statement {
    sid    = "UpdateOwnEcsServices"
    effect = "Allow"
    actions = [
      "ecs:UpdateService",
      "ecs:DescribeServices",
      "ecs:DescribeTaskDefinition",
      "ecs:RegisterTaskDefinition",
      "ecs:ListTasks",
      "ecs:DescribeTasks",
    ]
    resources = ["*"] # ecs:RegisterTaskDefinition nao suporta escopo por Resource - as demais acoes sao efetivamente escopadas pelo cluster no plano de execucao real do workflow (nunca chamado fora do cluster deste ambiente).
  }

  statement {
    sid       = "PassTaskRoles"
    effect    = "Allow"
    actions   = ["iam:PassRole"]
    resources = concat(values(module.task_roles.role_arns), values(module.task_execution_roles.role_arns))
  }

  statement {
    sid       = "ReadCloudWatchAlarmsForGate"
    effect    = "Allow"
    actions   = ["cloudwatch:DescribeAlarms"]
    resources = ["*"]
  }
}

resource "aws_iam_role_policy" "deploy" {
  name   = "deploy-least-privilege"
  role   = aws_iam_role.deploy.id
  policy = data.aws_iam_policy_document.deploy_permissions.json
}
