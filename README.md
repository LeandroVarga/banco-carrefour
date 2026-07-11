# banco-carrefour

Case de arquitetura de soluções para o desafio Banco Carrefour.

## Objetivo

Este repositório documenta e materializa uma solução para controle de fluxo de caixa diário de comerciantes, com registro de lançamentos de débito e crédito e consulta de consolidado diário.

A arquitetura prioriza confiabilidade do registro financeiro, disponibilidade do serviço de Lançamentos, consulta eficiente do Consolidado, segurança, observabilidade, recuperação e decisões arquiteturais rastreáveis.

## Situação atual

Status do trabalho:

```text
- documentação arquitetural principal criada
- ADRs criados de ADR-0000 a ADR-0012
- arquitetura, segurança e operação documentadas
- implementação ainda pendente
- testes automatizados ainda pendentes
- execução local ainda pendente
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
1. revisar documentação criada
2. consolidar eventuais ajustes finais
3. iniciar implementação
4. criar testes automatizados
5. criar execução local com Docker Compose
6. validar evidências operacionais e de carga
```
