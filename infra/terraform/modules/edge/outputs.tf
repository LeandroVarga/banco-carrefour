output "alb_arn" {
  value = aws_lb.alb.arn
}

output "alb_https_listener_arn" {
  description = "ARN do listener HTTPS do ALB interno - usado por cada ecs-service-api para criar sua própria listener_rule por path (ex.: /ledger/*, /consolidation/*)."
  value       = aws_lb_listener.https.arn
}

output "alb_dns_name" {
  value = aws_lb.alb.dns_name
}

output "alb_arn_suffix" {
  description = "Formato app/<lb>/<lb-id> - usado como dimensão LoadBalancer em widgets de dashboard/alarme de ALB."
  value       = aws_lb.alb.arn_suffix
}

output "api_gateway_invoke_url" {
  value = aws_api_gateway_stage.this.invoke_url
}

output "waf_web_acl_arn" {
  value = aws_wafv2_web_acl.this.arn
}

output "vpc_link_id" {
  description = "ID do VPC Link V2 (aws_apigatewayv2_vpc_link) - conecta a API Gateway REST diretamente ao ALB interno, sem NLB intermediário."
  value       = aws_apigatewayv2_vpc_link.this.id
}
