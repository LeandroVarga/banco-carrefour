# banco-carrefour

Solução para o desafio técnico de Arquiteto de Soluções: controle de fluxo de caixa diário para comerciantes.

A entrega cobre registro de lançamentos de débito/crédito, consolidação diária, relatório por API e execução local reproduzível via Docker Compose.

## 1. O que foi entregue

- Serviço de **Lançamentos** para registrar créditos e débitos.
- Serviço de **Consolidado** para consultar o saldo diário por comerciante.
- Separação entre escrita e leitura, com Ledger como fonte de verdade.
- Outbox transacional, mensageria assíncrona e consumo idempotente.
- Projeção `DailyBalance` materializada e reconstruível.
- Documentação de arquitetura, segurança, decisões e operação.
- Implementação local/container-first com testes automatizados e CI.
- Referência AWS documentada para ECS Fargate, RDS PostgreSQL, SQS/DLQ, API Gateway com WAF, VPC Link/private integration e ALB interno.

## 2. Como avaliar este repositório

| Objetivo do avaliador | Onde olhar |
|---|---|
| Ver atendimento item a item do case | [docs/operations/evidencias-do-case.md](docs/operations/evidencias-do-case.md) |
| Entender arquitetura | [docs/architecture/05-arquitetura-da-solucao.md](docs/architecture/05-arquitetura-da-solucao.md) |
| Ver diagramas | [docs/architecture/06-diagramas.md](docs/architecture/06-diagramas.md) |
| Ver segurança | [docs/security/arquitetura-de-seguranca.md](docs/security/arquitetura-de-seguranca.md) |
| Executar localmente | [docs/operations/runbook-demonstracao-local.md](docs/operations/runbook-demonstracao-local.md) |
| Ver decisões | [docs/decisions/registro-de-decisoes.md](docs/decisions/registro-de-decisoes.md) |
| Ver operação | [docs/operations/arquitetura-operacional.md](docs/operations/arquitetura-operacional.md) |
| Ver referência AWS/IaC | [infra/README.md](infra/README.md) e [docs/operations/runbook-implantacao-aws.md](docs/operations/runbook-implantacao-aws.md) |

## 3. Atendimento ao case

| Item do case | Como foi atendido | Referências |
|---|---|---|
| 1. Arquitetura e Domínios | Domínios, capacidades e limites entre Lançamentos e Consolidado foram definidos, com Ledger como fonte de verdade e Consolidado como projeção derivada. | [Contexto](docs/architecture/01-contexto-de-negocio.md), [ABBs](docs/architecture/03-blocos-de-arquitetura.md), [SBBs](docs/architecture/04-blocos-de-solucao.md) |
| 2. Levantamento de Requisitos | Requisitos funcionais, requisitos não funcionais, ASRs e critérios arquiteturais foram rastreados. | [Requisitos](docs/architecture/02-requisitos-arquiteturais.md), [Rastreabilidade](docs/architecture/07-rastreabilidade.md), [Traceability](docs/traceability.md) |
| 3. Arquitetura da Solução | A solução descreve componentes, responsabilidades, fluxos de comunicação, padrões arquiteturais, execução local e referência AWS. | [Arquitetura](docs/architecture/05-arquitetura-da-solucao.md), [Diagramas](docs/architecture/06-diagramas.md) |
| 4. Segurança | Autenticação, autorização por comerciante, proteção de APIs, proteção de dados, secrets e comunicação entre serviços foram documentados e parcialmente materializados no baseline local. | [Segurança](docs/security/arquitetura-de-seguranca.md), [ADR-0011](docs/decisions/ADR-0011-decisoes-de-seguranca.md) |
| 5. Implementação | Código em [src/](src/), contratos em [contracts/](contracts/), testes em [tests/](tests/), execução container-first e CI em [.github/workflows/](.github/workflows/). | [Evidências](docs/operations/evidencias-do-case.md), [Traceability](docs/traceability.md) |
| 6. Operação da Solução | Runbook local, health checks, logs, observabilidade, recuperação, evidências e runbook AWS como referência documental. | [Runbook local](docs/operations/runbook-demonstracao-local.md), [Operação](docs/operations/arquitetura-operacional.md), [Observabilidade](docs/operations/observabilidade-sli-slo-e-recuperacao.md) |
| 7. Diferenciais ou complementares | ADRs, estimativa de custos, matriz de evidências, referência AWS/IaC/CI-CD e separação explícita entre baseline local e produção real. | [ADRs](docs/decisions/registro-de-decisoes.md), [Custos](docs/operations/estimativa-de-custos.md), [Infra](infra/README.md) |

## 4. Arquitetura em uma frase

O Ledger é a fonte de verdade financeira. O Consolidado é uma projeção derivada para leitura diária por comerciante.

O registro de lançamentos não chama o Consolidado de forma síncrona. A Outbox transacional e a mensageria assíncrona desacoplam as fronteiras, enquanto o consumo idempotente atualiza a projeção `DailyBalance`.

## 5. Como executar localmente

Pré-requisitos:

- Git
- Docker com Docker Compose

Após clonar o repositório, acesse a pasta raiz do projeto:

```powershell
cd banco-carrefour
```

Na pasta raiz do projeto, execute:

```powershell
docker compose up -d --build ledger-api ledger-outbox-publisher consolidation-worker consolidation-api aspire-dashboard
```

Esse comando usa o `docker-compose.yml` da raiz do repositório, sobe as dependências necessárias, prepara os bancos e inicia APIs, workers e dashboard local de observabilidade.

Health checks:

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

Para reiniciar o ambiente do zero, ainda na pasta raiz do projeto, execute:

```powershell
docker compose down -v
docker compose up -d --build ledger-api ledger-outbox-publisher consolidation-worker consolidation-api aspire-dashboard
```

Fluxo end-to-end completo, geração de token local, idempotência, DLQ e telemetria estão no [runbook local](docs/operations/runbook-demonstracao-local.md).

## 6. Como rodar os testes

Comando principal:

```powershell
docker compose run --rm dotnet-sdk dotnet test
```

Teste de carga separado:

```powershell
docker compose run --rm dotnet-sdk dotnet run --project tests/Consolidation.LoadTests
```

`dotnet test` cobre contratos e integração. O teste de carga é separado e valida o requisito de 50 RPS do Consolidado no baseline local/container-first.

## 7. Evidências principais

| Evidência | Resultado |
|---|---|
| Testes automatizados | Contratos e integração executados por `dotnet test`. |
| CI container-first | Workflow executa build, testes e `git diff --check` via Docker Compose. |
| Teste de carga do Consolidado | 50 RPS validados localmente/container-first. |

Resultado observado no teste de carga:

| Métrica | Valor |
|---|---|
| Requisições planejadas | 3000 |
| Requisições executadas | 3000 |
| Sucessos | 3000 |
| Falhas | 0 |
| Throughput observado | 50.02 req/s |
| p95 | 5.80 ms |
| p99 | 7.51 ms |

Detalhes:

- [docs/operations/evidencias-do-case.md](docs/operations/evidencias-do-case.md)
- [docs/operations/teste-de-carga-consolidado.md](docs/operations/teste-de-carga-consolidado.md)
- [docs/traceability.md](docs/traceability.md)

## 8. Decisões arquiteturais

As decisões estão registradas como ADRs. O índice está em [docs/decisions/registro-de-decisoes.md](docs/decisions/registro-de-decisoes.md).

Decisões centrais:

- separar Lançamentos e Consolidado;
- usar Outbox transacional;
- adotar consumo at-least-once e idempotente;
- manter persistências independentes;
- usar PostgreSQL como persistência relacional;
- tratar AWS como plataforma de referência do case.

## 9. Limites assumidos

- Execução local não é topologia produtiva de alta disponibilidade.
- AWS é referência documental, sem deploy executado.
- Terraform não foi executado em ambiente AWS.
- Publicação de imagens no ECR não foi executada.
- Rate limiting produtivo/distribuído permanece pendente.
- Observabilidade produtiva permanece pendente.
- Validação produtiva de múltiplos workers, backlog e autoscaling permanece pendente.

## 10. Mapa da documentação

| Área | Documento |
|---|---|
| Mapa geral | [docs/README.md](docs/README.md) |
| Arquitetura | [docs/architecture/README.md](docs/architecture/README.md) |
| Segurança | [docs/security/README.md](docs/security/README.md) |
| Decisões | [docs/decisions/registro-de-decisoes.md](docs/decisions/registro-de-decisoes.md) |
| Operação | [docs/operations/README.md](docs/operations/README.md) |
| Referência AWS/IaC | [infra/README.md](infra/README.md) |
