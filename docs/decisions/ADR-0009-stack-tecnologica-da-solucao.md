---
adr_id: ADR-0009
titulo: Stack Tecnológica da Solução
status: Aceita
data: 2026-07-10
responsavel: Arquitetura de Soluções
decisao_relacionada: Tecnologias de implementação de referência
---

# ADR-0009 — Stack Tecnológica da Solução

## 1. Contexto

A solução precisa materializar APIs HTTP, workers assíncronos, persistência transacional, mensageria, contratos de API, execução local e observabilidade básica.

A arquitetura já definiu fronteiras separadas entre Lançamentos e Consolidado, uso de Outbox, consumo at-least-once com idempotência, projeção materializada e persistências independentes.

A implementação do desafio precisa ser simples de executar localmente, compreensível para avaliação técnica e compatível com a AWS como plataforma de referência do case.

A stack tecnológica deve apoiar:

```text
- APIs para registro e consulta
- workers para publicação e consumo assíncrono
- transações locais
- banco relacional
- mensageria local
- contratos HTTP documentados
- empacotamento em containers
- execução local reproduzível
- observabilidade mínima para diagnóstico
```

---

## 2. Decisão

A solução adotará a seguinte stack de aplicação:

| Camada | Tecnologia de referência | Papel |
|---|---|---|
| Backend | .NET em versão LTS | Implementação das APIs e workers. |
| APIs HTTP | ASP.NET Core | Exposição dos endpoints de Lançamentos e Consolidado. |
| Workers | .NET Worker Service | Execução do Outbox Publisher e do consumidor do Consolidado. |
| Persistência | PostgreSQL | Armazenamento transacional, Outbox, idempotência e projeções. |
| Mensageria local | RabbitMQ | Canal assíncrono de referência para execução local do desafio. |
| Containers | Docker | Empacotamento das unidades implantáveis. |
| Execução local | Docker Compose | Orquestração local para avaliação e testes. |
| Contratos | OpenAPI | Documentação dos contratos HTTP. |
| Contrato de evento | JSON Schema | Validação do evento `EntryCreated.v1`. |
| Observabilidade | OpenTelemetry | Logs, métricas e traces vendor-neutral. |

A solução adotará a seguinte stack de plataforma AWS de referência:

| Camada | Tecnologia de referência | Papel |
|---|---|---|
| Runtime | Amazon ECS Fargate | Execução de APIs e workers. |
| Imagens | Amazon ECR | Registry de imagens versionadas. |
| Banco de dados | Amazon RDS for PostgreSQL | Persistências separadas de Ledger e Consolidation. |
| Mensageria | Amazon SQS Standard com DLQ | Canal assíncrono para `EntryCreated.v1`. |
| Exposição HTTP | Amazon API Gateway ou ALB com AWS WAF | Entrada HTTP protegida. |
| Secrets e parâmetros | AWS Secrets Manager e/ou SSM Parameter Store | Configuração sensível por ambiente. |
| Criptografia | AWS KMS | Chaves gerenciadas para criptografia. |
| Observabilidade | ADOT, CloudWatch e X-Ray | Logs, métricas, alarmes e tracing. |
| IaC | Terraform | Provisionamento da infraestrutura. |
| CI/CD | GitHub Actions com OIDC | Build/test, publicação no ECR e deploy no ECS. |

A versão exata do .NET deve seguir uma versão LTS vigente no momento da implementação.

A escolha de PostgreSQL é detalhada no ADR-0006.

A escolha de RabbitMQ como referência local de mensageria é detalhada no ADR-0007.

A organização das unidades implantáveis é detalhada no ADR-0008.

---

## 3. O que a decisão inclui

Esta decisão inclui:

```text
- .NET LTS como plataforma de backend
- ASP.NET Core para APIs HTTP
- .NET Worker Service para processamento assíncrono
- PostgreSQL como banco relacional de referência
- RabbitMQ como broker local de referência
- Docker para empacotamento
- Docker Compose para execução local
- OpenAPI para documentação dos contratos
- OpenTelemetry como base de observabilidade
- serviços AWS de referência para runtime, imagens, dados, mensageria, segurança, observabilidade, IaC e CI/CD
```

---

## 4. O que fica fora desta decisão

Esta decisão não define:

```text
- conta, região, landing zone e sizing AWS
- pipeline executado de deploy produtivo
- módulos Terraform funcionais aplicados
- padrão real de autenticação corporativa
- service mesh, API gateway ou ingress definitivo
- configuração final de escalabilidade e alta disponibilidade
```

Esses pontos são detalhados na arquitetura alvo, segurança, operação, ADR-0010 e ADR-0015.

---

## 5. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar como base da solução |
|---|---|---|
| .NET LTS, ASP.NET Core e Worker Service | Stack para APIs e workers usando a mesma plataforma de backend. | Alternativa adotada. Atende bem ao escopo, reduz dispersão tecnológica e facilita implementação, testes e execução local. |
| Java e Spring Boot | Stack madura para APIs, workers e sistemas corporativos. | Também seria adequada, mas aumentaria troca de stack em relação à decisão de referência adotada para o desafio. |
| Node.js | Stack adequada para APIs e serviços leves. | Poderia atender partes do escopo, mas a solução exige consistência transacional, workers e organização backend onde .NET LTS oferece bom equilíbrio para o case. |
| Serverless como implementação principal | APIs e processamento assíncrono seriam materializados por funções gerenciadas. | Pode ser adequado em cloud, mas aumenta dependência de provedor e dificulta execução local fiel no escopo do desafio. |
| Stack poliglota | Cada componente poderia usar tecnologia diferente. | Aumenta complexidade de build, testes, observabilidade e operação sem necessidade para o escopo inicial. |

---

## 6. Consequências

Consequências positivas:

```text
- reduz dispersão tecnológica entre APIs e workers
- facilita compartilhamento de padrões de código, logging, configuração e testes
- permite execução local com containers
- simplifica onboarding e avaliação técnica
- mantém boa compatibilidade com ambientes corporativos e cloud
- permite evolução para serviços gerenciados sem alterar o papel arquitetural dos componentes
```

Consequências e tradeoffs:

```text
- concentra a implementação em uma stack principal
- exige gestão de versão LTS e atualização futura do runtime
- exige disciplina de configuração por ambiente
- exige empacotamento e health checks por unidade implantável
- não elimina decisões futuras de plataforma, segurança, observabilidade e deploy
```

---

## 7. Relação com requisitos, ABBs e SBBs

Esta decisão sustenta principalmente:

```text
- ASR-001: Lançamentos deve continuar disponível mesmo se Consolidado falhar
- ASR-002: Consolidado deve suportar 50 RPS em pico
- ASR-003: Consolidado deve limitar falhas ou perdas de requisições a no máximo 5% no pico
- ASR-010: O fluxo de registro, publicação, consumo e consolidação deve ser observável
- ASR-011: Falhas de publicação, consumo ou consolidação devem ser recuperáveis
- ABB-001: Fronteira de Lançamentos
- ABB-006: Publicação Recuperável
- ABB-008: Fronteira de Consolidado
- ABB-012: API de Consulta do Consolidado
- ABB-013: Observabilidade do Fluxo
- ABB-014: Recuperação Operacional
- SBB-001: Ledger.Api
- SBB-006: Ledger.OutboxPublisher
- SBB-008: Consolidation.Worker
- SBB-012: Consolidation.Api
- SBB-013: API Contracts
- SBB-016: Observability
- SBB-018: Containers and Local Runtime
- SBB-019: Configuration and Secrets
```

---

## 8. Relação com documentos

Esta decisão sustenta:

```text
- docs/architecture/04-blocos-de-solucao.md
- docs/architecture/05-arquitetura-da-solucao.md
- docs/architecture/06-diagramas.md
- docs/security/arquitetura-de-seguranca.md
- docs/operations/arquitetura-operacional.md
- docs/operations/observabilidade-sli-slo-e-recuperacao.md
```

---

## 9. Status

Decisão aceita para o escopo inicial da solução.
