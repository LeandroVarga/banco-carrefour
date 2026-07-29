# Log groups (um por workload deployável) e um dashboard consolidado por
# ambiente. Métricas de negócio/arquiteturais (lançamentos aceitos,
# eventos duplicados, atraso de projeção, backlog de Outbox) são emitidas
# pela própria aplicação via OpenTelemetry/ADOT (ADR-0012) - este módulo
# apenas declara onde os logs residem e como o dashboard os apresenta,
# nunca instrumenta a aplicação.
resource "aws_cloudwatch_log_group" "this" {
  for_each = var.log_groups

  name              = "/aws/ecs/banco-carrefour-${var.environment}/${each.key}"
  retention_in_days = each.value

  tags = merge(var.tags, {
    project     = "banco-carrefour"
    environment = var.environment
    managed_by  = "terraform"
    workload    = each.key
  })
}

# O dashboard CloudWatch NÃO é criado neste módulo: seus widgets reais
# precisam de outputs dos módulos de workload (ecs-service-api, ecs-service-worker,
# ecs-service-publisher, deployment-alarms), que por sua vez consomem
# module.observability.log_group_names/log_group_arns na sua própria
# instanciação - colocar o dashboard aqui criaria uma dependência circular
# (observability -> ledger_api -> observability). O recurso
# aws_cloudwatch_dashboard é declarado diretamente em cada ambiente
# (infra/terraform/environments/{development,staging,production}/main.tf),
# depois de todos os módulos de workload.
