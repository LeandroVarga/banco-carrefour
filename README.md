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
- política básica de ack/nack do consumer implementada: sucesso, duplicado, erro de validação e JSON inválido com ack; erro desconhecido/transitório com nack/requeue
- Consolidation.Api implementada com GET /daily-balances/{businessDate}
- consulta do Consolidado deriva merchant_id do token e retorna 404 para projeção indisponível sem afirmar saldo zero
- testes de contrato e integração criados para contratos, Ledger write path, Outbox publisher, projeção, consumer e API do Consolidado
- CI container-first criado em .github/workflows/ci.yml
- validação prática de 50 RPS do Consolidado ainda pendente
- observabilidade completa, health/readiness/liveness, DLQ completa e deploy/IaC ainda pendentes
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
1. executar teste de carga do Consolidado para validar 50 RPS e critérios de falha
2. adicionar health/readiness/liveness e observabilidade completa
3. definir DLQ ou política operacional equivalente completa
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

A solução completa ainda não está pronta: reconstrução/reprocessamento operacional completo, observabilidade produtiva, health/readiness/liveness, DLQ completa, hardening de segurança, execução end-to-end dos serviços de aplicação via Compose, deploy/IaC e evidência de carga de 50 RPS permanecem pendentes.
