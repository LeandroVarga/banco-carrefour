---
adr_id: ADR-0000
titulo: Semântica do Consolidado Diário
status: Aceita
data: 2026-07-10
responsavel: Arquitetura de Soluções
decisao_relacionada: Semântica de negócio
---

# ADR-0000 — Semântica do Consolidado Diário

## 1. Contexto

O desafio informa que um comerciante precisa controlar seu fluxo de caixa diário com lançamentos de débito e crédito.

Também informa que o comerciante precisa de um relatório que disponibilize o saldo diário consolidado.

O termo consolidado diário pode ter diferentes interpretações em contextos financeiros, como movimento líquido do dia, saldo acumulado, saldo contábil, saldo de liquidação ou fechamento formal de caixa.

Para orientar a arquitetura e evitar ambiguidade no desenho da solução, a semântica inicial do consolidado diário precisa ser definida.

---

## 2. Decisão

O consolidado diário representa, no escopo inicial, o movimento líquido dos lançamentos de um comerciante em uma data.

```text
saldo diário = total de créditos do dia - total de débitos do dia
```

O consolidado diário é uma visão derivada dos lançamentos registrados.

Ele não substitui a fonte de verdade financeira.

---

## 3. O que a decisão inclui

Esta decisão inclui:

```text
- cálculo por comerciante e data
- totalização de créditos
- totalização de débitos
- cálculo do saldo diário
- quantidade de lançamentos considerados
- data e hora da última atualização da visão consolidada
```

---

## 4. O que fica fora do escopo inicial

Esta decisão não cobre:

```text
- saldo bancário acumulado
- saldo contábil formal
- saldo de liquidação
- fechamento manual de caixa
- conciliação bancária
- estornos e cancelamentos
- múltiplas moedas
- múltiplos fusos horários
- posição financeira histórica acumulada
```

Esses temas podem ser tratados em decisões futuras, caso passem a fazer parte do escopo da solução.

---

## 5. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar no escopo inicial |
|---|---|---|
| Movimento líquido do dia | Créditos menos débitos agrupados por comerciante e data. | Alternativa adotada. Atende diretamente ao relatório diário do desafio com menor complexidade inicial. |
| Saldo acumulado | Saldo histórico acumulado até determinada data. | Exige definição de saldo inicial, histórico anterior, correções, fechamento e possíveis regras contábeis. |
| Saldo contábil formal | Saldo calculado segundo regras contábeis específicas. | Exige regras contábeis, plano de contas, eventos contábeis e critérios regulatórios não informados no desafio. |
| Saldo de liquidação | Saldo baseado em liquidação financeira efetiva. | Exige integração com ciclo de liquidação, compensação ou sistemas externos. |
| Fechamento manual de caixa | Saldo fechado por ação manual do comerciante ou operação. | Exige fluxo de fechamento, reabertura, auditoria e autorização específica. |

---

## 6. Consequências

Consequências positivas:

```text
- reduz ambiguidade sobre o significado do relatório diário
- simplifica o modelo inicial de domínio
- permite consolidado derivado a partir dos lançamentos
- permite reconstrução da visão consolidada
- facilita consulta por comerciante e data
```

Consequências e limitações:

```text
- não resolve saldo acumulado histórico
- não resolve fechamento formal de caixa
- não resolve estornos e cancelamentos
- não resolve liquidação financeira
- exige nova decisão caso o conceito de saldo evolua
```

---

## 7. Relação com documentos

Esta decisão sustenta:

```text
- docs/architecture/01-contexto-de-negocio.md
- docs/architecture/02-requisitos-arquiteturais.md
- docs/architecture/03-blocos-de-arquitetura.md
```

---

## 8. Status

Decisão aceita para o escopo inicial da solução.
