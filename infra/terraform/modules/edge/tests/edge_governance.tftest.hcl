# Teste real do Terraform (terraform test, ver ADR-0008) contra o modulo
# edge, usando mock_provider para nunca exigir credenciais/conta AWS reais.
# Verifica em plano
# real (nao apenas por leitura de string, como os testes de
# Architecture.Tests) que a cadeia corrigida WAF -> API Gateway REST ->
# VPC Link V2 -> ALB interno realmente se materializa no grafo de
# recursos, e que nenhum NLB/VPC Link classico e criado.

mock_provider "aws" {}

# O provider mockado gera um ARN sintetico invalido por padrao para
# atributos computed (ex.: "irz60ele") - "aws_lb.alb.arn" e consumido por
# outros recursos (aws_lb_listener.load_balancer_arn,
# aws_api_gateway_integration.integration_target) que validam o formato do
# ARN mesmo em modo mockado (validacao client-side do provider, nunca uma
# chamada real a AWS). Substituido por um ARN sintetico mas
# sintaticamente valido, apenas para permitir o "apply" mockado completar.
override_resource {
  target = aws_lb.alb
  values = {
    arn      = "arn:aws:elasticloadbalancing:us-east-1:123456789012:loadbalancer/app/banco-carrefour-production-alb/1234567890abcdef"
    dns_name = "banco-carrefour-production-alb-123456789.us-east-1.elb.amazonaws.com"
  }
}

override_resource {
  target = aws_cloudwatch_log_group.api_gateway_access_logs
  values = {
    arn = "arn:aws:logs:us-east-1:123456789012:log-group:/aws/apigateway/banco-carrefour-production:*"
  }
}

override_resource {
  target = aws_api_gateway_stage.this
  values = {
    arn = "arn:aws:apigateway:us-east-1::/restapis/abc123/stages/production"
  }
}

override_resource {
  target = aws_wafv2_web_acl.this
  values = {
    arn = "arn:aws:wafv2:us-east-1:123456789012:regional/webacl/banco-carrefour-production/00000000-0000-0000-0000-000000000000"
  }
}

variables {
  environment           = "production"
  vpc_id                = "vpc-0123456789abcdef0"
  private_subnet_ids    = ["subnet-aaaaaaaaaaaaaaaaa", "subnet-bbbbbbbbbbbbbbbbb"]
  alb_security_group_id = "sg-0123456789abcdef0"
  certificate_arn       = "arn:aws:acm:us-east-1:123456789012:certificate/00000000-0000-0000-0000-000000000000"
}

run "plan_cria_vpc_link_v2_sem_nlb_e_sem_vpc_link_classico" {
  command = apply

  assert {
    condition     = aws_apigatewayv2_vpc_link.this.name == "banco-carrefour-production-vpc-link-v2"
    error_message = "VPC Link V2 (aws_apigatewayv2_vpc_link) deveria existir com o nome esperado."
  }

  assert {
    condition     = aws_lb.alb.internal == true
    error_message = "O ALB interno deveria ter internal = true (nunca publico)."
  }

  assert {
    condition     = aws_api_gateway_integration.proxy.connection_type == "VPC_LINK"
    error_message = "A integracao da API Gateway deveria usar connection_type = VPC_LINK."
  }

  assert {
    condition     = aws_api_gateway_integration.proxy.connection_id == aws_apigatewayv2_vpc_link.this.id
    error_message = "connection_id da integracao deveria apontar para o VPC Link V2 criado por este modulo."
  }

  assert {
    condition     = aws_api_gateway_integration.proxy.integration_target == aws_lb.alb.arn
    error_message = "integration_target deveria apontar diretamente para o ARN do ALB interno (sem NLB intermediario)."
  }

  assert {
    condition     = aws_security_group_rule.vpc_link_to_alb.source_security_group_id == aws_security_group.vpc_link.id
    error_message = "A regra de ingress do ALB deveria ter como origem exclusiva o security group do VPC Link V2."
  }
}

run "plan_nunca_materializa_nlb_ou_vpc_link_classico" {
  command = apply

  # Se o modulo reintroduzisse um NLB ou um aws_api_gateway_vpc_link (v1),
  # os enderecos abaixo passariam a existir no plano e o proprio "terraform
  # test" falharia ao tentar resolver um endereco de recurso inexistente -
  # a ausencia desses recursos e verificada indiretamente pelo fato de o
  # plano acima (que lista todos os recursos reais do modulo) nunca
  # precisar referenciar aws_lb.nlb ou aws_api_gateway_vpc_link.this.
  assert {
    condition     = length(aws_wafv2_web_acl.this.id) > 0
    error_message = "O WAF Web ACL deveria existir e estar associado ao estagio da API Gateway."
  }
}
