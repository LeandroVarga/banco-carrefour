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
| 4. Segurança | Autenticação OIDC/RS256 via Keycloak, autorização por comerciante e por escopo, proteção de borda (TLS, WAF, ModSecurity), secrets/SSM/KMS e menor privilégio entre componentes são reais e testados no baseline local. | [Segurança](docs/security/arquitetura-de-seguranca.md), [ADR-0007](docs/decisions/ADR-0007-identidade-autorizacao-e-isolamento-por-merchant.md), [ADR-0008](docs/decisions/ADR-0008-protecao-de-borda-e-conectividade-privada.md), [ADR-0009](docs/decisions/ADR-0009-menor-privilegio-secrets-e-criptografia.md) |
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

Antes de subir a stack, gere os secrets e certificados locais (idempotente, nunca versionado):

```bash
sh scripts/security/bootstrap-local-security.sh
```

Na pasta raiz do projeto, execute:

```powershell
docker compose up -d --build ledger-api ledger-outbox-publisher consolidation-worker consolidation-api aspire-dashboard
```

Esse comando usa o `docker-compose.yml` da raiz do repositório e sobe, por dependência transitiva, Keycloak, LocalStack, os dois PostgreSQL e o `edge-proxy`, além dos quatro workloads de negócio e do dashboard local de observabilidade, preparando os bancos no processo. `Ledger.Api` e `Consolidation.Api` não publicam porta própria no host — o único ponto de entrada de negócio é o `edge-proxy` HTTPS (ADR-0008).

Health checks, através do `edge-proxy` (certificado autoassinado local, use `-k`):

```bash
curl -sk https://localhost:8443/ledger/health/ready
curl -sk https://localhost:8443/consolidation/health/ready
```

URLs locais:

| Serviço | URL |
|---|---|
| Ledger.Api (via edge-proxy) | `https://localhost:8443/ledger/` |
| Consolidation.Api (via edge-proxy) | `https://localhost:8443/consolidation/` |
| Keycloak (via edge-proxy) | `https://keycloak.localhost:8443` |
| LocalStack SQS | `http://localhost:4566` |
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
docker compose run --rm --no-deps -v /var/run/docker.sock:/var/run/docker.sock -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal dotnet-sdk dotnet test
```

Os testes de integração provisionam PostgreSQL e LocalStack/SQS efêmeros com Testcontainers. Não é necessário executar `docker compose up` antes dos testes.

**Limitação conhecida do comando acima**: `tests/Security.IntegrationTests` cria containers Keycloak/PostgreSQL ad-hoc via Testcontainers e acessa a porta publicada via `127.0.0.1` diretamente (ver `IdentityFixture.cs`). Quando o processo de teste roda dentro do container-irmão `dotnet-sdk` (como no comando acima), esse `127.0.0.1` é isolado por namespace de rede do próprio container-irmão e não alcança o container do Keycloak — os demais projetos de teste não têm esse problema. Para rodar `Security.IntegrationTests` localmente, use o SDK do .NET diretamente no host (`dotnet test tests/Security.IntegrationTests`), com Docker Desktop disponível para o Testcontainers. O CI hospedado (`security-gate` em `ci.yml`) já roda esse projeto nativamente no runner pelo mesmo motivo - ver [dependencias-e-supply-chain.md](docs/security/dependencias-e-supply-chain.md).

Smoke de desempenho (50 RPS, reproduzível localmente e no gate de CI - ver [09-escopo-priorizacao-e-limites.md](docs/architecture/09-escopo-priorizacao-e-limites.md) e ADR-0012):

```bash
sh scripts/security/bootstrap-local-security.sh
sh scripts/ci/run-performance-smoke.sh
```

`dotnet test` cobre contratos e integração. O Docker Compose permanece como caminho da demonstração local completa. O smoke de desempenho mede a capacidade real do `Consolidation.Api` (alvo direto, não pelo edge-proxy - ver ADR-0012) e valida o requisito de 50 RPS com no máximo 5% de falhas elegíveis.

## 7. Evidências principais

| Evidência | Resultado |
|---|---|
| Testes automatizados | Contratos e integração executados por `dotnet test`, com infraestrutura efêmera nos testes de integração. |
| CI (5 gates) | `fast-quality-gate` (unitário/contrato/arquitetura), `integration-gate`, `security-gate`, `image-gate` (build + non-root das 4 imagens) e `performance-smoke-gate` (50 RPS) - ver ADR-0012. Workflow validado localmente; execução real em runner hospedado pelo GitHub ainda pendente até o branch ser enviado. |
| Smoke de desempenho do Consolidado | 50 RPS validados localmente/container-first, reproduzível via `scripts/ci/run-performance-smoke.sh`. |
| Supply chain | SBOM (CycloneDX) e scan de vulnerabilidade das 4 imagens reais e do artefato operacional `migration-runner`, auditoria NuGet, revisão de dependências em PR, CodeQL e Dependabot - ver [dependencias-e-supply-chain.md](docs/security/dependencias-e-supply-chain.md). Validado localmente com sucesso; execução real em runner hospedado ainda pendente. |
| Identidade de release e Amazon ECR | 4 repositórios ECR (um por workload de negócio), identidade OIDC do GitHub Actions e IAM de publicação de menor privilégio (`infra/terraform/environments/aws-reference`), workflow `publish-images.yml` (build once → release-qualification → publicar → atestar) - ver [ADR-0013](docs/decisions/ADR-0013-integridade-de-release-e-software-supply-chain.md). Terraform validado (`fmt`/`init`/`validate`/`plan`, nunca aplicado). Manifesto de release versionado (`schemas/release-manifest.schema.json`, v4.0.0), separando os 4 componentes de negócio do artefato operacional `migration-runner`. Nenhuma imagem publicada, nenhuma role assumida, nenhuma atestação hospedada executada. |
| Plataforma AWS multi-conta e estratégias de deployment por workload | Módulos Terraform de rede, cluster ECS, 3 estratégias de deployment por workload, RDS (Ledger e Consolidation independentes), borda (WAF/API Gateway/VPC Link V2/ALB interno), observabilidade e alarmes, materializados nos 3 ambientes de workload (`development`/`staging`/`production`) e conectados a 4 workflows de deploy (`deploy-development.yml`, `promote-staging.yml`, `promote-production.yml`, `rollback-production.yml`) - ver [ADR-0011](docs/decisions/ADR-0011-plataforma-aws-e-isolamento-de-ambientes.md) e [ADR-0014](docs/decisions/ADR-0014-promocao-deployment-e-rollback-por-workload.md). CANARY nativo do ECS (Ledger.Api/Consolidation.Api), capacity canary de dois serviços (Consolidation.Worker), rolling controlado com circuit breaker (Ledger.OutboxPublisher) - decisões verificadas contra a documentação oficial da AWS e o schema real do provider Terraform antes de implementar. `terraform fmt`/`validate` limpos nos 5 ambientes reais; nenhum `terraform apply` foi executado, nenhum recurso AWS real foi provisionado. |

Resultado observado no smoke de desempenho (janela sustentada):

| Métrica | Valor |
|---|---|
| Requisições planejadas | 3000 |
| Requisições executadas | 3000 |
| Sucessos | 3000 |
| Falhas | 0 |
| Throughput observado | 50.01 req/s |
| p50 | 2.66 ms |
| p95 | 4.23 ms |
| p99 | 6.39 ms |

Detalhes:

- [docs/operations/evidencias-do-case.md](docs/operations/evidencias-do-case.md)
- [docs/operations/teste-de-carga-consolidado.md](docs/operations/teste-de-carga-consolidado.md)
- [docs/traceability.md](docs/traceability.md)

## 8. Decisões arquiteturais

As decisões estão registradas em 16 ADRs (ADR-0000 a ADR-0015). O índice de navegação está em [docs/decisions/README.md](docs/decisions/README.md); o registro detalhado de governança está em [docs/decisions/registro-de-decisoes.md](docs/decisions/registro-de-decisoes.md).

Decisões centrais:

- separar Ledger e Consolidation em fronteiras independentes, com persistência PostgreSQL própria por fronteira;
- usar Outbox transacional e Amazon SQS Standard com DLQ para integração assíncrona confiável;
- adotar identidade OIDC/RS256 via Keycloak, autorização por escopo e isolamento por comerciante;
- proteger a borda localmente com TLS/WAF/ModSecurity, e na AWS de referência com WAF/API Gateway/VPC Link V2/ALB interno;
- tratar AWS como plataforma de referência multi-conta, com Amazon ECR como registry único e release imutável por digest;
- promover a mesma release por Development/Staging/Production com estratégia de deployment específica por workload;
- governar migrations de banco de dados por `MigrationRunner`, advisory lock do PostgreSQL e disciplina EXPAND/BACKFILL/CONTRACT.

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
