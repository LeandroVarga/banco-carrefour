# Fábrica genérica de alarmes CloudWatch - cada entrada de var.alarms vira
# um aws_cloudwatch_metric_alarm. Os NOMES resultantes (nunca os ARNs) são
# o contrato consumido por aws_ecs_service.alarms.alarm_names (gate real de
# rollback automático do ECS) e pelas condições de promoção dos workflows
# de deploy (ver ADR-0014).
resource "aws_cloudwatch_metric_alarm" "this" {
  for_each = var.alarms

  alarm_name          = "${var.name_prefix}-${each.key}"
  alarm_description   = each.value.description
  namespace           = each.value.namespace
  metric_name         = each.value.metric_name
  statistic           = each.value.statistic
  period              = each.value.period_seconds
  evaluation_periods  = each.value.evaluation_periods
  threshold           = each.value.threshold
  comparison_operator = each.value.comparison_operator
  dimensions          = each.value.dimensions
  treat_missing_data  = each.value.treat_missing_data

  alarm_actions = var.alarm_actions

  tags = merge(var.tags, {
    project     = "banco-carrefour"
    environment = var.environment
    managed_by  = "terraform"
  })
}
