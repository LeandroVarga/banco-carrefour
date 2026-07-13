# Segurança

Esta pasta contém a documentação de segurança da solução.

Documentos:

- `arquitetura-de-seguranca.md`

A documentação de segurança cobre autenticação, autorização, proteção de APIs, controle de acesso entre serviços, secrets, proteção de dados financeiros, comunicação segura, validação de entrada, rate limit e separação entre execução local e AWS como plataforma de referência do case.

Na execução local, alguns controles são simplificados para reprodutibilidade. Na referência AWS, a documentação aponta para IdP OIDC/OAuth2, Cognito quando aplicável, IAM, Secrets Manager/SSM, KMS, VPC/subnets, security groups, WAF, TLS/mTLS, auditoria e menor privilégio.
