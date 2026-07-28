# Observabilidade da task one-off de migracao. Duas fontes REAIS de sinal,
# nenhuma metrica fabricada:
#
#   1) Falhas visiveis dentro da aplicacao MigrationRunner - o comando
#      "migrate" (unico caminho automatico) emite eventos StructuredLog
#      genuinos em stdout (ver src/Migrations/MigrationRunner/*.cs) quando
#      falha: "migration.failed", "migration.configuration_error",
#      "migration.refused_unapproved_phase", "migration.unhandled_error",
#      "migration.usage_error". Um metric filter no log group de aplicacao
#      (var.log_group_name) transforma essas linhas JSON reais num metrico
#      customico - nunca inventa um campo que o codigo nao emite.
#
#   2) Falhas ANTES da aplicacao rodar - quando a task nem chega a
#      iniciar o container (imagem invalida, capacidade Fargate
#      insuficiente, erro de rede) nenhuma linha de log da aplicacao e
#      emitida, entao (1) nunca captura esse caso. O proprio ECS emite um
#      evento real "ECS Task State Change" no EventBridge com
#      "stopCode": "TaskFailedToStart" (ver AWS Documentation MCP:
#      https://docs.aws.amazon.com/AmazonECS/latest/developerguide/ecs_task_events.html
#      e .../stopped-task-error-codes.html) - uma regra EventBridge captura
#      esse caso especificamente, escopada ao "group": "family:<familia
#      desta task definition>" (confirmado no mesmo doc oficial) para nunca
#      capturar eventos de task de OUTRO workload no mesmo cluster.
#
# Ambos os mecanismos e sintaxes (JSON metric filter pattern, EventBridge
# event pattern, permissao de log group para EventBridge, metrica
# AWS/Events.MatchedEvents) foram confirmados via AWS Documentation MCP
# antes de escrever este arquivo - nunca inventados.

locals {
  # Eventos StructuredLog que representam falha no UNICO caminho
  # automatico ("migrate") - nunca inclui migration.contract_failed nem
  # migration.backfill_failed, pois "contract"/"backfill" nunca sao
  # invocados por nenhum workflow automatizado (ver ContractCommand.cs/
  # BackfillCommand.cs) - uma falha nesses comandos e sempre observada
  # diretamente por quem os executa manualmente, nunca por este alarme.
  migration_failure_events = [
    "migration.failed",
    "migration.configuration_error",
    "migration.refused_unapproved_phase",
    "migration.unhandled_error",
    "migration.usage_error",
  ]

  # boundary.ToString() do enum MigrationBoundary (C#) e sempre "Ledger" ou
  # "Consolidation" (capitalizado) - ver src/Migrations/MigrationRunner/MigrationBoundary.cs.
  boundary_display_name = { ledger = "Ledger", consolidation = "Consolidation" }[var.boundary]

  # Escopado por fronteira (campo "boundary" real do StructuredLog) - as
  # duas instancias deste modulo compartilham o MESMO log group de
  # aplicacao, entao sem este filtro o alarme de UMA fronteira dispararia
  # tambem para falhas da OUTRA. Excecao conhecida: "migration.usage_error"
  # pode ocorrer ANTES do parsing de --boundary suceder (Program.cs) e
  # nesse caso pode nao carregar o campo "boundary" - essa falha bem rara
  # (uso incorreto da CLI, nunca um cenario real de deploy) pode nao ser
  # atribuida a nenhuma das duas fronteiras; aceito como limitacao
  # documentada, nunca escondida.
  migration_failure_filter_pattern = join(" || ", [
    for event_name in local.migration_failure_events :
    "(($.event = \"${event_name}\") && ($.boundary = \"${local.boundary_display_name}\"))"
  ])

  migration_runner_failures_metric_namespace = "BancoCarrefour/MigrationRunner"
  migration_runner_failures_metric_name      = "MigrationRunnerFailures"
}

# --- (1) Falhas reportadas pela propria aplicacao (StructuredLog) ---

resource "aws_cloudwatch_log_metric_filter" "migration_runner_failures" {
  # Nome escopado por fronteira (var.boundary): este modulo e instanciado
  # DUAS vezes por ambiente (Ledger, Consolidation), ambas escrevendo no
  # MESMO log group de aplicacao compartilhado (var.log_group_name,
  # distinguido por awslogs-stream-prefix) - sem o sufixo de fronteira,
  # as duas instancias tentariam criar um metric filter com o nome
  # IDENTICO no mesmo log group (colisao real no ECS/CloudWatch).
  name           = "banco-carrefour-${var.environment}-migration-${var.boundary}-runner-failures"
  log_group_name = var.log_group_name
  pattern        = "{ ${local.migration_failure_filter_pattern} }"

  metric_transformation {
    name          = local.migration_runner_failures_metric_name
    namespace     = local.migration_runner_failures_metric_namespace
    value         = "1"
    default_value = 0
  }
}

module "migration_runner_failures_alarm" {
  source = "../deployment-alarms"

  # Sem referencia direta a atributos do metric filter acima (namespace/nome
  # sao os mesmos locals usados nos dois lugares) - depends_on explicito
  # garante que o metric filter exista antes do alarme, mesmo sem uma
  # aresta implicita de dependencia.
  depends_on = [aws_cloudwatch_log_metric_filter.migration_runner_failures]

  environment = var.environment
  name_prefix = "banco-carrefour-${var.environment}-migration-${var.boundary}-runner"

  alarms = {
    "app-level-failure" = {
      namespace           = local.migration_runner_failures_metric_namespace
      metric_name         = local.migration_runner_failures_metric_name
      statistic           = "Sum"
      period_seconds      = 300
      evaluation_periods  = 1
      threshold           = 0
      comparison_operator = "GreaterThanThreshold"
      dimensions          = {}
      treat_missing_data  = "notBreaching"
      description         = "Comando 'migrate' (Ledger ou Consolidation) reportou falha via StructuredLog - ver o log group de aplicacao para o evento completo (nunca inclui secrets, ver StructuredLog.cs)."
    }
  }

  alarm_actions = var.alarm_actions
  tags          = var.tags
}

# --- (2) Falhas antes da aplicacao rodar (evento nativo do ECS) ---

resource "aws_cloudwatch_log_group" "task_lifecycle_events" {
  name              = "/aws/events/banco-carrefour-${var.environment}/migration-${var.boundary}-task-lifecycle"
  retention_in_days = var.task_lifecycle_log_retention_days

  tags = merge(local.common_tags, { Name = "banco-carrefour-${var.environment}-migration-${var.boundary}-task-lifecycle-logs" })
}

resource "aws_cloudwatch_event_rule" "task_failed_to_start" {
  name        = "banco-carrefour-${var.environment}-migration-${var.boundary}-task-failed-to-start"
  description = "ECS Task State Change com stopCode=TaskFailedToStart, escopado a family:${aws_ecs_task_definition.this.family} - a task de migracao nunca chegou a iniciar o container (imagem, capacidade Fargate ou rede), entao nenhum log da aplicacao existe para este caso."

  event_pattern = jsonencode({
    source      = ["aws.ecs"]
    detail-type = ["ECS Task State Change"]
    detail = {
      lastStatus = ["STOPPED"]
      stopCode   = ["TaskFailedToStart"]
      group      = ["family:${aws_ecs_task_definition.this.family}"]
    }
  })

  tags = local.common_tags
}

# CloudWatch Logs como alvo direto (sem role_arn - permissao concedida via
# resource policy no log group, nao via IAM role assumida pela regra, ver
# exemplo oficial "Cloudwatch Log Group Usage" do provider AWS/Terraform).
resource "aws_cloudwatch_event_target" "task_failed_to_start_to_logs" {
  rule = aws_cloudwatch_event_rule.task_failed_to_start.name
  arn  = aws_cloudwatch_log_group.task_lifecycle_events.arn
}

data "aws_iam_policy_document" "task_lifecycle_events_log_policy" {
  statement {
    sid    = "AllowEventBridgeCreateLogStream"
    effect = "Allow"
    principals {
      type        = "Service"
      identifiers = ["events.amazonaws.com", "delivery.logs.amazonaws.com"]
    }
    actions   = ["logs:CreateLogStream"]
    resources = ["${aws_cloudwatch_log_group.task_lifecycle_events.arn}:*"]
  }

  statement {
    sid    = "AllowEventBridgePutLogEventsFromThisRuleOnly"
    effect = "Allow"
    principals {
      type        = "Service"
      identifiers = ["events.amazonaws.com", "delivery.logs.amazonaws.com"]
    }
    actions   = ["logs:PutLogEvents"]
    resources = ["${aws_cloudwatch_log_group.task_lifecycle_events.arn}:*:*"]

    condition {
      test     = "ArnEquals"
      variable = "aws:SourceArn"
      values   = [aws_cloudwatch_event_rule.task_failed_to_start.arn]
    }
  }
}

resource "aws_cloudwatch_log_resource_policy" "task_lifecycle_events" {
  policy_name     = "banco-carrefour-${var.environment}-migration-${var.boundary}-task-lifecycle-events"
  policy_document = data.aws_iam_policy_document.task_lifecycle_events_log_policy.json
}

module "task_failed_to_start_alarm" {
  source = "../deployment-alarms"

  environment = var.environment
  name_prefix = "banco-carrefour-${var.environment}-migration-${var.boundary}-runner"

  alarms = {
    "task-failed-to-start" = {
      namespace           = "AWS/Events"
      metric_name         = "MatchedEvents"
      statistic           = "Sum"
      period_seconds      = 300
      evaluation_periods  = 1
      threshold           = 0
      comparison_operator = "GreaterThanThreshold"
      dimensions          = { RuleName = aws_cloudwatch_event_rule.task_failed_to_start.name }
      treat_missing_data  = "notBreaching"
      description         = "Task ECS one-off de migracao (Ledger ou Consolidation) parou com stopCode=TaskFailedToStart antes de iniciar o container - ver ${aws_cloudwatch_log_group.task_lifecycle_events.name} para o evento bruto."
    }
  }

  alarm_actions = var.alarm_actions
  tags          = var.tags
}
