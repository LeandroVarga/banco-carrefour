---
adr_id: ADR-0004
titulo: Projeção Materializada do Consolidado
status: Aceita
data: 2026-07-10
responsavel: Arquitetura de Soluções
decisao_relacionada: Modelo de leitura do Consolidado
---

# ADR-0004 — Projeção Materializada do Consolidado

## 1. Contexto

O desafio exige que o comerciante consulte um relatório com o saldo diário consolidado.

Também informa que, em picos, o serviço de Consolidado recebe 50 requisições por segundo, com no máximo 5% de perda de requisições.

A solução separa Lançamentos e Consolidado em fronteiras distintas.

Lançamentos é a fonte de verdade financeira. Consolidado é uma visão derivada para consulta.

Se o Consolidado calcular o saldo diário lendo e somando todos os lançamentos a cada consulta, a leitura pode ficar mais cara, menos previsível e mais acoplada ao volume histórico de lançamentos.

Para atender melhor ao fluxo de consulta, a solução precisa de uma estrutura preparada para leitura por comerciante e data.

---

## 2. Decisão

A solução adotará uma projeção materializada para o Consolidado diário.

Essa projeção manterá uma visão calculada por comerciante e data, contendo os principais dados do relatório diário.

No escopo inicial, a projeção deve conter:

```text
- comerciante
- data de negócio
- total de créditos
- total de débitos
- saldo diário
- quantidade de lançamentos considerados
- data e hora da última atualização
```

A projeção será atualizada a partir dos eventos de lançamentos processados pelo Consolidado.

O Consolidado permanece como visão derivada e reconstruível a partir da fonte de verdade financeira.

---

## 3. O que a decisão inclui

Esta decisão inclui:

```text
- manter uma visão de leitura por comerciante e data
- evitar recálculo completo do consolidado a cada consulta
- atualizar a visão consolidada a partir do fluxo assíncrono
- permitir consulta direta do relatório diário
- permitir reconstrução da projeção quando necessário
- manter rastreabilidade entre projeção, lançamentos e eventos processados
```

---

## 4. O que fica fora desta decisão

Esta decisão não define:

```text
- tecnologia final de persistência
- estrutura física definitiva das tabelas
- índices finais
- estratégia final de cache
- política de retenção histórica
- estratégia completa de rebuild em produção
- metas finais de latência da consulta
```

Esses pontos serão detalhados nos blocos de solução, na arquitetura operacional e em decisões posteriores.

---

## 5. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar como base da solução |
|---|---|---|
| Calcular sob demanda a partir dos lançamentos | A consulta do Consolidado somaria créditos e débitos lendo os lançamentos a cada requisição. | Mantém o modelo simples, mas aumenta custo de leitura e torna o desempenho mais dependente do volume de lançamentos. |
| Usar apenas cache de consulta | O resultado seria calculado e armazenado temporariamente em cache. | Pode melhorar leituras repetidas, mas não substitui uma visão consolidada rastreável e reconstruível. |
| Gerar relatório em lote | O Consolidado seria atualizado apenas por processo batch periódico. | Pode ser útil em cenários de fechamento, mas reduz atualidade da informação e não atende tão bem à consulta operacional contínua. |
| Projeção materializada do Consolidado | A visão diária é mantida previamente por comerciante e data, sendo atualizada conforme os lançamentos são processados. | Alternativa adotada. Favorece consulta eficiente, rastreabilidade e reconstrução controlada. |

---

## 6. Consequências

Consequências positivas:

```text
- melhora previsibilidade da consulta do Consolidado
- reduz necessidade de recalcular saldo a cada requisição
- favorece atendimento ao pico de 50 RPS informado no desafio
- mantém o Consolidado separado da fonte de verdade financeira
- permite reconstrução da visão consolidada a partir dos lançamentos
- facilita observabilidade sobre atualização, atraso e status da projeção
```

Consequências e tradeoffs:

```text
- introduz consistência eventual entre registro e consulta
- exige processamento assíncrono confiável
- exige controle de idempotência no consumo e atualização atômica da projeção para evitar lost update
- exige estratégia de reconstrução da projeção
- exige monitoramento de atraso, falhas e atualização da visão consolidada
- adiciona uma estrutura de leitura derivada para manter e operar
```

---

## 7. Relação com requisitos e ABBs

Esta decisão sustenta principalmente:

```text
- RNF-002: Consolidado deve suportar 50 RPS em pico
- RNF-003: Consolidado deve limitar falhas ou perdas de requisições a no máximo 5% no pico
- ASR-002: Consolidado deve suportar 50 RPS em pico
- ASR-003: Consolidado deve limitar falhas ou perdas de requisições a no máximo 5% no pico
- ASR-005: Consolidado pode ficar temporariamente defasado, desde que observável e recuperável
- ASR-008: A consulta do Consolidado deve usar uma estrutura adequada para leitura
- ABB-008: Fronteira de Consolidado
- ABB-010: Projeção Materializada do Consolidado
- ABB-011: Persistência do Consolidado
- ABB-012: API de Consulta do Consolidado
- ABB-013: Observabilidade do Fluxo
- ABB-014: Recuperação Operacional
```

---

## 8. Relação com documentos

Esta decisão sustenta:

```text
- docs/architecture/01-contexto-de-negocio.md
- docs/architecture/02-requisitos-arquiteturais.md
- docs/architecture/03-blocos-de-arquitetura.md
```

---

## 9. Status

Decisão aceita para o escopo inicial da solução.
