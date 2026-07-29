variable "roles" {
  description = <<-EOT
    Mapa nome-da-role => estrutura de menor privilégio de referência
    (secret/parameter/KMS ARNs que essa role deveria conseguir acessar).
    Provisiona a estrutura IAM (roles, policies, associações) como
    referência AWS; o enforcement de policy NÃO é comprovável no
    LocalStack Hobby usado neste projeto (ver ADR-0009 e
    docs/security/threat-model.md) - nenhum teste deste projeto afirma
    isolamento negativo garantido por IAM localmente.

    ecr_repository_arns/log_group_arns/sqs_send_queue_arns/sqs_consume_queue_arns
    : permitem reaproveitar este MESMO módulo genérico tanto
    para task roles de aplicação (secrets/parameters/kms/sqs) quanto para
    task EXECUTION roles (ecr pull + logs + secrets/parameters/kms, já que
    é a execution role - nunca a task role - quem resolve os 'secrets' da
    container definition) - nunca uma role ampla compartilhada entre os 4
    workloads (cada chave deste mapa é uma role independente com ARNs
    próprios).

    sqs_send_queue_arns vs sqs_consume_queue_arns (auditoria de
    fechamento pré-push, ver ADR-0009 - "permissões IAM distintas
    para Publisher (send) e Worker (receive/delete)"): antes desta
    correção, um único "sqs_queue_arns" concedia SendMessage +
    ReceiveMessage + DeleteMessage + ChangeMessageVisibility
    indistintamente a QUALQUER role que o usasse - Ledger.OutboxPublisher
    (que só publica) e Consolidation.Worker (que só consome) recebiam o
    MESMO conjunto de ações, violando o próprio texto da ADR. Corrigido
    separando em dois campos com ações realmente distintas.
  EOT
  type = map(object({
    secret_arns            = optional(list(string), [])
    parameter_arns         = optional(list(string), [])
    kms_key_arns           = optional(list(string), [])
    ecr_repository_arns    = optional(list(string), [])
    log_group_arns         = optional(list(string), [])
    sqs_send_queue_arns    = optional(list(string), [])
    sqs_consume_queue_arns = optional(list(string), [])
  }))
}

variable "assume_role_service_principal" {
  description = "Service principal autorizado a assumir estas roles (referência: ECS Fargate task role, ver infra/README.md)."
  type        = string
  default     = "ecs-tasks.amazonaws.com"
}

variable "tags" {
  type    = map(string)
  default = {}
}
