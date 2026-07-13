# banco-carrefour

Solução para o desafio técnico de Arquiteto de Soluções: controle de fluxo de caixa diário para comerciantes, com registro de lançamentos de débito/crédito e disponibilização de relatório com saldo diário consolidado.

## Visão geral

A solução separa **Lançamentos** e **Consolidado** em fronteiras independentes.

**Lançamentos** é a fonte de verdade financeira.

**Consolidado** é uma projeção derivada, materializada e reconstruível.

O registro de lançamentos não depende de chamada síncrona ao Consolidado. A integração ocorre de forma assíncrona, com Outbox transacional, broker de mensagens e consumo idempotente.

O relatório com saldo diário consolidado é disponibilizado por API a partir da projeção `DailyBalance`.

A entrega inclui arquitetura, implementação local/container-first, testes, segurança, observabilidade, operação, CI container-first e decisões arquiteturais rastreáveis. A referência AWS, CI/CD de deploy, publicação de imagens e Terraform estão documentados como caminho de implantação do case, sem afirmar execução produtiva.

## Arquitetura

A arquitetura usa Outbox transacional para desacoplar o registro de lançamentos da consolidação diária.

O Ledger grava o lançamento e a mensagem de integração na mesma transação. O publisher publica eventos pendentes em um broker de mensagens. O Worker do Consolidado consome os eventos, deduplica por `eventId` e atualiza a projeção `DailyBalance`.

RabbitMQ materializa localmente o papel de mensageria para tornar o case reproduzível. Na implantação AWS de referência do case, esse papel é atendido por Amazon SQS Standard com DLQ.

Fluxos, sequências e diagramas estão em [docs/architecture/06-diagramas.md](docs/architecture/06-diagramas.md). A jornada segue ABB sem tecnologia, SBB com materialização concreta e AWS como plataforma de referência do case. Essa decisão está documentada em [docs/decisions/ADR-0010-execucao-local-portabilidade-cloud-e-padroes-corporativos.md](docs/decisions/ADR-0010-execucao-local-portabilidade-cloud-e-padroes-corporativos.md).

### Unidades implantáveis

| Unidade | Responsabilidade |
|---|---|
| `Ledger.Api` | Registra lançamentos, valida contrato, aplica autenticação/autorização, garante idempotência de entrada e grava a Outbox. |
| `Ledger.OutboxPublisher` | Publica eventos pendentes da Outbox no broker de mensagens. |
| `Consolidation.Worker` | Consome eventos, deduplica por `eventId`, atualiza `DailyBalance` e trata retry/DLQ. |
| `Consolidation.Api` | Disponibiliza o relatório com saldo diário consolidado por comerciante e data de negócio. |

### Padrões adotados

```text
- separação de responsabilidades por fronteira
- Outbox transacional
- comunicação assíncrona
- mensageria local por RabbitMQ e referência AWS por SQS/DLQ
- consumo at-least-once
- idempotência de entrada
- deduplicação por eventId
- projeção materializada
- retry controlado
- DLQ
- contratos versionados
- OpenTelemetry
- CI container-first já implementado
- CI/CD, imagens versionadas e Terraform como referência de entrega AWS
```

## Atendimento ao desafio

| Bloco do case | Atendimento | Referências |
|---|---|---|
| 1. Arquitetura e Domínios | Domínios, capacidades e limites entre Lançamentos e Consolidado. | [Contexto](docs/architecture/01-contexto-de-negocio.md), [ABBs](docs/architecture/03-blocos-de-arquitetura.md), [SBBs](docs/architecture/04-blocos-de-solucao.md) |
| 2. Levantamento de Requisitos | Requisitos funcionais, não funcionais e ASRs rastreáveis. | [Requisitos](docs/architecture/02-requisitos-arquiteturais.md), [Rastreabilidade](docs/architecture/07-rastreabilidade.md), [Traceability](docs/traceability.md) |
| 3. Arquitetura da Solução | Componentes, responsabilidades, fluxos, integrações e padrões arquiteturais. | [Arquitetura da Solução](docs/architecture/05-arquitetura-da-solucao.md), [Diagramas](docs/architecture/06-diagramas.md) |
| 4. Segurança | Autenticação, autorização, proteção de dados, segurança de APIs e comunicação entre serviços. | [Segurança](docs/security/arquitetura-de-seguranca.md), [ADR-0011](docs/decisions/ADR-0011-decisoes-de-seguranca.md) |
| 5. Implementação | Código, contratos, testes, CI local e referência de CI/CD, imagens e Terraform. | [Contratos](contracts/), [Código](src/), [Testes](tests/), [Workflows](.github/workflows/), [Infra](infra/) |
| 6. Operação | Deploy, monitoramento, logs, observabilidade, escalabilidade e recuperação. | [Operação](docs/operations/arquitetura-operacional.md), [Observabilidade](docs/operations/observabilidade-sli-slo-e-recuperacao.md), [Runbook](docs/operations/runbook-demonstracao-local.md) |
| Complementares | ADRs, custos, evidências e critérios operacionais. | [ADRs](docs/decisions/registro-de-decisoes.md), [Custos](docs/operations/estimativa-de-custos.md), [Evidências](docs/operations/evidencias-do-case.md) |

## Segurança

A segurança é parte da arquitetura da solução e cobre autenticação, autorização, proteção de dados, segurança de APIs, comunicação entre serviços e segregação de responsabilidades.

Na execução reproduzível do case, alguns mecanismos são simplificados, como JWT local HS256 e credenciais locais. Na implantação AWS de referência, a solução mapeia segurança para IdP corporativo via OIDC/OAuth2, Cognito quando aplicável, IAM, Secrets Manager/SSM, KMS, controles de rede, WAF associado ao API Gateway, VPC Link, ALB interno, TLS/mTLS onde aplicável, menor privilégio e auditoria.

Detalhes estão em [docs/security/arquitetura-de-seguranca.md](docs/security/arquitetura-de-seguranca.md) e [docs/decisions/ADR-0011-decisoes-de-seguranca.md](docs/decisions/ADR-0011-decisoes-de-seguranca.md).

## Implementação, CI/CD e IaC

| Área | Entrega |
|---|---|
| Contratos | OpenAPI e contrato versionado do evento `EntryCreated.v1`. |
| Código | APIs, workers, persistências, Outbox, projeção, retry/DLQ, autenticação, rate limiting e health checks. |
| Testes | Cobertura automatizada de contratos, integração, idempotência, projeção, consumo, APIs, concorrência e validações. |
| CI | Build e testes container-first já implementados em GitHub Actions. |
| CI/CD AWS | Decisão documentada para OIDC, push no ECR e deploy no ECS. |
| Imagens | APIs e workers empacotáveis em containers; publicação no ECR é referência de implantação. |
| IaC | Terraform documentado como abordagem de referência AWS; módulos funcionais ainda não foram aplicados. |

Referências principais:

| Item | Link |
|---|---|
| Contrato HTTP | [contracts/openapi.yaml](contracts/openapi.yaml) |
| Evento `EntryCreated.v1` | [contracts/events/entry-created-v1.schema.json](contracts/events/entry-created-v1.schema.json) |
| Código | [src/](src/) |
| Testes | [tests/](tests/) |
| CI/CD | [.github/workflows/](.github/workflows/) |
| Terraform | [infra/](infra/) |

## Operação

A operação cobre deploy, monitoramento, logs, observabilidade, escalabilidade e recuperação de falhas.

| Tema | Atendimento |
|---|---|
| Deploy | Execução local via Compose implementada; deploy AWS de referência documentado para ECS Fargate. |
| Infraestrutura | Terraform definido como referência para rede, mensageria, banco, permissões, parâmetros, segredos e observabilidade. |
| Monitoramento | Health checks, logs, métricas e traces implementados no baseline local; dashboards produtivos, alarmes e métricas completas de backlog/lag permanecem pendentes. |
| Logs | Logs estruturados com correlação por requisição, evento e fluxo assíncrono. |
| Observabilidade | OpenTelemetry como padrão de instrumentação. |
| Escalabilidade | Escrita, publicação, consumo e consulta separados para escala independente. |
| Recuperação | Outbox, retry controlado, DLQ, consumo idempotente, projeção reconstruível, redrive e reprocessamento. |

Detalhes operacionais estão em [docs/operations/arquitetura-operacional.md](docs/operations/arquitetura-operacional.md), [docs/operations/observabilidade-sli-slo-e-recuperacao.md](docs/operations/observabilidade-sli-slo-e-recuperacao.md) e [docs/operations/runbook-demonstracao-local.md](docs/operations/runbook-demonstracao-local.md).

## Execução local

A execução local é container-first via Docker Compose.

Runbook completo: [docs/operations/runbook-demonstracao-local.md](docs/operations/runbook-demonstracao-local.md).

Subir infraestrutura local:

```powershell
docker compose up -d ledger-postgres consolidation-postgres rabbitmq
```

Aplicar migrations:

```powershell
docker compose run --rm ledger-migrations
docker compose run --rm consolidation-migrations
```

Subir solução completa:

```powershell
docker compose up -d --build ledger-api ledger-outbox-publisher consolidation-worker consolidation-api aspire-dashboard
```

Validar health checks:

```powershell
curl.exe http://localhost:8080/health/ready
curl.exe http://localhost:8081/health/ready
```

URLs locais:

| Serviço | URL |
|---|---|
| Ledger.Api | `http://localhost:8080` |
| Consolidation.Api | `http://localhost:8081` |
| RabbitMQ Management | `http://localhost:15672` |
| Aspire Dashboard | `http://localhost:18888` |

## Evidências

| Evidência | Link |
|---|---|
| Atendimento do case | [docs/operations/evidencias-do-case.md](docs/operations/evidencias-do-case.md) |
| Teste de carga do Consolidado | [docs/operations/teste-de-carga-consolidado.md](docs/operations/teste-de-carga-consolidado.md) |
| Rastreabilidade | [docs/traceability.md](docs/traceability.md) |
| Contratos | [contracts/](contracts/) |
| Testes | [tests/](tests/) |
| CI/CD | [.github/workflows/](.github/workflows/) |
| IaC | [infra/](infra/) |

Resultado final do teste de carga do Consolidado:

```text
total sustentado planejado: 3000
total sustentado executado: 3000
sucessos: 3000
falhas: 0
throughput observado: 50.02 req/s
p95: 5.80 ms
p99: 7.51 ms
```

## Decisões principais

As decisões arquiteturais estão registradas em [docs/decisions/registro-de-decisoes.md](docs/decisions/registro-de-decisoes.md).

Principais decisões:

```text
- consolidado diário como movimento líquido do dia
- separação entre Lançamentos e Consolidado
- Ledger como fonte de verdade
- Consolidado como projeção derivada e reconstruível
- Outbox transacional
- integração assíncrona por broker ou fila gerenciada
- consumo at-least-once com idempotência
- persistências independentes por fronteira
- PostgreSQL como referência relacional
- .NET LTS, ASP.NET Core, Worker Service e containers
- OpenTelemetry
- CI/CD para build, testes, versionamento e publicação de imagens
- Terraform para infraestrutura como código
```

## Premissas e limites

Premissas adotadas:

```text
- AWS como referência de implantação cloud do case
- Docker Compose como execução local reproduzível
- RabbitMQ como materialização local do papel de broker
- Terraform como ferramenta de infraestrutura como código
- ECR como referência para imagens versionadas de APIs e workers
- adaptação dos serviços finais conforme plataforma corporativa ou cloud disponível
```

Limites preservados:

```text
- o case não informa legado, portanto arquitetura de transição não se aplica
- a execução local não representa topologia produtiva de alta disponibilidade
- sizing final, autoscaling e limites reais dependem de ambiente produtivo ou equivalente
- políticas corporativas de segurança, rede, auditoria e retenção podem alterar a materialização final
- mapeamento final de serviços AWS pode ser substituído por equivalentes corporativos sem alterar os papéis arquiteturais
- publicação de imagens, Terraform aplicado, deploy AWS e smoke tests não foram executados no estado atual
```
