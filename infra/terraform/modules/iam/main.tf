# Estrutura IAM de menor privilégio de referência: uma role por componente,
# cada uma com policy inline restrita apenas aos recursos (secret/parameter/
# KMS) que aquele componente deveria acessar. Provisionamento real via
# Terraform — mas o ENFORCEMENT dessas policies não é comprovável no
# LocalStack Hobby usado neste projeto (confirmado empiricamente: um usuário
# sem nenhuma policy anexada conseguiu ler um secret — ver ADR-0009 e
# docs/security/threat-model.md). Por isso este módulo é tratado como
# estrutura local + referência AWS, nunca como prova de isolamento negativo
# comprovado localmente.
data "aws_iam_policy_document" "assume_role" {
  statement {
    sid     = "AllowAssumeRole"
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = [var.assume_role_service_principal]
    }
  }
}

resource "aws_iam_role" "this" {
  for_each = var.roles

  name               = each.key
  assume_role_policy = data.aws_iam_policy_document.assume_role.json
  tags               = var.tags
}

data "aws_iam_policy_document" "least_privilege" {
  for_each = var.roles

  dynamic "statement" {
    for_each = length(each.value.secret_arns) > 0 ? [1] : []
    content {
      sid       = "ReadOwnSecret"
      actions   = ["secretsmanager:GetSecretValue"]
      resources = each.value.secret_arns
    }
  }

  dynamic "statement" {
    for_each = length(each.value.parameter_arns) > 0 ? [1] : []
    content {
      sid       = "ReadConfigParameters"
      actions   = ["ssm:GetParameter", "ssm:GetParameters"]
      resources = each.value.parameter_arns
    }
  }

  dynamic "statement" {
    for_each = length(each.value.kms_key_arns) > 0 ? [1] : []
    content {
      sid       = "DecryptSecretsKey"
      actions   = ["kms:Decrypt"]
      resources = each.value.kms_key_arns
    }
  }

  # ecr:GetAuthorizationToken so e suportada com Resource "*" (a AWS nao
  # oferece escopo por repositorio para essa acao especifica, mesma
  # limitacao ja documentada na role de publicacao do ADR-0013) - as
  # demais acoes de pull permanecem escopadas aos repositorios exatos
  # desta role (nunca "ecr:*"/Resource "*" para acoes escopaveis).
  dynamic "statement" {
    for_each = length(each.value.ecr_repository_arns) > 0 ? [1] : []
    content {
      sid       = "PullOwnRepository"
      actions   = ["ecr:BatchGetImage", "ecr:GetDownloadUrlForLayer", "ecr:BatchCheckLayerAvailability"]
      resources = each.value.ecr_repository_arns
    }
  }

  dynamic "statement" {
    for_each = length(each.value.ecr_repository_arns) > 0 ? [1] : []
    content {
      sid       = "EcrAuthToken"
      actions   = ["ecr:GetAuthorizationToken"]
      resources = ["*"]
    }
  }

  dynamic "statement" {
    for_each = length(each.value.log_group_arns) > 0 ? [1] : []
    content {
      sid       = "WriteOwnLogGroup"
      actions   = ["logs:CreateLogStream", "logs:PutLogEvents"]
      resources = each.value.log_group_arns
    }
  }

  # Publisher (Ledger.OutboxPublisher): somente publica - nunca recebe ou
  # exclui mensagens da fila que ele mesmo produz.
  dynamic "statement" {
    for_each = length(each.value.sqs_send_queue_arns) > 0 ? [1] : []
    content {
      sid       = "SendToOwnQueue"
      actions   = ["sqs:SendMessage", "sqs:GetQueueAttributes"]
      resources = each.value.sqs_send_queue_arns
    }
  }

  # Worker (Consolidation.Worker): somente consome - nunca publica na fila
  # que ele mesmo lê (isolamento real produtor/consumidor, ver ADR-0009
  # seção 9).
  dynamic "statement" {
    for_each = length(each.value.sqs_consume_queue_arns) > 0 ? [1] : []
    content {
      sid       = "ConsumeOwnQueue"
      actions   = ["sqs:ReceiveMessage", "sqs:DeleteMessage", "sqs:GetQueueAttributes", "sqs:ChangeMessageVisibility"]
      resources = each.value.sqs_consume_queue_arns
    }
  }
}

resource "aws_iam_role_policy" "least_privilege" {
  for_each = var.roles

  name   = "${each.key}-least-privilege"
  role   = aws_iam_role.this[each.key].id
  policy = data.aws_iam_policy_document.least_privilege[each.key].json
}
