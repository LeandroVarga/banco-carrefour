---
adr_id: ADR-0007
titulo: Canal Assíncrono, Broker e RabbitMQ Local
status: Aceita
data: 2026-07-10
responsavel: Arquitetura de Soluções
decisao_relacionada: Comunicação assíncrona entre Lançamentos e Consolidado
---

# ADR-0007 — Canal Assíncrono, Broker e RabbitMQ Local

## 1. Contexto

A solução separa Lançamentos e Consolidado em fronteiras arquiteturais distintas.

Lançamentos registra créditos e débitos e precisa continuar disponível mesmo se o Consolidado falhar.

O Consolidado é uma visão derivada, atualizada a partir das informações geradas pelos lançamentos.

Essa separação exige um mecanismo de comunicação entre fronteiras que não torne o registro financeiro dependente da disponibilidade imediata do Consolidado.

A solução também adota Outbox para registrar a intenção de publicação junto da transação do lançamento.

Após isso, os eventos pendentes precisam ser publicados em um canal que permita entrega posterior, retry, redelivery e isolamento de falhas.

---

## 2. Decisão

A comunicação entre Lançamentos e Consolidado será assíncrona e mediada por um broker ou fila.

No ambiente local do desafio, RabbitMQ será usado como broker de referência.

Na AWS de referência do case, o mesmo papel é materializado por Amazon SQS Standard com DLQ para `EntryCreated.v1`.

RabbitMQ não é tratado como produção neste case. A substituição por padrão corporativo equivalente continua possível, desde que preserve o papel arquitetural de canal assíncrono confiável.

O canal assíncrono será usado para transportar eventos de lançamentos publicados pela Outbox até o consumidor do Consolidado.

O consumo seguirá o modelo at-least-once com processamento idempotente, conforme definido no ADR-0003.

---

## 3. O que a decisão inclui

Esta decisão inclui:

```text
- comunicação assíncrona entre Lançamentos e Consolidado
- uso de broker ou fila como canal de integração
- papel arquitetural de canal assíncrono confiável
- RabbitMQ como materialização local
- Amazon SQS Standard com DLQ como materialização AWS de referência
- desacoplamento temporal entre produtor e consumidor
- entrega recuperável para o Consolidado
- suporte a retry, redelivery e isolamento de mensagens com falha persistente
- possibilidade de substituição por padrão corporativo equivalente sem alterar o papel arquitetural
```

---

## 4. O que fica fora desta decisão

Esta decisão não define:

```text
- topologia física definitiva de exchanges, filas e bindings
- política final de DLQ
- política final de retenção de mensagens
- configuração final de alta disponibilidade do broker
- estratégia final de particionamento, ordenação ou paralelismo
- parâmetros finais de retry, backoff e timeout
- configuração final de segurança do broker
```

Esses pontos serão detalhados nos blocos de solução, na arquitetura operacional e em decisões futuras quando necessário.

---

## 5. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar como base da solução |
|---|---|---|
| Chamada síncrona entre Lançamentos e Consolidado | Lançamentos chamaria Consolidado diretamente durante o registro. | Reintroduz dependência síncrona e pode indisponibilizar o registro financeiro se o Consolidado falhar. |
| Consolidado lendo diretamente a base de Lançamentos | Consolidado consultaria ou varreria a persistência de Lançamentos para atualizar a visão. | Aumenta acoplamento por banco, reduz isolamento entre fronteiras e dificulta evolução independente. |
| Fila em memória | Eventos seriam mantidos apenas em memória da aplicação. | Não oferece durabilidade suficiente para o fluxo financeiro e perde mensagens em reinícios ou falhas. |
| Streaming distribuído como base inicial | Uso de plataforma de streaming como solução principal desde o início. | Pode ser adequado em cenários de alto volume, múltiplos consumidores e retenção longa de eventos, mas aumenta complexidade operacional para o escopo do desafio. |
| Broker ou fila com RabbitMQ local | Eventos são transportados por canal assíncrono, com RabbitMQ como referência local. | Alternativa adotada. Atende ao desacoplamento, execução local, recuperação e simplicidade adequada ao escopo inicial. |
| Amazon SQS Standard com DLQ | Uso de fila gerenciada na AWS de referência do case. | Alternativa adotada para implantação AWS de referência, preservando retry, isolamento e redrive por configuração operacional. |

---

## 6. Consequências

Consequências positivas:

```text
- desacopla Lançamentos e Consolidado
- protege o registro financeiro de falhas no consumidor
- permite entrega posterior ao Consolidado
- permite retry e redelivery
- suporta recuperação operacional do fluxo
- permite execução local reproduzível com RabbitMQ
- mapeia o papel de mensageria para SQS Standard com DLQ na AWS de referência
```

Consequências e tradeoffs:

```text
- introduz consistência eventual
- exige consumidor idempotente
- exige monitoramento de filas, backlog, erros e mensagens isoladas
- exige configuração e operação do broker no ambiente local
- exige tratar diferenças operacionais entre RabbitMQ local e SQS, como visibility timeout, redrive policy e métricas de fila
- pode exigir políticas específicas de retry, DLQ e retenção conforme ambiente
```

---

## 7. Relação com requisitos, ABBs e SBBs

Esta decisão sustenta principalmente:

```text
- RNF-001: Lançamentos não deve ficar indisponível caso Consolidado falhe
- ASR-001: Lançamentos deve continuar disponível mesmo se Consolidado falhar
- ASR-005: Consolidado pode ficar temporariamente defasado, desde que observável e recuperável
- ASR-007: Eventos ou mensagens duplicadas não devem duplicar efeitos no Consolidado
- ASR-011: Falhas de publicação, consumo ou consolidação devem ser recuperáveis
- ABB-006: Publicação Recuperável
- ABB-007: Canal Assíncrono Confiável
- ABB-009: Consumo Idempotente
- ABB-014: Recuperação Operacional
- SBB-006: Ledger.OutboxPublisher
- SBB-007: Message Broker
- SBB-008: Consolidation.Worker
- SBB-017: Operational Recovery
```

---

## 8. Relação com documentos

Esta decisão sustenta:

- [03-blocos-de-arquitetura.md](../architecture/03-blocos-de-arquitetura.md)
- [04-blocos-de-solucao.md](../architecture/04-blocos-de-solucao.md)
- [05-arquitetura-da-solucao.md](../architecture/05-arquitetura-da-solucao.md)
- [arquitetura-operacional.md](../operations/arquitetura-operacional.md)

---

## 9. Status

Decisão aceita para o escopo inicial da solução.
