# Borda de referência (ver ADR-0008 - "VPC Link V2
# direto ao ALB interno"): client -> AWS WAF -> API Gateway REST (endpoint
# regional público) -> VPC Link V2 -> ALB interno -> ECS/Fargate.
#
# Correção real (auditoria pré-push): a decisão original deste módulo
# assumia que a API Gateway REST v1 só suportava o VPC Link CLÁSSICO (v1,
# exige um NLB), com base na observação de que o comando
# "aws apigatewayv2 create-vpc-link" pertence ao plano de controle
# apigatewayv2 - uma inferência incorreta (o RECURSO VpcLinkV2 é criado
# via esse plano de controle compartilhado, mas é referenciável tanto por
# integrações de REST API quanto por HTTP API). Confirmado via
# documentação oficial atual da AWS
# (apigateway/latest/developerguide/private-integration.html): "API
# Gateway supports VPC links V2 for REST APIs. VPC links V2 let you create
# private integrations that connect your REST API to Application Load
# Balancers WITHOUT using a Network Load Balancer... VPC links V1 are
# considered a LEGACY integration type... we recommend that you don't
# create new VPC links V1." Confirmado também no schema real e atual do
# provider Terraform (aws_api_gateway_integration.integration_target,
# aws_apigatewayv2_vpc_link) via WebFetch contra a fonte oficial do
# provider (Terraform MCP configurado mas inalcançável nesta sessão,
# mesma limitação já registrada no ADR-0008).
#
# Restrição de mesma conta (confirmada na mesma página oficial: "All
# resources must be owned by the same AWS account. This includes the
# load balancer, VPC link and REST API."): a API Gateway REST, o VPC Link
# V2 e o ALB interno deste módulo pertencem SEMPRE à MESMA conta de
# workload (Development, Staging ou Production) - nunca a conta Artifacts
# nem uma referência cross-account. A conta Artifacts permanece central
# apenas para o Amazon ECR (ver ADR-0013/ADR-0013), nunca para recursos de
# borda.
#
# O NLB intermediário e o VPC Link clássico (v1) foram removidos - nunca
# retidos "porque já estavam implementados". RDS e as tasks ECS nunca são
# alcançáveis fora desta cadeia (security groups em camadas).
locals {
  common_tags = merge(var.tags, {
    project     = "banco-carrefour"
    environment = var.environment
    managed_by  = "terraform"
  })
}

# --- ALB interno: alvo real dos target groups por serviço (canary/blue-green nativo do ECS) - retido porque a estratégia de deployment nativa do ECS exige um Application Load Balancer, nunca por inércia ---
resource "aws_lb" "alb" {
  name               = "banco-carrefour-${var.environment}-alb"
  internal           = true
  load_balancer_type = "application"
  subnets            = var.private_subnet_ids
  security_groups    = [var.alb_security_group_id]

  drop_invalid_header_fields = true

  access_logs {
    bucket  = "" # Reservado: bucket de logs é responsabilidade da conta Log Archive compartilhada (ver docs/decisions) - desabilitado por padrão até esse ARN existir.
    enabled = false
  }

  tags = merge(local.common_tags, { Name = "banco-carrefour-${var.environment}-alb" })
}

resource "aws_lb_listener" "https" {
  load_balancer_arn = aws_lb.alb.arn
  port              = 443
  protocol          = "HTTPS"
  ssl_policy        = "ELBSecurityPolicy-TLS13-1-2-2021-06"
  certificate_arn   = var.certificate_arn

  # Falha fechado por padrao: qualquer path sem uma listener_rule
  # explicita de um servico (ecs-service-api) recebe 503, nunca roteia
  # implicitamente para um alvo default.
  default_action {
    type = "fixed-response"
    fixed_response {
      content_type = "text/plain"
      message_body = "no route configured"
      status_code  = "503"
    }
  }

  tags = local.common_tags
}

# --- VPC Link V2: conecta a API Gateway REST diretamente ao ALB interno, sem NLB intermediário ---
resource "aws_security_group" "vpc_link" {
  name_prefix = "banco-carrefour-${var.environment}-vpclink-"
  description = "ENIs do VPC Link V2 - egress somente para o ALB interno na porta 443, sem ingress (o VPC Link nunca recebe conexoes, so origina)."
  vpc_id      = var.vpc_id

  egress {
    description     = "HTTPS para o ALB interno."
    from_port       = 443
    to_port         = 443
    protocol        = "tcp"
    security_groups = [var.alb_security_group_id]
  }

  tags = merge(local.common_tags, { Name = "banco-carrefour-${var.environment}-vpclink-sg" })

  lifecycle {
    create_before_destroy = true
  }
}

resource "aws_apigatewayv2_vpc_link" "this" {
  name               = "banco-carrefour-${var.environment}-vpc-link-v2"
  security_group_ids = [aws_security_group.vpc_link.id]
  subnet_ids         = var.private_subnet_ids

  tags = local.common_tags
}

# Regra de ingress do ALB (SG criado pelo modulo network, sem nenhum
# ingress declarado la de proposito) - so pode existir aqui, depois que
# este modulo cria o security group do VPC Link V2. Substitui o antigo
# ingress por CIDR amplo da VPC por uma regra precisa (somente a partir
# do SG do VPC Link).
resource "aws_security_group_rule" "vpc_link_to_alb" {
  type                     = "ingress"
  security_group_id        = var.alb_security_group_id
  source_security_group_id = aws_security_group.vpc_link.id
  from_port                = 443
  to_port                  = 443
  protocol                 = "tcp"
  description              = "HTTPS a partir do VPC Link V2 (unico caminho de entrada do ALB interno - nunca a internet, nunca um CIDR amplo da VPC)."
}

# --- API Gateway REST (endpoint regional publico, unico ponto de entrada) ---
resource "aws_api_gateway_rest_api" "this" {
  name = "banco-carrefour-${var.environment}"

  endpoint_configuration {
    types = ["REGIONAL"]
  }

  tags = local.common_tags
}

resource "aws_api_gateway_resource" "proxy" {
  rest_api_id = aws_api_gateway_rest_api.this.id
  parent_id   = aws_api_gateway_rest_api.this.root_resource_id
  path_part   = "{proxy+}"
}

resource "aws_api_gateway_method" "proxy_any" {
  rest_api_id      = aws_api_gateway_rest_api.this.id
  resource_id      = aws_api_gateway_resource.proxy.id
  http_method      = "ANY"
  authorization    = "NONE" # Autenticacao/autorizacao real e feita pela aplicacao (Keycloak/OIDC, ADR-0007/0022) - a API Gateway aqui e roteamento privado, nao um autorizador de identidade.
  api_key_required = false

  request_parameters = {
    "method.request.path.proxy" = true
  }
}

resource "aws_api_gateway_integration" "proxy" {
  rest_api_id             = aws_api_gateway_rest_api.this.id
  resource_id             = aws_api_gateway_resource.proxy.id
  http_method             = aws_api_gateway_method.proxy_any.http_method
  type                    = "HTTP_PROXY"
  integration_http_method = "ANY"
  connection_type         = "VPC_LINK"
  connection_id           = aws_apigatewayv2_vpc_link.this.id
  # integration_target: ARN do ALB - o mecanismo REAL de roteamento do VPC
  # Link V2 (nunca "uri", que aqui serve apenas para o header Host e
  # validacao de certificado - ver AWS Documentation MCP,
  # set-up-private-integration.html, confirmado no schema oficial e atual
  # do provider Terraform via WebFetch antes de implementar).
  integration_target = aws_lb.alb.arn
  uri                = "https://${aws_lb.alb.dns_name}/{proxy}"

  request_parameters = {
    "integration.request.path.proxy" = "method.request.path.proxy"
  }
}

resource "aws_cloudwatch_log_group" "api_gateway_access_logs" {
  name              = "/aws/apigateway/banco-carrefour-${var.environment}"
  retention_in_days = var.log_retention_days
  tags              = local.common_tags
}

resource "aws_api_gateway_deployment" "this" {
  rest_api_id = aws_api_gateway_rest_api.this.id

  triggers = {
    redeployment = sha1(jsonencode([
      aws_api_gateway_resource.proxy.id,
      aws_api_gateway_method.proxy_any.id,
      aws_api_gateway_integration.proxy.id,
    ]))
  }

  lifecycle {
    create_before_destroy = true
  }
}

resource "aws_api_gateway_stage" "this" {
  deployment_id = aws_api_gateway_deployment.this.id
  rest_api_id   = aws_api_gateway_rest_api.this.id
  stage_name    = var.environment

  access_log_settings {
    destination_arn = aws_cloudwatch_log_group.api_gateway_access_logs.arn
    format = jsonencode({
      requestId      = "$context.requestId"
      ip             = "$context.identity.sourceIp"
      httpMethod     = "$context.httpMethod"
      resourcePath   = "$context.resourcePath"
      status         = "$context.status"
      responseLength = "$context.responseLength"
      requestTime    = "$context.requestTime"
    })
  }

  tags = local.common_tags
}

resource "aws_api_gateway_method_settings" "this" {
  rest_api_id = aws_api_gateway_rest_api.this.id
  stage_name  = aws_api_gateway_stage.this.stage_name
  method_path = "*/*"

  settings {
    logging_level          = "INFO"
    data_trace_enabled     = false # Nunca logar corpo de requisicao/resposta (pode conter dados de merchant) - so metadados via access_log_settings.
    metrics_enabled        = true
    throttling_rate_limit  = 200
    throttling_burst_limit = 100
  }
}

# --- WAF: rate-limiting + conjunto de regras gerenciadas pela AWS ---
resource "aws_wafv2_web_acl" "this" {
  name  = "banco-carrefour-${var.environment}"
  scope = "REGIONAL"

  default_action {
    allow {}
  }

  rule {
    name     = "RateLimitPerIp"
    priority = 1

    action {
      block {}
    }

    statement {
      rate_based_statement {
        limit              = var.waf_rate_limit_per_5min
        aggregate_key_type = "IP"
      }
    }

    visibility_config {
      cloudwatch_metrics_enabled = true
      metric_name                = "banco-carrefour-${var.environment}-rate-limit"
      sampled_requests_enabled   = true
    }
  }

  rule {
    name     = "AWSManagedRulesCommonRuleSet"
    priority = 2

    override_action {
      none {}
    }

    statement {
      managed_rule_group_statement {
        name        = "AWSManagedRulesCommonRuleSet"
        vendor_name = "AWS"
      }
    }

    visibility_config {
      cloudwatch_metrics_enabled = true
      metric_name                = "banco-carrefour-${var.environment}-common-rule-set"
      sampled_requests_enabled   = true
    }
  }

  rule {
    name     = "AWSManagedRulesKnownBadInputsRuleSet"
    priority = 3

    override_action {
      none {}
    }

    statement {
      managed_rule_group_statement {
        name        = "AWSManagedRulesKnownBadInputsRuleSet"
        vendor_name = "AWS"
      }
    }

    visibility_config {
      cloudwatch_metrics_enabled = true
      metric_name                = "banco-carrefour-${var.environment}-known-bad-inputs"
      sampled_requests_enabled   = true
    }
  }

  visibility_config {
    cloudwatch_metrics_enabled = true
    metric_name                = "banco-carrefour-${var.environment}"
    sampled_requests_enabled   = true
  }

  tags = local.common_tags
}

resource "aws_wafv2_web_acl_association" "this" {
  resource_arn = aws_api_gateway_stage.this.arn
  web_acl_arn  = aws_wafv2_web_acl.this.arn
}
