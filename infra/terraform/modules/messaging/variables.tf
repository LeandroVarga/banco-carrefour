variable "queue_name" {
  type = string
}

variable "dlq_name" {
  type = string
}

variable "max_receive_count" {
  type    = number
  default = 3
}

variable "visibility_timeout_seconds" {
  type    = number
  default = 5
}

variable "receive_wait_time_seconds" {
  type    = number
  default = 10
}

variable "message_retention_seconds" {
  type    = number
  default = 345600
}

variable "dlq_message_retention_seconds" {
  type    = number
  default = 1209600
}

variable "tags" {
  type    = map(string)
  default = {}
}
