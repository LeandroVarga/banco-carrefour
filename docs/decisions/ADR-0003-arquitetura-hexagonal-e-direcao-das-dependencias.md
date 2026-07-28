---
adr_id: ADR-0003
titulo: Arquitetura hexagonal e direção das dependências
status: Aceita
categoria: Estrutura de código
altitude_decisoria: Fundacional
data: 2026-07-28
responsavel: Arquitetura de Soluções
---

# ADR-0003 — Arquitetura hexagonal e direção das dependências

## 1. Contexto

O fluxo inicial do Ledger concentrava validação de domínio, idempotência, transação EF Core e serialização de evento diretamente no endpoint HTTP, dificultando testar regras de negócio sem infraestrutura real e sem uma direção explícita de dependências. O mesmo risco existia no Publisher e no Consolidation.

## 2. Pergunta arquitetural

Como a lógica de negócio deve depender da infraestrutura e da tecnologia de hospedagem?

## 3. Decisão

Ledger, Ledger.OutboxPublisher e Consolidation adotam Arquitetura Hexagonal, com camadas explícitas e direção única de dependências:

```text
Domain <- Application <- Infrastructure
Application <- Api / Worker / OutboxPublisher (hosts)
Infrastructure <- Api / Worker / OutboxPublisher (hosts)
```

`Domain` contém os conceitos e invariantes de negócio (`FinancialEntry`, `DailyBalance`, dinheiro, tipo, comerciante, data de negócio), sem dependência de EF Core, Npgsql, SDK da AWS, mensageria ou ASP.NET Core. `Application` contém os casos de uso (`IRegisterFinancialEntryUseCase`, `IPublishPendingEventsUseCase`, `IApplyFinancialEntryUseCase`, `IGetDailyBalanceUseCase`) e as portas de saída (`IFinancialEntryRegistrationStore`, `IOutboxStore`, `IIntegrationEventPublisher`, `IDailyBalanceReader`), também independente de infraestrutura concreta. `Infrastructure` implementa as portas com EF Core, Npgsql e o SDK de mensageria. Os hosts (`Ledger.Api`, `Ledger.OutboxPublisher`, `Consolidation.Api`, `Consolidation.Worker`) são adaptadores de entrada e composition roots — resolvem as implementações concretas das portas e nunca contêm regra de negócio.

## 4. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar |
|---|---|---|
| Endpoint HTTP com acesso direto ao `DbContext` | Regra de domínio e persistência acopladas ao HTTP. | Impede testar regras de negócio sem infraestrutura real e mistura responsabilidades de camada. |
| Repositórios genéricos | Abstração de persistência genérica sobre qualquer entidade. | Não representa a garantia transacional real exigida (registrar `FinancialEntry`, `InputIdempotency` e Outbox atomicamente). |
| MediatR ou CQRS cerimonial | Mediação de comandos/queries por biblioteca dedicada. | Adiciona indireção sem necessidade concreta no escopo atual. |
| Projeto `Common`/`SharedKernel` entre Ledger e Consolidation | Código compartilhado entre as duas fronteiras. | Acopla prematuramente fronteiras que devem evoluir de forma independente (ADR-0001). |
| Arquitetura Hexagonal com Domain/Application/Infrastructure e hosts como composition roots | Direção única de dependências, portas explícitas de entrada e saída. | Alternativa adotada. Torna o domínio testável isoladamente e explicita a direção de dependência. |

## 5. Trade-offs

O padrão aumenta o número de projetos na solução e exige disciplina para não vazar tipos de infraestrutura para `Application`/`Domain`. Em contrapartida, o domínio financeiro passa a ser testável sem Docker, PostgreSQL ou mensageria.

## 6. Consequências

`POST /entries` e os demais endpoints deixam de receber `DbContext` diretamente. `Ledger.Application`, `Consolidation.Application` e as camadas `Domain` correspondentes seguem sem qualquer referência a EF Core, Npgsql, SDK da AWS ou ASP.NET Core — garantido por testes de arquitetura, não apenas por convenção.

## 7. Guardrails

- Testes de arquitetura impedem que `Domain`/`Application` referenciem Entity Framework Core, Npgsql, pacotes `Amazon.*` ou ASP.NET Core.
- Testes de arquitetura impedem referência cruzada direta entre os assemblies de Ledger e Consolidation.
- Contratos externos (`contracts/`) nunca dependem do `Domain` interno.

## 8. Risco arquitetural evitado

Uma implementação futura não deve acoplar regra de domínio ou de aplicação diretamente a EF Core, HTTP, SDKs da AWS, clientes de mensageria ou frameworks de hospedagem.

## 9. ASRs relacionados

ASR-004 (lançamentos confiáveis), ASR-006 (idempotência de entrada), ASR-012 (semântica financeira consistente e testável).

## 10. ABBs e SBBs relacionados

ABB-001 a ABB-006 (fronteira, fonte de verdade, persistência transacional, idempotência, Outbox, publicação recuperável); SBB-001, SBB-006, SBB-008, SBB-012 (unidades implantáveis).

## 11. Evidências de implementação

`src/Ledger/{Ledger.Domain,Ledger.Application,Ledger.Infrastructure}`, `src/Consolidation/{Consolidation.Domain,Consolidation.Application,Consolidation.Infrastructure}`, `tests/Architecture.Tests/{LedgerArchitectureTests,ConsolidationArchitectureTests,LedgerPublisherArchitectureTests}.cs`.

## 12. ADRs relacionados

ADR-0001 (fronteiras Ledger e Consolidation), ADR-0004 (integração assíncrona confiável), ADR-0006 (unidades implantáveis e topologia de runtime).
