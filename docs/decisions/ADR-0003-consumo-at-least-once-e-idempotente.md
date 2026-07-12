---
adr_id: ADR-0003
titulo: Consumo At-least-once e Idempotente
status: Aceita
data: 2026-07-10
responsavel: Arquitetura de Soluções
decisao_relacionada: Processamento assíncrono recuperável
---

# ADR-0003 — Consumo At-least-once e Idempotente

## 1. Contexto

A solução separa Lançamentos e Consolidado em fronteiras distintas.

Lançamentos registra a movimentação financeira e publica informações para que o Consolidado atualize a visão diária.

Como a comunicação entre essas fronteiras é assíncrona e recuperável, o fluxo precisa lidar com falhas temporárias, retries e reprocessamento.

Em canais assíncronos, uma mesma mensagem pode ser entregue mais de uma vez.

Esse comportamento não deve gerar duplicidade de efeito no Consolidado.

---

## 2. Decisão

A solução adotará consumo at-least-once com processamento idempotente no Consolidado.

Isso significa que a arquitetura aceita a possibilidade de uma mensagem ser entregue mais de uma vez, desde que o consumidor consiga identificar eventos já processados e impedir a reaplicação do mesmo lançamento.

O Consolidado deve registrar o processamento dos eventos recebidos e usar esse controle para evitar duplicidade de efeito.
No `DailyBalance`, a aplicação do efeito financeiro deve ocorrer por operação atômica no banco, preservando idempotência e evitando atualização perdida sob concorrência.

Quando o consumidor precisar republicar a mensagem para retry ou DLQ, o ack da mensagem original deve ocorrer somente depois da confirmação do broker e do roteamento da cópia. Falha nessa republicação mantém a original reprocessável.

No escopo inicial, a consolidação não depende de ordenação global dos eventos.

O saldo diário é calculado a partir de lançamentos imutáveis aplicados de forma idempotente.

---

## 3. O que a decisão inclui

Esta decisão inclui:

```text
- aceitar entrega at-least-once no fluxo assíncrono
- registrar eventos processados pelo Consolidado
- impedir que o mesmo evento produza efeito mais de uma vez
- permitir retry e reprocessamento seguro
- manter rastreabilidade entre evento, lançamento e atualização do Consolidado
- monitorar falhas de consumo e mensagens isoladas para análise
```

---

## 4. O que fica fora desta decisão

Esta decisão não define:

```text
- tecnologia final do broker ou fila
- formato definitivo do evento
- política final de retenção dos eventos processados
- política final de DLQ ou isolamento de mensagens
- quantidade final de consumidores
- estratégia final de particionamento ou paralelismo
```

Esses pontos serão detalhados nos blocos de solução e em decisões posteriores.

---

## 5. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar como base da solução |
|---|---|---|
| Consumo sem controle de idempotência | O consumidor aplicaria toda mensagem recebida sem verificar se já foi processada. | Pode duplicar o saldo consolidado quando houver retry, redelivery ou reprocessamento. |
| Entrega exactly-once como premissa arquitetural | A arquitetura dependeria de uma garantia estrita de entrega única fim a fim. | Aumenta complexidade e acoplamento tecnológico. Não elimina a necessidade de idempotência em cenários reais de falha. |
| Consumo síncrono no registro do lançamento | O Consolidado seria atualizado dentro do fluxo de registro. | Reintroduz dependência síncrona entre Lançamentos e Consolidado e contraria o isolamento desejado. |
| Consumo at-least-once com idempotência | A mensagem pode ser entregue mais de uma vez, mas o consumidor impede duplicidade de efeito. | Alternativa adotada. Equilibra confiabilidade, recuperação e simplicidade operacional. |

---

## 6. Consequências

Consequências positivas:

```text
- permite retry e reprocessamento seguro
- evita duplicidade de efeito no Consolidado
- evita lost update na projeção quando eventos distintos chegam em paralelo para o mesmo comerciante e data
- reduz dependência de garantias fortes do broker
- melhora resiliência do fluxo assíncrono
- mantém rastreabilidade dos eventos processados
- favorece recuperação operacional após falhas
```

Consequências e tradeoffs:

```text
- exige armazenamento de eventos processados
- exige chave única de evento
- exige controle transacional entre marcação de evento processado e atualização da projeção
- exige política de retenção para registros de processamento
- exige observabilidade sobre falhas, retries e mensagens isoladas
```

---

## 7. Relação com requisitos e ABBs

Esta decisão sustenta principalmente:

```text
- ASR-005: Consolidado pode ficar temporariamente defasado, desde que observável e recuperável
- ASR-007: Eventos ou mensagens duplicadas não devem duplicar efeitos no Consolidado
- ASR-010: O fluxo de registro, publicação, consumo e consolidação deve ser observável
- ASR-011: Falhas de publicação, consumo ou consolidação devem ser recuperáveis
- ABB-007: Canal Assíncrono Confiável
- ABB-009: Consumo Idempotente
- ABB-010: Projeção Materializada do Consolidado
- ABB-013: Observabilidade do Fluxo
- ABB-014: Recuperação Operacional
```

---

## 8. Relação com documentos

Esta decisão sustenta:

```text
- docs/architecture/02-requisitos-arquiteturais.md
- docs/architecture/03-blocos-de-arquitetura.md
```

---

## 9. Status

Decisão aceita para o escopo inicial da solução.
