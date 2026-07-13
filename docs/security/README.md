# Segurança

Esta pasta contém a documentação de segurança da solução.

| Documento | Conteúdo |
|---|---|
| [arquitetura-de-seguranca.md](arquitetura-de-seguranca.md) | Autenticação, autorização por comerciante, proteção de APIs, dados, secrets, comunicação entre serviços, rate limit e diferenças entre execução local e referência AWS. |

Na execução local, alguns controles são simplificados para manter a avaliação reproduzível. Na referência AWS, a documentação aponta para IdP OIDC/OAuth2 ou Cognito quando aplicável, IAM, Secrets Manager/SSM, KMS, VPC/subnets, security groups, WAF, TLS/mTLS onde aplicável, auditoria e menor privilégio.
