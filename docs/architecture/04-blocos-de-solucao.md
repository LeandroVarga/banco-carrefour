---
doc_id: ARCH-004
titulo: Blocos de Solução
versao: 1.0
status: Atualizado
responsavel: Arquitetura de Soluções
ultima_atualizacao: 2026-07-10
etapa_relacionada: Definition and Decision
---

# Blocos de Solução

## 1. Objetivo

Este documento define os SBBs, Solution Building Blocks, utilizados para materializar os blocos de arquitetura definidos em `03-blocos-de-arquitetura.md`.

Os SBBs descrevem componentes, tecnologias, bancos, filas, APIs, workers e mecanismos concretos que compõem a solução.

A arquitetura completa da solução será consolidada em `05-arquitetura-da-solucao.md`.

---

## 2. Diretrizes de materialização

A definição dos SBBs segue as decisões arquiteturais já registradas.

Decisões fundacionais:

```text
ADR-0000 -> semântica financeira e data de negócio
ADR-0001 -> fronteiras Ledger e Consolidation
ADR-0004 -> integração assíncrona confiável (Outbox, SQS, consumo idempotente)
```

Decisões de materialização:

```text
ADR-0002 -> persistência PostgreSQL independente por fronteira
ADR-0003 -> arquitetura hexagonal e direção das dependências
ADR-0006 -> unidades implantáveis e topologia de runtime
ADR-0010 -> execução local e paridade comportamental
```

Essas decisões orientam as seguintes diretrizes:

```text
- manter Lançamentos e Consolidado como fronteiras separadas
- proteger o caminho crítico de registro financeiro
- tratar Lançamentos como fonte de verdade financeira
- tratar Consolidado como visão derivada e reconstruível
- evitar dependência síncrona do Consolidado no registro de Lançamentos
- usar comunicação assíncrona entre as fronteiras
- permitir publicação, consumo e reprocessamento recuperáveis
- tratar duplicidade de entrada e duplicidade de consumo
- preparar o Consolidado para leitura por comerciante e data
- manter observabilidade do fluxo fim a fim
- preservar portabilidade para ambiente cloud ou plataforma corporativa
```

---

## 3. Stack de referência

A stack abaixo representa a materialização de referência para o desafio.

| Camada | Stack de aplicação | Materialização local | Materialização AWS de referência |
|---|---|---|---|
| Backend | .NET LTS | Containers Docker | ECS Fargate. |
| APIs HTTP | ASP.NET Core | Portas locais no Compose | API Gateway com AWS WAF, VPC Link/private integration e ALB interno para ECS Fargate. |
| Workers | .NET Worker Service | Containers Docker | ECS Fargate. |
| Imagens | Docker | Build local/CI | Amazon ECR. |
| Persistência | PostgreSQL | PostgreSQL em container | Amazon RDS for PostgreSQL. |
| Mensageria | Canal assíncrono confiável | LocalStack SQS Standard com DLQ | Amazon SQS Standard com DLQ. |
| Contratos | OpenAPI e JSON Schema | Arquivos versionados | Independentes da infraestrutura. |
| Observabilidade | OpenTelemetry | OTLP e Aspire Dashboard | ADOT, CloudWatch e X-Ray. |
| Configuração e secrets | Configuração por ambiente | Variáveis locais | Secrets Manager e/ou SSM Parameter Store. |
| Criptografia | Chaves por ambiente | Configuração local | AWS KMS. |
| IaC | Infraestrutura como código | Docker Compose | Terraform. |
| CI/CD | GitHub Actions | CI container-first | OIDC para AWS, push no ECR e deploy no ECS. |

As escolhas de persistência, mensageria, unidades implantáveis, stack tecnológica, execução local e portabilidade são sustentadas pelos ADRs de materialização. Decisões específicas de segurança e observabilidade serão detalhadas em documentos próprios.

---

## 4. Unidades implantáveis

A solução é composta por quatro unidades implantáveis principais.

| Unidade | Tipo | Responsabilidade |
|---|---|---|
| Ledger.Api | API | Registrar lançamentos financeiros. |
| Ledger.OutboxPublisher | Worker | Publicar eventos pendentes da Outbox. |
| Consolidation.Worker | Worker | Consumir eventos e atualizar a projeção consolidada. |
| Consolidation.Api | API | Consultar o consolidado diário. |

Essa separação permite operar, escalar e recuperar partes do fluxo de forma independente.

---

## 5. SBBs principais

| ID | SBB | ABB relacionado ou papel arquitetural | Materialização local | Materialização AWS de referência | Responsabilidade principal |
|---|---|---|---|---|---|
| SBB-001 | Ledger.Api | ABB-001, Fronteira de Lançamentos | Container no Docker Compose local | ECS Fargate com imagem no ECR | Receber, validar e registrar lançamentos. |
| SBB-002 | Ledger Database | ABB-002, ABB-003 | PostgreSQL container | RDS for PostgreSQL | Armazenar lançamentos, idempotência e Outbox. |
| SBB-003 | Entries | Lançamentos financeiros | Tabela no PostgreSQL local do Ledger | Tabela no RDS PostgreSQL do Ledger | Persistir créditos e débitos registrados. |
| SBB-004 | Input Idempotency | ABB-004 | Tabela no PostgreSQL local do Ledger | Tabela no RDS PostgreSQL do Ledger | Controlar requisições repetidas. |
| SBB-005 | Outbox | ABB-005 | Tabela no PostgreSQL local do Ledger | Tabela no RDS PostgreSQL do Ledger | Registrar eventos pendentes de publicação. |
| SBB-006 | Ledger.OutboxPublisher | ABB-006 | Container no Docker Compose local | ECS Fargate com imagem no ECR | Publicar eventos da Outbox no canal assíncrono. |
| SBB-007 | Message Broker | ABB-007 | LocalStack SQS Standard com DLQ | SQS Standard com DLQ | Transportar eventos entre Lançamentos e Consolidado. |
| SBB-008 | Consolidation.Worker | ABB-008, ABB-009 | Container no Docker Compose local | ECS Fargate com imagem no ECR | Processar eventos e atualizar projeções. |
| SBB-009 | Consolidation Database | ABB-011 | PostgreSQL container | RDS for PostgreSQL | Armazenar projeções e eventos processados. |
| SBB-010 | Processed Events | ABB-009 | Tabela no PostgreSQL local do Consolidado | Tabela no RDS PostgreSQL do Consolidado | Evitar duplicidade de efeito no Consolidado. |
| SBB-011 | DailyBalance | ABB-010 | Tabela no PostgreSQL local do Consolidado | Tabela no RDS PostgreSQL do Consolidado | Armazenar o consolidado diário por comerciante e data. |
| SBB-012 | Consolidation.Api | ABB-012 | Container no Docker Compose local | ECS Fargate com imagem no ECR | Expor consulta do relatório diário consolidado. |
| SBB-013 | API Contracts | Contratos de integração | OpenAPI e JSON Schema versionados | Contratos independentes de ECS, RDS ou SQS | Padronizar endpoints, payloads e respostas. |
| SBB-014 | Authentication and Authorization | ABB-015 | Keycloak real, OIDC/RS256, discovery e JWKS (ADR-0007) | IdP OIDC/OAuth2 corporativo ou gerenciado | Proteger APIs e acesso por comerciante. |
| SBB-015 | Service-to-Service Security | ABB-016 | edge-proxy real HTTPS/WAF (ModSecurity/OWASP CRS), PostgreSQL de menor privilégio por componente (ADR-0008/ADR-0009) | IAM, security groups, WAF, KMS e TLS onde aplicável | Proteger credenciais, permissões e integrações internas. |
| SBB-016 | Observability | ABB-013 | OpenTelemetry com Aspire Dashboard local (ADR-0012) | ADOT, CloudWatch e X-Ray | Registrar logs, métricas, traces e correlação. |
| SBB-017 | Operational Recovery | ABB-014 | Redelivery e DLQ local no SQS/LocalStack, rotação de credencial ponta a ponta comprovada por execução real (ADR-0009) e runbooks | Redrive policy, DLQ, alarmes CloudWatch e runbooks AWS | Permitir retry, isolamento, reprocessamento, rotação e reconstrução. |
| SBB-018 | Containers and Local Runtime | Runtime local reproduzível | Docker Compose local (ADR-0010) | ECS Fargate como runtime cloud de referência | Executar a solução de forma reproduzível e evoluir para runtime gerenciado. |
| SBB-019 | Configuration and Secrets | Configuração e secrets | Secrets Manager/SSM/KMS reais via LocalStack, um secret por componente, SSM como fonte autoritativa de issuer/audience (ADR-0009) | Secrets Manager e/ou SSM Parameter Store | Centralizar parâmetros e valores sensíveis. |
| SBB-020 | IaC | Infraestrutura como código | Terraform aplicado localmente contra LocalStack (`environments/localstack-hobby`) - init/validate/plan/apply/destroy reais | Terraform materializado nos ambientes `aws-reference`/`development`/`staging`/`production`, validado, nunca aplicado contra AWS real (ADR-0011) | Descrever e provisionar infraestrutura de forma rastreável. |
| SBB-021 | CI/CD | Entrega automatizada | CI local/container-first | GitHub Actions com OIDC, Amazon ECR e deploy ECS por workload (ADR-0013/ADR-0014) | Validar, versionar imagens e implantar a referência AWS. |

---

## 6. Persistência de Lançamentos

A fronteira de Lançamentos utiliza uma persistência própria.

Componentes principais:

```text
Ledger Database
├── Entries
├── Input Idempotency
└── Outbox
```

Responsabilidades:

```text
- persistir lançamentos financeiros
- manter dados de rastreabilidade
- controlar idempotência de entrada
- registrar intenção de publicação na Outbox
- permitir retomada da publicação após falhas
```

O registro do lançamento e a intenção de publicação devem ocorrer de forma consistente dentro da fronteira de Lançamentos.

---

## 7. Persistência do Consolidado

A fronteira de Consolidado utiliza uma persistência própria para leitura e controle de processamento.

Componentes principais:

```text
Consolidation Database
├── DailyBalance
└── Processed Events
```

Responsabilidades:

```text
- armazenar a projeção diária por comerciante e data
- controlar eventos já processados
- evitar duplicidade de efeito no saldo consolidado
- permitir consulta eficiente do relatório diário
- apoiar reconstrução da visão consolidada
```

O Consolidado permanece uma visão derivada. A fonte de verdade financeira continua em Lançamentos.

---

## 8. Comunicação assíncrona

A comunicação entre Lançamentos e Consolidado é materializada por um canal assíncrono.

Fluxo principal:

```text
Ledger.Api
-> Ledger Database / Outbox
-> Ledger.OutboxPublisher
-> Message Broker
-> Consolidation.Worker
-> Consolidation Database / DailyBalance
-> Consolidation.Api
```

No ambiente local, o canal assíncrono é materializado com SQS Standard no LocalStack, provisionado por Terraform local.

Na AWS de referência do case, o mesmo papel é materializado por Amazon SQS Standard com DLQ para `FinancialEntryRegistered.v1`, mantendo consumo idempotente e redrive operacional.

---

## 9. Contratos principais

Os contratos iniciais da solução são:

```text
POST /entries
GET /daily-balances/{businessDate}
```

A identificação do comerciante deve ser obtida do contexto autenticado. Endpoints administrativos ou internos podem incluir o comerciante explicitamente, desde que passem por autorização adequada.

O contrato de registro de lançamento deve contemplar, no mínimo:

```text
- identificação do comerciante obtida do contexto autenticado ou validada contra ele
- tipo do lançamento
- valor
- moeda
- data de ocorrência
- chave de idempotência
- identificadores de rastreabilidade
```

O contrato de consulta do consolidado deve retornar, no mínimo:

```text
- comerciante
- data de negócio
- total de créditos
- total de débitos
- saldo diário
- quantidade de lançamentos considerados
- data e hora da última atualização
```

---

## 10. Observabilidade e recuperação

A solução deve expor sinais operacionais nos principais pontos do fluxo.

Sinais principais:

```text
- requisições recebidas em Ledger.Api
- lançamentos registrados
- tentativas repetidas de entrada
- eventos pendentes na Outbox
- falhas de publicação
- eventos publicados
- mensagens consumidas
- eventos já processados
- eventos duplicados descartados
- falhas de consumo
- atraso entre registro e consolidação
- consultas ao Consolidado
- taxa de erro do Consolidado
```

Mecanismos de recuperação:

```text
- retry de publicação
- retry de consumo
- isolamento de mensagens com falha persistente
- reprocessamento controlado
- reconstrução da projeção DailyBalance a partir dos lançamentos
```

---

## 11. Segurança

A solução deve materializar segurança em dois níveis.

Segurança de acesso externo:

```text
- autenticação das chamadas externas
- autorização por comerciante
- proteção contra consulta cruzada entre comerciantes
- validação de entrada
- rate limit e proteções contra abuso
```

Segurança interna:

```text
- menor privilégio entre componentes, com segregação completa de credenciais como requisito produtivo
- acesso restrito aos bancos
- acesso restrito ao broker
- proteção da comunicação entre serviços
- uso de secrets por ambiente
```

O detalhamento completo será tratado em `docs/security/arquitetura-de-seguranca.md`.

---

## 12. Portabilidade local e AWS de referência

A solução separa materialização local de materialização AWS de referência.

| Necessidade | Execução local | AWS como referência do case |
|---|---|---|
| APIs e workers | Containers Docker | ECS Fargate. |
| Imagens | Build local/CI | ECR. |
| Banco de dados | PostgreSQL em container | RDS for PostgreSQL. |
| Mensageria | LocalStack SQS Standard com DLQ | SQS Standard com DLQ. |
| Secrets | Secrets Manager/SSM/KMS reais via LocalStack (ADR-0009) | Secrets Manager/SSM. |
| Criptografia | KMS real via LocalStack (round-trip comprovado) | KMS. |
| Observabilidade | OpenTelemetry e Aspire Dashboard | ADOT, CloudWatch e X-Ray. |
| Autenticação | Keycloak real, OIDC/RS256, discovery e JWKS (ADR-0007) | IdP OIDC/OAuth2 corporativo ou gerenciado. |
| Exposição HTTP | edge-proxy real (HTTPS/WAF, ADR-0008), sem porta própria publicada pelas APIs | AWS WAF, API Gateway REST, VPC Link V2 e ALB interno, sem NLB intermediário. |
| IaC | Terraform aplicado localmente contra LocalStack (`environments/localstack-hobby`) | Terraform materializado nos ambientes `aws-reference`/`development`/`staging`/`production`, validado, nunca aplicado contra AWS real. |
| CI/CD | GitHub Actions de CI | GitHub Actions com OIDC para AWS. |

Essa abordagem mantém a solução executável no desafio e documenta AWS como plataforma de referência, sem afirmar que o Banco Carrefour usa AWS.

---

## 13. Mapeamento ABB para SBB

| ABB | SBBs relacionados |
|---|---|
| ABB-001 — Fronteira de Lançamentos | SBB-001 |
| ABB-002 — Fonte de Verdade Financeira | SBB-002, SBB-003 |
| ABB-003 — Persistência Transacional de Lançamentos | SBB-002, SBB-003, SBB-005 |
| ABB-004 — Idempotência de Entrada | SBB-004 |
| ABB-005 — Outbox Durável | SBB-005 |
| ABB-006 — Publicação Recuperável | SBB-006 |
| ABB-007 — Canal Assíncrono Confiável | SBB-007 |
| ABB-008 — Fronteira de Consolidado | SBB-008, SBB-012 |
| ABB-009 — Consumo Idempotente | SBB-010 |
| ABB-010 — Projeção Materializada do Consolidado | SBB-011 |
| ABB-011 — Persistência do Consolidado | SBB-009, SBB-010, SBB-011 |
| ABB-012 — API de Consulta do Consolidado | SBB-012, SBB-013 |
| ABB-013 — Observabilidade do Fluxo | SBB-016 |
| ABB-014 — Recuperação Operacional | SBB-006, SBB-017 |
| ABB-015 — Segurança de Acesso | SBB-014 |
| ABB-016 — Controle de Comunicação entre Serviços | SBB-015, SBB-019 |

---

## 14. Mapeamento SBB para ADR

| ADR | SBBs sustentados |
|---|---|
| ADR-0000 — Semântica financeira e data de negócio | SBB-011, SBB-012 |
| ADR-0001 — Fronteiras Ledger e Consolidation | SBB-001, SBB-008, SBB-012 |
| ADR-0002 — Persistência PostgreSQL independente por fronteira | SBB-002, SBB-003, SBB-004, SBB-005, SBB-009, SBB-010, SBB-011 |
| ADR-0003 — Arquitetura hexagonal e direção das dependências | SBB-001, SBB-006, SBB-008, SBB-012 |
| ADR-0004 — Integração assíncrona confiável | SBB-002, SBB-005, SBB-006, SBB-007, SBB-008, SBB-010, SBB-017 |
| ADR-0005 — Contratos HTTP e eventos de integração | SBB-013 |
| ADR-0006 — Unidades implantáveis e topologia de runtime | SBB-001, SBB-006, SBB-008, SBB-012, SBB-018 |
| ADR-0007 — Identidade, autorização e isolamento por merchant | SBB-014 |
| ADR-0008 — Proteção de borda e conectividade privada | SBB-015 |
| ADR-0009 — Menor privilégio, secrets e criptografia | SBB-015, SBB-019 |
| ADR-0010 — Execução local e paridade comportamental | SBB-001, SBB-002, SBB-006, SBB-007, SBB-008, SBB-009, SBB-012, SBB-014, SBB-015, SBB-016, SBB-017, SBB-018, SBB-019 |
| ADR-0011 — Plataforma AWS e isolamento de ambientes | SBB-002, SBB-009, SBB-019, SBB-020 |
| ADR-0012 — Observabilidade e objetivos operacionais | SBB-016, SBB-017, SBB-018, SBB-019 |
| ADR-0013 — Integridade de release e software supply chain | SBB-018, SBB-021 |
| ADR-0014 — Promoção, deployment e rollback por workload | SBB-001, SBB-006, SBB-008, SBB-012, SBB-021 |
| ADR-0015 — Governança de migrations de banco de dados | SBB-002, SBB-009 |

---

## 15. Relação com os próximos documentos

Este documento materializa os ABBs em blocos concretos de solução.

A composição desses blocos está detalhada em:

```text
- 05-arquitetura-da-solucao.md
- 06-diagramas.md
- [docs/decisions/](../decisions/)
- [docs/security/](../security/)
- [docs/operations/](../operations/)
```

---

## 16. Status

Documento atualizado como baseline dos SBBs materializados ou planejados para evolução produtiva.
