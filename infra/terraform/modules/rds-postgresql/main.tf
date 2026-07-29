# Instância RDS PostgreSQL genérica - usada duas vezes por ambiente (Ledger
# e Consolidation), nunca uma instância compartilhada entre as duas
# fronteiras (ADR-0002: persistências independentes por fronteira). O
# usuário mestre nunca é usado pela aplicação em runtime - roles de menor
# privilégio são criadas por migration/bootstrap (mesmo modelo do
# ADR-0009), fora do escopo deste módulo.
resource "aws_db_subnet_group" "this" {
  name       = "${var.identifier}-subnets"
  subnet_ids = var.subnet_ids
  tags       = var.tags
}

resource "aws_db_parameter_group" "this" {
  name   = "${var.identifier}-params"
  family = "postgres16"

  dynamic "parameter" {
    for_each = var.parameters
    content {
      name  = parameter.key
      value = parameter.value
    }
  }

  tags = var.tags
}

resource "aws_db_instance" "this" {
  identifier     = var.identifier
  engine         = "postgres"
  engine_version = var.engine_version
  instance_class = var.instance_class

  allocated_storage     = var.allocated_storage_gb
  max_allocated_storage = var.max_allocated_storage_gb > 0 ? var.max_allocated_storage_gb : null
  storage_type          = "gp3"
  storage_encrypted     = true

  db_name  = var.database_name
  username = var.master_username

  # manage_master_user_password (RDS-managed, integrado ao Secrets
  # Manager, rotação automática pela própria AWS) é o padrão quando nenhum
  # secret externo é fornecido - nunca uma senha mestre em texto plano em
  # variável/state deste módulo.
  manage_master_user_password = var.master_password_secret_arn == null
  password                    = null

  db_subnet_group_name   = aws_db_subnet_group.this.name
  vpc_security_group_ids = var.vpc_security_group_ids
  parameter_group_name   = aws_db_parameter_group.this.name

  multi_az            = var.multi_az
  publicly_accessible = false

  backup_retention_period = var.backup_retention_days
  # Janela fora do horário comercial (03:00-04:00 UTC) - ambos os bancos
  # (Ledger/Consolidation) usam a MESMA janela de proposito, para que uma
  # manutenção nunca deixe apenas um dos dois indisponível durante o
  # backup do outro.
  backup_window      = "03:00-04:00"
  maintenance_window = "sun:04:30-sun:05:30"

  deletion_protection       = var.deletion_protection
  skip_final_snapshot       = var.skip_final_snapshot
  final_snapshot_identifier = var.skip_final_snapshot ? null : "${var.identifier}-final-${formatdate("YYYYMMDDhhmmss", timestamp())}"

  performance_insights_enabled          = var.performance_insights_enabled
  performance_insights_retention_period = var.performance_insights_enabled ? var.performance_insights_retention_days : null

  monitoring_interval = var.monitoring_interval_seconds
  monitoring_role_arn = var.monitoring_interval_seconds > 0 ? var.monitoring_role_arn : null

  apply_immediately = var.apply_immediately

  copy_tags_to_snapshot = true
  tags                  = var.tags

  lifecycle {
    ignore_changes = [
      # O identificador de snapshot final embutiria um timestamp de plan
      # diferente a cada execução - nunca deve forçar um replace/diff
      # espúrio fora de um destroy real.
      final_snapshot_identifier,
    ]
  }
}
