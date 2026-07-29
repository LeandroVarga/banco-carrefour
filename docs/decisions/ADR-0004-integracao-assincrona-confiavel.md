---
adr_id: ADR-0004
titulo: Integração assíncrona confiável
status: Aceita
categoria: Mensageria e confiabilidade
altitude_decisoria: Fundacional
data: 2026-07-28
responsavel: Arquitetura de Soluções
---

# ADR-0004 — Integração assíncrona confiável

## 1. Contexto

O Ledger precisa notificar o Consolidation sobre novos lançamentos sem depender da disponibilidade imediata do canal de mensageria nem do próprio Consolidation, e sem perder um lançamento já aceito. O canal assíncrono pode entregar uma mensagem mais de uma vez, e o consumidor precisa lidar com isso sem duplicar efeito no saldo diário.

## 2. Pergunta arquitetural

Como o Ledger notifica o Consolidation de forma assíncrona sem perder lançamentos aceitos nem assumir entrega exactly-once?

## 3. Decisão

O Ledger registra a intenção de publicação na mesma transação do lançamento financeiro — o padrão Outbox. `FinancialEntry`, `InputIdempotency` e o registro de Outbox são gravados atomicamente. A publicação real ocorre depois, fora dessa transação, por um publicador dedicado (`Ledger.OutboxPublisher`).

O claim de mensagens pendentes usa `FOR UPDATE SKIP LOCKED`, marca a mensagem como `Processing`, registra `locked_by`/`locked_at` e permite mais de uma instância do publicador sem duas instâncias reclamarem a mesma mensagem. A transação de claim é curta; a publicação no canal assíncrono ocorre fora dela — nunca dentro da mesma transação de banco que reivindica o lote. Falha de publicação libera a mensagem para nova tentativa via `next_attempt_at`.

O canal assíncrono é Amazon SQS Standard com DLQ na AWS de referência, e SQS-compatível via LocalStack na execução local — o mesmo comportamento de ack, visibility timeout, redrive policy e long polling em ambos os ambientes. `Ledger.OutboxPublisher` envia a mensagem para a fila; `Consolidation.Worker` consome por long polling.

A entrega é at-least-once: uma mensagem pode chegar mais de uma vez. `Consolidation.Worker` registra cada evento processado (`ProcessedEvent`, chave única de evento) e aplica o efeito financeiro de forma idempotente, por operação atômica no banco — reentrega não duplica o efeito no saldo diário. Não há garantia de ordenação global entre eventos; o saldo diário é calculado a partir de lançamentos imutáveis, aplicados de forma idempotente, o que torna a ordem de aplicação irrelevante para o resultado final. `DeleteMessage` só ocorre depois da aplicação bem-sucedida (mensagem processada ou já aplicada anteriormente); mensagens inválidas ou com falha transitória não são excluídas e seguem a política de redrive para a DLQ.

Essa integração assíncrona é o que permite ao Ledger permanecer disponível quando o Consolidation falha (ADR-0001): o lançamento é aceito e persistido independentemente do estado do canal ou do consumidor.

## 4. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar |
|---|---|---|
| Publicar o evento diretamente na requisição, sem Outbox | A API publicaria o evento no mesmo fluxo síncrono do registro. | Deixa uma janela de perda entre gravar o lançamento e publicar, e torna o registro sensível à disponibilidade do canal. |
| Transação distribuída entre banco e broker | Banco e broker participando de uma transação coordenada. | Aumenta complexidade e acoplamento operacional entre componentes distintos sem necessidade concreta. |
| Entrega exactly-once como premissa arquitetural | Depender de uma garantia estrita de entrega única fim a fim. | Aumenta complexidade tecnológica e não elimina a necessidade de idempotência em cenários reais de falha e retry. |
| RabbitMQ como broker local | Broker local com semântica própria de exchanges, filas e acknowledgement. | Rejeitada. RabbitMQ introduziria um segundo comportamento de mensageria divergente do alvo AWS, reduzindo a paridade comportamental entre execução local e referência AWS (ack, retry, DLQ e redrive funcionam de forma diferente de SQS). RabbitMQ não é o broker da solução definitiva — não é o Message Broker do ambiente local nem da referência AWS. |
| Amazon SQS Standard com DLQ (real na AWS, compatível via LocalStack localmente) | Mesmo comportamento de fila, ack, visibility timeout e redrive em ambos os ambientes. | Alternativa adotada. Outbox com claim recuperável elimina o risco de perda entre registro e publicação; SQS mantém paridade comportamental entre execução local e AWS. |

## 5. Trade-offs

A solução introduz consistência eventual entre o Ledger e o Consolidation e exige que o consumidor seja idempotente e que a Outbox seja monitorada quanto a backlog e mensagens isoladas. Em contrapartida, o registro financeiro nunca fica bloqueado pela disponibilidade do canal ou do consumidor, e a reentrega de mensagens nunca duplica efeito no saldo diário.

## 6. Consequências

A tabela de Outbox ganha colunas operacionais de claim (`status`, `locked_by`, `locked_at`, `next_attempt_at`, `attempts`). `ProcessedEvent` no Consolidation cresce proporcionalmente ao volume de eventos recebidos e exige política de retenção. O tempo de claim precisa ser calibrado conforme latência e volume observados em produção.

## 7. Guardrails

- Nenhuma publicação ocorre dentro da mesma transação de banco que grava o lançamento financeiro ou que reivindica o lote de claim.
- Todo consumo é idempotente por chave única de evento (`ProcessedEvent`), nunca aplicado sem essa verificação.
- `DeleteMessage` só ocorre após confirmação de aplicação bem-sucedida ou de evento já processado.
- RabbitMQ nunca é reintroduzido como broker ativo — testes de arquitetura impedem referência a `RabbitMQ` em `src/`.

## 8. Risco arquitetural evitado

Uma implementação futura não deve publicar fora da transação do Ledger de forma insegura, assumir entrega exactly-once sem idempotência, nem reintroduzir RabbitMQ como broker ativo local ou de referência.

## 9. ASRs relacionados

ASR-001 (Ledger disponível mesmo com falha do Consolidation), ASR-005 (defasagem observável e recuperável), ASR-006 (idempotência de entrada), ASR-007 (eventos duplicados não duplicam efeito), ASR-010 (fluxo observável), ASR-011 (falhas recuperáveis).

## 10. ABBs e SBBs relacionados

ABB-005 (Outbox Durável), ABB-006 (Publicação Recuperável), ABB-007 (Canal Assíncrono Confiável), ABB-009 (Consumo Idempotente), ABB-014 (Recuperação Operacional); SBB-005 (Outbox), SBB-006 (Ledger.OutboxPublisher), SBB-007 (Message Broker — materializado por SQS), SBB-008 (Consolidation.Worker), SBB-010 (Processed Events).

## 11. Evidências de implementação

`src/Ledger/Ledger.Infrastructure/Outbox/{PostgresOutboxStore,SqsIntegrationEventPublisher}.cs`, `src/Consolidation/Consolidation.Worker/Sqs/{SqsFinancialEntryConsumer,FinancialEntryMessageParser}.cs`, `src/Consolidation/Consolidation.Infrastructure/DailyBalances/EfDailyBalanceProjectionStore.cs`, `infra/terraform/modules/messaging`, `tests/Ledger.Application.Tests/PublishPendingEventsUseCaseTests.cs`, `tests/Consolidation.IntegrationTests/SqsFinancialEntryConsumerIntegrationTests.cs`, `scripts/release/verify-ledger-consolidation-isolation.sh`.

## 12. ADRs relacionados

ADR-0001 (fronteiras Ledger e Consolidation), ADR-0003 (arquitetura hexagonal), ADR-0005 (contratos HTTP e eventos de integração), ADR-0010 (execução local e paridade comportamental), ADR-0012 (observabilidade e objetivos operacionais).
