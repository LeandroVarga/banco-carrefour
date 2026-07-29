# Cluster ECS compartilhado pelos 4 workloads deployaveis deste ambiente
# (Ledger.Api, Ledger.OutboxPublisher, Consolidation.Api,
# Consolidation.Worker) - somente Fargate/Fargate Spot (nunca EC2
# gerenciado por este repositorio, sem AMI/patching proprio a manter).
resource "aws_ecs_cluster" "this" {
  name = "banco-carrefour-${var.environment}"

  setting {
    name  = "containerInsights"
    value = var.enable_container_insights ? "enabled" : "disabled"
  }

  tags = merge(var.tags, {
    project     = "banco-carrefour"
    environment = var.environment
    managed_by  = "terraform"
  })
}

# FARGATE_SPOT disponivel na estrategia de capacidade, mas nunca como
# unica opcao (rollout/rollback nunca deve depender de capacidade spot
# disponivel) - cada servico decide seu proprio capacity_provider_strategy
# via aws_ecs_service, este modulo apenas habilita ambos os providers no
# cluster.
resource "aws_ecs_cluster_capacity_providers" "this" {
  cluster_name = aws_ecs_cluster.this.name

  capacity_providers = ["FARGATE", "FARGATE_SPOT"]

  default_capacity_provider_strategy {
    capacity_provider = "FARGATE"
    weight            = 1
    base              = 0
  }
}
