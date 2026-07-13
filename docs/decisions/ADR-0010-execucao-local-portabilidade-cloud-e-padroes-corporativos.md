---
adr_id: ADR-0010
titulo: Execução Local, AWS como Plataforma de Referência e Portabilidade por Papéis
status: Aceita
data: 2026-07-10
responsavel: Arquitetura de Soluções
decisao_relacionada: Estratégia de execução local, referência AWS do case e portabilidade por papéis arquiteturais
---

# ADR-0010 - Execução Local, AWS como Plataforma de Referência e Portabilidade por Papéis

## 1. Contexto

O desafio exige uma solução implementável, documentada, testável e executável para avaliação técnica.

A jornada arquitetural separa papéis e materializações:

```text
1. ABBs definem papéis arquiteturais sem tecnologia.
2. SBBs materializam ABBs com componentes, tecnologias e serviços.
3. Na passagem de ABBs para SBBs, a solução escolhe AWS como plataforma de referência do case.
4. A execução local continua existindo como materialização reproduzível.
5. A implantação cloud de referência usa AWS, Terraform, CI/CD, publicação de imagens e serviços gerenciados.
```

A escolha da AWS nesta ADR não afirma que o Banco Carrefour usa AWS. Ela define uma plataforma de referência para o case, mantendo os papéis arquiteturais substituíveis por padrões corporativos equivalentes.

Docker Compose continua necessário para avaliação local reproduzível, mas não representa topologia produtiva.

---

## 2. Decisão

A solução adotará Docker e Docker Compose como estratégia de execução local para o desafio.

Para implantação cloud de referência do case, a solução adotará AWS como plataforma técnica de referência.

A portabilidade será preservada por papéis arquiteturais, não por equivalência literal de produto. Assim, cada ABB continua independente de tecnologia; os SBBs e documentos operacionais indicam a materialização local e a materialização AWS de referência.

Mapeamento de referência:

| Necessidade arquitetural | Execução local | AWS como referência do case |
|---|---|---|
| APIs e workers | Containers Docker via Docker Compose | Amazon ECS Fargate. |
| Imagens | Build local e CI | Amazon ECR. |
| Ledger Database | PostgreSQL em container | Amazon RDS for PostgreSQL. |
| Consolidation Database | PostgreSQL em container | Amazon RDS for PostgreSQL. |
| Mensageria | RabbitMQ em container | Amazon SQS Standard com DLQ para `EntryCreated.v1`. |
| Autenticação | JWT local HS256 | IdP corporativo via OIDC/OAuth2, com Amazon Cognito como referência possível. |
| Secrets e parâmetros | Variáveis de ambiente locais | AWS Secrets Manager e/ou SSM Parameter Store. |
| Criptografia | Configuração local | AWS KMS. |
| Observabilidade | OpenTelemetry, OTLP e Aspire Dashboard local | ADOT, CloudWatch Logs/Metrics/Alarms e X-Ray. |
| Exposição HTTP | Portas locais | Amazon API Gateway com AWS WAF, VPC Link/private integration e ALB interno. |
| IaC | Docker Compose local | Terraform. |
| CI/CD | GitHub Actions de CI | GitHub Actions com OIDC para AWS, build/test, versionamento, push no ECR e deploy no ECS. |

---

## 3. O que a decisão inclui

Esta decisão inclui:

```text
- Docker como mecanismo de empacotamento local
- Docker Compose como orquestração local do desafio
- containers separados para APIs e workers
- PostgreSQL e RabbitMQ locais para execução reproduzível
- AWS como plataforma cloud de referência do case
- ECS Fargate para APIs e workers
- ECR para imagens
- RDS for PostgreSQL para persistências separadas
- SQS Standard com DLQ para mensageria de referência
- Secrets Manager/SSM, KMS, CloudWatch, X-Ray e ADOT como serviços de referência
- API Gateway com WAF, VPC Link/private integration e ALB interno para exposição HTTP
- Terraform para infraestrutura como código
- GitHub Actions com OIDC para AWS
- portabilidade conceitual por papéis arquiteturais
```

---

## 4. O que fica fora desta decisão

Esta decisão não define:

```text
- conta, organização, região ou landing zone AWS real
- sizing final de ECS, RDS, SQS, CloudWatch ou X-Ray
- política final de rede, VPC, subnets, NAT, endpoints privados ou conectividade corporativa
- RTO, RPO, HA e DR definitivos
- quotas, reservas, savings plans ou modelo comercial
- política real de identidade corporativa
- parâmetros finais de autoscaling
- pipeline executado de deploy produtivo
- Terraform funcional aplicado em uma conta AWS
```

CI/CD, publicação de imagens e Terraform como decisão de entrega são registrados na ADR-0015.

---

## 5. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar como base da solução |
|---|---|---|
| Execução manual local | Executar APIs, workers, banco e broker manualmente na máquina do avaliador. | Aumenta variação de ambiente, dificulta reprodução e torna a avaliação mais frágil. |
| Docker Compose para execução local | Executar unidades e dependências em containers coordenados localmente. | Alternativa adotada para avaliação local reproduzível. |
| Kubernetes local como base do desafio | Usar cluster local para simular produção. | Aumenta complexidade de instalação e avaliação sem necessidade para o case. |
| AWS como plataforma de referência do case | Mapear papéis arquiteturais para serviços AWS sem exigir conta cloud para avaliação local. | Alternativa adotada para demonstrar implantação cloud coerente e rastreável. |
| Cloud específica como requisito real do banco | Tratar AWS como plataforma obrigatória do Banco Carrefour. | Não adotada, porque o case não autoriza afirmar plataforma real da organização. |
| Serverless como execução principal | Usar funções e serviços gerenciados como runtime principal. | Pode ser adequado em determinados ambientes, mas não preserva a mesma topologia de APIs e workers escolhida para o case. |

---

## 6. Consequências

Consequências positivas:

```text
- torna a solução executável localmente
- reduz dependência de conta AWS para avaliação
- explicita uma implantação cloud de referência
- torna a passagem ABB -> SBB -> AWS rastreável
- permite discutir custo, segurança, operação, observabilidade e deploy com serviços concretos
- preserva substituição por padrões corporativos equivalentes sem alterar papéis arquiteturais
- evita tratar RabbitMQ e Docker Compose como produção
```

Consequências e tradeoffs:

```text
- a referência AWS ainda exige validação real antes de produção
- Terraform, deploy e publicação de imagens precisam ser implementados e aplicados em ambiente controlado para virar evidência executada
- SQS altera detalhes operacionais em relação ao RabbitMQ local, especialmente ack, visibilidade, redrive e DLQ
- custos dependem de região, sizing, retenção, tráfego, suporte e políticas comerciais
- segurança local continua simplificada em relação à referência AWS
```

---

## 7. Relação com requisitos, ABBs e SBBs

Esta decisão sustenta principalmente:

```text
- ASR-002: Consolidado deve suportar 50 RPS em pico
- ASR-003: Consolidado deve limitar falhas ou perdas de requisições a no máximo 5% no pico
- ASR-009: O acesso aos dados deve respeitar o comerciante autenticado e autorizado
- ASR-010: O fluxo deve ser observável
- ASR-011: Falhas de publicação, consumo ou consolidação devem ser recuperáveis
- ABB-012: API de Consulta do Consolidado
- ABB-013: Observabilidade do Fluxo
- ABB-014: Recuperação Operacional
- ABB-015: Segurança de Acesso
- ABB-016: Controle de Comunicação entre Serviços
- SBB-001: Ledger.Api
- SBB-002: Ledger Database
- SBB-006: Ledger.OutboxPublisher
- SBB-007: Message Broker
- SBB-008: Consolidation.Worker
- SBB-009: Consolidation Database
- SBB-012: Consolidation.Api
- SBB-014: Authentication and Authorization
- SBB-015: Service-to-Service Security
- SBB-016: Observability
- SBB-017: Operational Recovery
- SBB-018: Containers and Local Runtime
- SBB-019: Configuration and Secrets
```

---

## 8. Relação com documentos

Esta decisão sustenta:

- [04-blocos-de-solucao.md](../architecture/04-blocos-de-solucao.md)
- [05-arquitetura-da-solucao.md](../architecture/05-arquitetura-da-solucao.md)
- [06-diagramas.md](../architecture/06-diagramas.md)
- [arquitetura-de-seguranca.md](../security/arquitetura-de-seguranca.md)
- [arquitetura-operacional.md](../operations/arquitetura-operacional.md)
- [observabilidade-sli-slo-e-recuperacao.md](../operations/observabilidade-sli-slo-e-recuperacao.md)
- [estimativa-de-custos.md](../operations/estimativa-de-custos.md)
- [runbook-implantacao-aws.md](../operations/runbook-implantacao-aws.md)
- [infra/README.md](../../infra/README.md)

---

## 9. Status

Decisão aceita como ponto de passagem entre SBBs e implantação cloud de referência do case.
