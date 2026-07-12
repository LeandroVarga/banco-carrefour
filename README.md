# banco-carrefour

Case de arquitetura de soluções para o desafio Banco Carrefour.

## Objetivo

Este repositório documenta e materializa uma solução para controle de fluxo de caixa diário de comerciantes, com registro de lançamentos de débito e crédito e consulta de consolidado diário.

A arquitetura prioriza confiabilidade do registro financeiro, disponibilidade do serviço de Lançamentos, consulta eficiente do Consolidado, segurança, observabilidade, recuperação e decisões arquiteturais rastreáveis.

## Situação atual

Status do trabalho:

```text
- documentação arquitetural principal criada
- ADRs criados de ADR-0000 a ADR-0013
- arquitetura, segurança e operação documentadas
- baseline .NET container-first criado
- Ledger write path inicial implementado no PR #4
- POST /entries implementado com autenticação JWT local, merchant_id derivado do token, idempotência de entrada, fingerprint canônico e Outbox transacional
- Ledger.OutboxPublisher implementado com RabbitMQ, publish confirm e mandatory routing
- Consolidation.Persistence implementado com DailyBalance e ProcessedEvent
- Consolidation.Application implementado com EntryCreatedProjectionProcessor, aplicação de CREDIT/DEBIT e deduplicação por eventId
- Consolidation.Worker implementado consumindo EntryCreated.v1 via RabbitMQ
- política básica de consumo do Consolidado implementada: sucesso e duplicado com ack; JSON inválido e erro de validação encaminhados para DLQ; erro desconhecido/transitório com nack/requeue
- Consolidation.Api implementada com GET /daily-balances/{businessDate}
- consulta do Consolidado deriva merchant_id do token e retorna 404 para projeção indisponível sem afirmar saldo zero
- testes de contrato e integração criados para contratos, Ledger write path, Outbox publisher, projeção, consumer e API do Consolidado
- CI container-first criado em .github/workflows/ci.yml
- teste de carga local/container-first do Consolidado executado com 50.01 req/s sustentado, 0% falhas, p95 4.50 ms e p99 5.68 ms
- health/readiness/liveness básicos das APIs HTTP implementados
- execução end-to-end local via Docker Compose com APIs, workers, bancos e RabbitMQ implementada
- observabilidade completa, retry/backoff avançado, hardening produtivo e deploy/IaC ainda pendentes
```

## Como navegar

| Objetivo | Documento |
|---|---|
| Mapa geral da documentação | `docs/README.md` |
| Jornada arquitetural | `docs/architecture/00-jornada-arquitetural.md` |
| Contexto de negócio | `docs/architecture/01-contexto-de-negocio.md` |
| Requisitos, RNFs e ASRs | `docs/architecture/02-requisitos-arquiteturais.md` |
| ABBs | `docs/architecture/03-blocos-de-arquitetura.md` |
| SBBs | `docs/architecture/04-blocos-de-solucao.md` |
| Arquitetura alvo | `docs/architecture/05-arquitetura-da-solucao.md` |
| Diagramas | `docs/architecture/06-diagramas.md` |
| Rastreabilidade | `docs/architecture/07-rastreabilidade.md` |
| ADRs | `docs/decisions/registro-de-decisoes.md` |
| Segurança | `docs/security/arquitetura-de-seguranca.md` |
| Operação | `docs/operations/arquitetura-operacional.md` |
| Observabilidade, SLIs, SLOs e recuperação | `docs/operations/observabilidade-sli-slo-e-recuperacao.md` |
| Estimativa de custos | `docs/operations/estimativa-de-custos.md` |

## Síntese da arquitetura

A solução separa duas fronteiras principais:

```text
- Lançamentos
- Consolidado
```

Lançamentos é a fonte de verdade financeira.

Consolidado é uma visão derivada, materializada e reconstruível.

O registro de lançamentos não depende de chamada síncrona ao Consolidado.

O fluxo principal é:

```text
Ledger.Api
-> Ledger Database
-> Outbox
-> Ledger.OutboxPublisher
-> Message Broker
-> Consolidation.Worker
-> Consolidation Database
-> Consolidation.Api
```

## Execução local

Para subir apenas a infraestrutura usada por build, testes e desenvolvimento:

```powershell
docker compose up -d ledger-postgres consolidation-postgres rabbitmq
```

Para aplicar migrations explicitamente:

```powershell
docker compose run --rm ledger-migrations
docker compose run --rm consolidation-migrations
```

Para subir a solução local completa com APIs, workers, bancos e RabbitMQ:

```powershell
docker compose up -d --build ledger-api ledger-outbox-publisher consolidation-worker consolidation-api
```

URLs locais:

| Serviço | URL |
|---|---|
| Ledger.Api | `http://localhost:8080` |
| Consolidation.Api | `http://localhost:8081` |
| RabbitMQ Management | `http://localhost:15672` |

O RabbitMQ Management usa as credenciais locais de desenvolvimento `ledger` / `ledger`.
Mensagens inválidas do Consolidado são isoladas na fila `consolidation.entry-created.dlq`, ligada à exchange `consolidation.dlx` pela routing key `consolidation.entry-created.dead`.

Health checks das APIs:

```powershell
curl.exe http://localhost:8080/health/live
curl.exe http://localhost:8080/health/ready
curl.exe http://localhost:8081/health/live
curl.exe http://localhost:8081/health/ready
```

Fluxo manual end-to-end:

```powershell
$token = powershell -NoProfile -ExecutionPolicy Bypass -File scripts/generate-local-jwt.ps1 -MerchantId merchant-001

curl.exe -i -X POST http://localhost:8080/entries `
  -H "Authorization: Bearer $token" `
  -H "Idempotency-Key: idem-local-001" `
  -H "X-Correlation-Id: corr-local-001" `
  -H "Content-Type: application/json" `
  --data "{""type"":""CREDIT"",""amount"":""150.75"",""currency"":""BRL"",""occurredAt"":""2026-07-12T13:45:00Z"",""description"":""Venda local""}"

Start-Sleep -Seconds 5

curl.exe -i http://localhost:8081/daily-balances/2026-07-12 `
  -H "Authorization: Bearer $token" `
  -H "X-Correlation-Id: corr-local-001"
```

As migrations são executadas por serviços efêmeros do Compose (`ledger-migrations` e `consolidation-migrations`) antes dos serviços de aplicação. As aplicações não executam `Database.Migrate()` no startup.

## Decisões principais

As decisões arquiteturais estão registradas em `docs/decisions/`.

Principais decisões:

```text
- semântica do consolidado diário como movimento líquido do dia
- separação entre Lançamentos e Consolidado
- Outbox para publicação confiável
- consumo at-least-once com idempotência
- projeção materializada DailyBalance
- persistências independentes por fronteira
- PostgreSQL como referência relacional
- RabbitMQ como broker local de referência
- quatro unidades implantáveis
- .NET LTS, ASP.NET Core, Worker Service, PostgreSQL, RabbitMQ e containers
- Docker Compose para execução local
- segurança por autenticação, autorização, menor privilégio e secrets
- observabilidade, SLIs, SLOs, recuperação e prontidão operacional
```

## Próximos passos

```text
1. complementar validação de capacidade em ambiente produtivo ou equivalente declarado
2. adicionar observabilidade completa
3. evoluir retry/backoff avançado e operação produtiva de mensagens isoladas
4. completar reconstrução/reprocessamento operacional
5. endurecer autenticação/autorização para produção
6. preparar deploy produtivo/IaC
```

---

## Estado de implementação

A documentação arquitetural foi complementada com contratos e critérios de prontidão para implementação.

Itens adicionados:

```text
- contracts/openapi.yaml
- contracts/events/entry-created-v1.schema.json
- docs/architecture/08-implementation-readiness.md
- docs/decisions/ADR-0013-contratos-http-e-evento-entry-created-v1.md
```

No PR #4, o caminho inicial de escrita do Ledger materializa parte dessas decisões: `POST /entries`, persistência PostgreSQL do Ledger, idempotência de entrada, Outbox transacional, publicação RabbitMQ e testes automatizados.

O incremento de projeção do Consolidado materializa a persistência independente do Consolidado, `DailyBalance`, `ProcessedEvent`, processamento idempotente de `EntryCreated.v1`, consumo via RabbitMQ, `Consolidation.Api` e `GET /daily-balances/{businessDate}`.

A solução completa ainda não está pronta: reconstrução/reprocessamento operacional completo, observabilidade produtiva, retry/backoff avançado, hardening de segurança, deploy/IaC e validação de capacidade em ambiente produtivo ou equivalente permanecem pendentes. Health/readiness/liveness básicos das APIs HTTP já estão disponíveis em `GET /health/live` e `GET /health/ready`, a execução end-to-end local via Docker Compose já inclui APIs, workers, bancos e RabbitMQ, e mensagens inválidas do Consolidado já são isoladas em DLQ local.
