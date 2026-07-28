---
adr_id: ADR-0001
titulo: Fronteiras Ledger e Consolidation
status: Aceita
categoria: Domínio de negócio
altitude_decisoria: Fundacional
data: 2026-07-28
responsavel: Arquitetura de Soluções
---

# ADR-0001 — Fronteiras Ledger e Consolidation

## 1. Contexto

A solução separa o registro financeiro confiável da consulta do saldo diário. O registro de lançamentos não pode ficar indisponível por causa de uma falha na consolidação, e a consolidação precisa suportar pico de 50 RPS com no máximo 5% de falhas elegíveis. Sem uma fronteira explícita entre as duas responsabilidades, o caminho crítico de registro fica vulnerável a lentidão ou indisponibilidade da consulta consolidada.

## 2. Pergunta arquitetural

Como o registro financeiro autoritativo e a consolidação diária derivada são separados em responsabilidades independentes?

## 3. Decisão

O Ledger é a fronteira autoritativa: recebe créditos e débitos, valida a entrada, aplica idempotência por comerciante e chave, persiste o lançamento como fonte de verdade financeira e produz a informação necessária para atualizar o Consolidado.

O Consolidation é uma fronteira derivada: recebe as informações produzidas pelo Ledger, mantém uma projeção materializada (`DailyBalance`) por comerciante e data de negócio, e expõe a consulta do saldo diário. O Consolidation nunca é a fonte de verdade — é sempre reconstruível a partir dos lançamentos do Ledger.

A falha do Consolidation nunca impede o registro de novos lançamentos no Ledger. Não existe dependência síncrona do registro do Ledger sobre a disponibilidade do Consolidation; a integração entre as duas fronteiras é assíncrona (ver ADR-0004).

## 4. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar |
|---|---|---|
| Fronteira única para registro e consulta | Lançamentos e saldo diário sob a mesma responsabilidade. | Aumenta o acoplamento entre escrita financeira e leitura consolidada, dificultando o isolamento de falha exigido. |
| Chamada síncrona do Ledger para o Consolidation | O registro dependeria da atualização imediata do saldo. | Torna o registro sensível à indisponibilidade ou lentidão do Consolidation, violando a disponibilidade exigida do Ledger. |
| Saldo calculado sob demanda a partir dos lançamentos | Somar créditos e débitos a cada consulta. | Aumenta o custo de leitura e torna o desempenho dependente do volume histórico de lançamentos, dificultando sustentar 50 RPS previsíveis. |
| Fronteiras separadas com projeção materializada e reconstruível | Ledger como fonte de verdade; Consolidation como projeção derivada, atualizada de forma assíncrona. | Alternativa adotada. Atende ao isolamento de falha, à previsibilidade de leitura e à clareza de responsabilidades. |

## 5. Trade-offs

A separação introduz consistência eventual entre o registro e a consulta consolidada: um lançamento aceito pode levar um curto intervalo até refletir no saldo diário. Essa defasagem é aceita porque é observável e recuperável (ver ADR-0012), e porque a alternativa — acoplamento síncrono — contraria diretamente o requisito de disponibilidade do Ledger.

## 6. Consequências

O Ledger não realiza `JOIN` nem consulta direta às tabelas do Consolidation, e vice-versa — a única integração entre as fronteiras é o canal assíncrono (ADR-0004). O Consolidation pode ser reconstruído inteiramente a partir do histórico de lançamentos do Ledger, sem depender de nenhum estado interno anterior do próprio Consolidation.

## 7. Guardrails

- Nenhum caminho de código do Ledger bloqueia o registro de um lançamento aguardando resposta do Consolidation.
- `DailyBalance` nunca é tratado como fonte de verdade financeira em nenhuma parte da solução.
- Testes de arquitetura e testes de integração comprovam, por execução real, que o Ledger continua aceitando lançamentos com o Consolidation indisponível.

## 8. Risco arquitetural evitado

Uma implementação futura não deve acoplar o registro de lançamentos à disponibilidade do Consolidation, nem tratar `DailyBalance` como fonte de verdade em vez de projeção derivada.

## 9. ASRs relacionados

ASR-001 (Ledger disponível mesmo com falha do Consolidation), ASR-002/ASR-003 (50 RPS, ≤5% de falha), ASR-005 (defasagem observável e recuperável), ASR-008 (estrutura de leitura adequada para consulta).

## 10. ABBs e SBBs relacionados

ABB-001 (Fronteira de Lançamentos), ABB-002 (Fonte de Verdade Financeira), ABB-008 (Fronteira de Consolidado), ABB-010 (Projeção Materializada do Consolidado), ABB-012 (API de Consulta do Consolidado); SBB-001/SBB-006 (Ledger.Api/OutboxPublisher), SBB-008/SBB-012 (Consolidation.Worker/Api).

## 11. Evidências de implementação

`src/Ledger/Ledger.Application/RegisterFinancialEntry`, `src/Consolidation/Consolidation.Application/{ApplyFinancialEntry,GetDailyBalance}`, `scripts/release/verify-ledger-consolidation-isolation.sh` (prova executável de isolamento contra containers reais), `tests/Consolidation.IntegrationTests/SystemFlowIntegrationTests.cs`.

## 12. ADRs relacionados

ADR-0000 (semântica financeira e data de negócio), ADR-0002 (persistência PostgreSQL independente por fronteira), ADR-0004 (integração assíncrona confiável).
