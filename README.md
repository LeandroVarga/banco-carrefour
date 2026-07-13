# banco-carrefour

Case de arquitetura de soluções para o desafio Banco Carrefour.

## Objetivo

Este repositório documenta e materializa uma solução para controle de fluxo de caixa diário de comerciantes, com registro de lançamentos de débito e crédito e consulta de consolidado diário.

A arquitetura prioriza confiabilidade do registro financeiro, disponibilidade do serviço de Lançamentos, consulta eficiente do Consolidado, segurança, observabilidade, recuperação e decisões arquiteturais rastreáveis.

## Situação atual

Status do trabalho:

```text
- documentação arquitetural principal criada
- ADRs criados de ADR-0000 a ADR-0014
- arquitetura, segurança e operação documentadas
- baseline .NET container-first criado
- Ledger.Api implementada para registro de lançamentos
- POST /entries implementado com autenticação JWT local validando assinatura, expiração, issuer e audience; merchant_id derivado do token, idempotência de entrada, fingerprint canônico e Outbox transacional
- Ledger.OutboxPublisher implementado com RabbitMQ, publish confirm e mandatory routing
- Consolidation.Persistence implementado com DailyBalance e ProcessedEvent
- Consolidation.Application implementado com EntryCreatedProjectionProcessor, aplicação atômica de CREDIT/DEBIT no DailyBalance e deduplicação por eventId
- Consolidation.Worker implementado consumindo EntryCreated.v1 via RabbitMQ
- política básica de consumo do Consolidado implementada: sucesso e duplicado com ack; JSON inválido e erro de validação encaminhados para DLQ com publicação confirmada e roteada antes do ack; erro desconhecido/transitório com retry local finito confirmado e roteado antes do ack e DLQ após exceder o limite
- Consolidation.Api implementada com GET /daily-balances/{businessDate}
- consulta do Consolidado deriva merchant_id do token e retorna 404 para projeção indisponível sem afirmar saldo zero
- testes de contrato e integração criados para contratos, Ledger write path, Outbox publisher, projeção, consumer e API do Consolidado
- CI container-first criado em .github/workflows/ci.yml
- teste de carga local/container-first do Consolidado executado com JWT local contendo issuer/audience: 3000 requisições planejadas/executadas na janela sustentada, 50.02 req/s, 0% falhas, p95 5.80 ms, p99 7.51 ms e validação de throughput mínimo observado de 50 RPS
- health/readiness/liveness básicos das APIs HTTP implementados
- rate limiting básico local/in-memory implementado nos endpoints de negócio das APIs HTTP, com resposta 429 padronizada
- execução end-to-end local via Docker Compose com APIs, workers, bancos e RabbitMQ implementada
- instrumentação OpenTelemetry básica implementada com logs estruturados, traces customizados, métricas customizadas e OTLP configurável
- Aspire Dashboard local adicionado ao Docker Compose para demonstração de logs, traces e métricas
- rate limiting distribuído/produtivo, observabilidade produtiva completa, operação produtiva de mensagens isoladas, hardening produtivo e deploy/IaC ainda pendentes
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
| Runbook de demonstração local | `docs/operations/runbook-demonstracao-local.md` |
| Evidências do case | `docs/operations/evidencias-do-case.md` |
| Teste de carga do Consolidado | `docs/operations/teste-de-carga-consolidado.md` |
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

O roteiro completo para avaliação local está em `docs/operations/runbook-demonstracao-local.md`.

A execução é container-first: os serviços rodam em containers via Docker Compose e não exigem .NET SDK local, PowerShell 7, Python, Node, OpenSSL ou ferramenta externa para JWT. Windows, Linux e macOS são suportados desde que Docker/Compose estejam disponíveis; o runbook traz exemplos para PowerShell e Bash/Zsh.
O helper `local-jwt` emite tokens locais HS256 com `iss`, `aud`, `exp` e `merchant_id` compatíveis com as APIs.

Para subir apenas a infraestrutura usada por build, testes e desenvolvimento:

```powershell
docker compose up -d ledger-postgres consolidation-postgres rabbitmq
```

Para aplicar migrations explicitamente:

```powershell
docker compose run --rm ledger-migrations
docker compose run --rm consolidation-migrations
```

Para subir a solução local completa com APIs, workers, bancos, RabbitMQ e Aspire Dashboard:

```powershell
docker compose up -d --build ledger-api ledger-outbox-publisher consolidation-worker consolidation-api aspire-dashboard
```

URLs locais:

| Serviço | URL |
|---|---|
| Ledger.Api | `http://localhost:8080` |
| Consolidation.Api | `http://localhost:8081` |
| RabbitMQ Management | `http://localhost:15672` |
| Aspire Dashboard | `http://localhost:18888` |

O RabbitMQ Management usa as credenciais locais de desenvolvimento `ledger` / `ledger`.
Mensagens inválidas do Consolidado são isoladas na fila `consolidation.entry-created.dlq`, ligada à exchange `consolidation.dlx` pela routing key `consolidation.entry-created.dead`.
Erros desconhecidos ou transitórios do `Consolidation.Worker` são encaminhados para a fila `consolidation.entry-created.retry` pela exchange `consolidation.retry`; após `RabbitMq__MaxRetryAttempts` tentativas, a mensagem é isolada na DLQ.
O worker só confirma a mensagem original depois que a republicação para retry ou DLQ é confirmada pelo broker e roteada; se essa republicação falhar, a original permanece reprocessável por `nack` com requeue.

O Aspire Dashboard local recebe OTLP das aplicações no Compose por `http://aspire-dashboard:18889`. No host, as portas publicadas são:

```text
- UI: http://localhost:18888
- OTLP/gRPC: http://localhost:4317
- OTLP/HTTP: http://localhost:4318
```

Uso operacional local para logs:

```powershell
docker compose logs -f ledger-api
docker compose logs -f ledger-outbox-publisher
docker compose logs -f consolidation-worker
docker compose logs -f consolidation-api
```

Rate limiting local das APIs:

```text
- POST /entries e GET /daily-balances/{businessDate} possuem rate limiting básico local/in-memory.
- O limite padrão é permissivo para não interferir no teste local de 50 RPS do Consolidado.
- Excesso de requisições retorna HTTP 429 no padrão ErrorResponse, preservando correlationId quando informado.
- /health/live e /health/ready não aplicam rate limit.
- Esse baseline não substitui rate limiting distribuído em API Gateway, WAF, ingress ou service mesh.
```

Health checks das APIs:

Windows/PowerShell:

```powershell
curl.exe http://localhost:8080/health/live
curl.exe http://localhost:8080/health/ready
curl.exe http://localhost:8081/health/live
curl.exe http://localhost:8081/health/ready
```

Fluxo manual end-to-end:

Exemplo Windows/PowerShell. Para Linux/macOS, use os comandos Bash/Zsh do runbook.

```powershell
$token = docker compose run --rm local-jwt --merchant-id merchant-001

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

Após executar o fluxo, abra `http://localhost:18888` para inspecionar logs, traces e métricas locais. Essa UI é apenas demonstração local/dev e não substitui plataforma produtiva de observabilidade, alertas, retenção ou dashboards operacionais.

As migrations são executadas por serviços efêmeros do Compose (`ledger-migrations` e `consolidation-migrations`) antes dos serviços de aplicação. As aplicações não executam `Database.Migrate()` no startup.

## Decisões principais

As decisões arquiteturais estão registradas em `docs/decisions/`.

Para avaliação do case, a matriz `docs/operations/evidencias-do-case.md` relaciona requisitos, evidências, status e limitações preservadas.

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
- OpenTelemetry como padrão vendor-neutral de instrumentação
```

## Próximos passos

```text
1. complementar validação de capacidade em ambiente produtivo ou equivalente declarado
2. evoluir observabilidade produtiva completa
3. evoluir operação produtiva de mensagens isoladas e backoff avançado
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
- docs/decisions/ADR-0014-instrumentacao-de-observabilidade-com-opentelemetry.md
```

No estado atual da main, o baseline local/container-first implementa `Ledger.Api`, `Ledger.OutboxPublisher`, `Consolidation.Worker`, `Consolidation.Api`, persistências PostgreSQL separadas, Outbox transacional, publicação RabbitMQ com confirm e mandatory routing, consumo idempotente do `EntryCreated.v1`, retry/DLQ do Consolidado com republicação confirmada e roteada antes do ack da original, `DailyBalance` com upsert atômico, JWT local com assinatura, expiração, issuer e audience, rate limiting básico local/in-memory, health/readiness/liveness das APIs, OpenTelemetry com Aspire Dashboard local, testes automatizados e teste de carga local/container-first.

Esse baseline está pronto para entrega do desafio técnico como demonstração local reproduzível. Ele não representa prontidão produtiva completa: rate limiting distribuído/produtivo, reconstrução/reprocessamento operacional completo, re-drive assistido da DLQ, observabilidade produtiva, operação produtiva de mensagens isoladas, backoff avançado, OIDC/TLS/mTLS/secret manager, deploy/IaC, validação produtiva de múltiplos workers/backlog/autoscaling e validação de capacidade em ambiente produtivo ou equivalente permanecem pendentes e documentados.
