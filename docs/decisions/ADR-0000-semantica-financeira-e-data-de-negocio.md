---
adr_id: ADR-0000
titulo: Semântica financeira e data de negócio
status: Aceita
categoria: Domínio de negócio
altitude_decisoria: Fundacional
data: 2026-07-28
responsavel: Arquitetura de Soluções
---

# ADR-0000 — Semântica financeira e data de negócio

## 1. Contexto

O Ledger registra lançamentos de débito e crédito de comerciantes. O Consolidado expõe o saldo diário derivado desses lançamentos. Ambos dependem de uma definição precisa de instante financeiro, data de negócio e do próprio significado do saldo diário — sem essa definição, cada componente poderia interpretar `businessDate`, fuso horário e efeito de débito/crédito de forma diferente.

## 2. Pergunta arquitetural

Como o tempo financeiro, a semântica de débito/crédito, a `businessDate` e o saldo diário consolidado são definidos de forma única para toda a solução?

## 3. Decisão

`occurredAt` é o instante em que o lançamento ocorreu, armazenado e transmitido em UTC, em timestamps RFC 3339. O Ledger deriva `businessDate` a partir de `occurredAt`, convertendo para o fuso de negócio fixo `America/Sao_Paulo` (`src/Ledger/Ledger.Domain/BusinessDate.cs`). O cliente nunca fornece `businessDate` diretamente com autoridade: o valor é sempre derivado pelo Ledger a partir do instante real do lançamento.

Crédito aumenta o saldo do comerciante na data de negócio; débito diminui. O saldo diário consolidado é o resultado líquido:

```text
saldo diário = total de créditos do dia - total de débitos do dia
```

O intervalo diário é a data de negócio completa em `America/Sao_Paulo`, do início ao fim do dia civil nesse fuso. O consolidado é uma visão derivada dos lançamentos, nunca a fonte de verdade financeira, e é sempre recalculável a partir do Ledger.

Lançamentos retroativos (com `occurredAt` em uma data de negócio já passada) são aceitos e atualizam o saldo consolidado da data correspondente. Lançamentos com `occurredAt` no futuro não são tratados neste escopo — o MVP assume que o cliente relata o instante real do lançamento, não uma data de negócio futura planejada.

A identidade do comerciante é sempre obtida do contexto autenticado (claim `merchant_id` do token), nunca de um campo informado livremente pelo chamador — essa é a fronteira que garante que um lançamento ou uma consulta pertence ao comerciante correto (ver ADR-0007).

## 4. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar |
|---|---|---|
| `businessDate` derivada de UTC bruto | Usar a data do timestamp UTC diretamente. | Desalinha o fechamento do dia comercial do horário real de operação do comerciante no Brasil, próximo à meia-noite. |
| `businessDate` informada pelo cliente | Aceitar a data de negócio enviada na requisição. | Permite que um cliente registre um lançamento em qualquer data arbitrária, quebrando a integridade temporal do saldo diário. |
| Fuso horário por comerciante | Permitir fuso configurável por comerciante desde o início. | Aumenta a complexidade do MVP sem requisito concreto; fora do escopo inicial (ver seção 6). |
| Saldo acumulado histórico como semântica principal | Calcular saldo contábil acumulado desde a abertura da conta. | Exige plano de contas, saldo inicial e regras contábeis não informadas pelo desafio. |
| Movimento líquido diário por comerciante e data, fuso fixo `America/Sao_Paulo` | Crédito menos débito, agrupado por comerciante e data de negócio derivada de um único fuso fixo. | Alternativa adotada. Atende ao relatório diário do desafio com semântica única e sem ambiguidade. |

## 5. Trade-offs

O fuso fixo simplifica a implementação, mas não atende comerciantes fora do fuso de referência sem uma decisão futura dedicada. A derivação de `businessDate` no Ledger centraliza a responsabilidade temporal em um único componente, exigindo que todo consumidor confie nessa derivação em vez de recalculá-la.

## 6. Consequências

Fica fora do escopo atual: saldo bancário acumulado, saldo contábil formal, saldo de liquidação, fechamento manual de caixa, conciliação bancária, estornos e cancelamentos, múltiplas moedas (BRL é a única moeda do MVP) e múltiplos fusos horários por comerciante. Esses temas podem ser tratados em decisões futuras dedicadas, sem alterar esta ADR.

## 7. Guardrails

- `businessDate` nunca é aceita como entrada explícita do cliente — é sempre derivada de `occurredAt`.
- A conversão de fuso horário usa exclusivamente `America/Sao_Paulo` em toda a solução, nunca UTC bruto nem um fuso variável não decidido.
- `merchant_id` nunca é aceito de payload, query string ou header controlado pelo cliente — sempre do token autenticado.
- Mudança de semântica de saldo (crédito/débito) exige nova ADR, nunca alteração silenciosa de código.

## 8. Risco arquitetural evitado

Uma implementação futura não deve derivar `businessDate` diretamente de UTC, confiar em uma data de negócio fornecida pelo cliente, nem alterar a semântica de saldo de débito/crédito de forma independente desta decisão.

## 9. ASRs relacionados

ASR-004 (lançamentos confiáveis), ASR-006 (idempotência de entrada), ASR-009 (acesso autenticado e autorizado por comerciante), ASR-012 (semântica financeira consistente).

## 10. ABBs e SBBs relacionados

ABB-001 (Fronteira de Lançamentos), ABB-002 (Fonte de Verdade Financeira), ABB-010 (Projeção Materializada do Consolidado); SBB-002 (Ledger Database), SBB-011 (DailyBalance).

## 11. Evidências de implementação

`src/Ledger/Ledger.Domain/BusinessDate.cs` (conversão real para `America/Sao_Paulo`), `src/Ledger/Ledger.Domain/FinancialEntry.cs`, `src/Consolidation/Consolidation.Domain/DailyBalance.cs`, `tests/Ledger.Domain.Tests/FinancialEntryTests.cs`, `tests/Consolidation.Domain.Tests/DailyBalanceTests.cs`.

## 12. ADRs relacionados

ADR-0001 (fronteiras Ledger e Consolidation), ADR-0005 (contratos HTTP e eventos), ADR-0007 (identidade, autorização e isolamento por merchant).
