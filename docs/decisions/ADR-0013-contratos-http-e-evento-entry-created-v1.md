---
adr_id: ADR-0013
titulo: Contratos HTTP e Evento EntryCreated.v1
status: Aceita
data: 2026-07-11
decisao_relacionada: Contratos mínimos para implementação da solução
---

# ADR-0013 — Contratos HTTP e Evento EntryCreated.v1

## 1. Contexto

A arquitetura separa Lançamentos e Consolidado em fronteiras distintas.

Lançamentos recebe comandos de registro financeiro e publica eventos para atualização do Consolidado.

Consolidado expõe consulta de DailyBalance por comerciante e data de negócio.

Antes da implementação, a solução precisa fechar contratos mínimos para evitar decisões implícitas no código.

Sem contratos explícitos, cada componente poderia interpretar de forma diferente:

```text
- payload de registro de lançamento sem merchantId no corpo da requisição
- chave de idempotência
- semântica de businessDate
- respostas HTTP esperadas
- contrato do evento assíncrono
- campos necessários para idempotência de consumo
- dados mínimos da projeção DailyBalance
```

---

## 2. Decisão

A solução adotará contratos versionados para a implementação inicial.

Contratos HTTP:

```text
contracts/openapi.yaml
```

Endpoints iniciais:

```text
POST /entries
GET /daily-balances/{businessDate}
```

Contrato assíncrono:

```text
contracts/events/entry-created-v1.schema.json
```

Evento inicial:

```text
EntryCreated.v1
```

Os contratos fazem parte da arquitetura da solução e devem orientar implementação, testes e documentação operacional.

Os contratos são independentes da infraestrutura usada para transportar ou hospedar a solução. Eles não dependem de RabbitMQ, SQS, ECS, RDS ou de outro serviço de plataforma.

---

## 3. O que a decisão inclui

Esta decisão inclui:

```text
- OpenAPI como contrato HTTP inicial
- JSON Schema como contrato inicial do evento assíncrono
- POST /entries como comando de registro de lançamento
- GET /daily-balances/{businessDate} como consulta do Consolidado
- Idempotency-Key obrigatória no registro de lançamento
- autenticação via Bearer JWT no contrato HTTP
- businessDate como data de negócio no formato YYYY-MM-DD
- EntryCreated.v1 como evento publicado após registro de lançamento
- eventId como chave de idempotência de consumo
- lastUpdatedAt no retorno do Consolidado
- independência dos contratos em relação a RabbitMQ, SQS, ECS e RDS
```

---

## 4. O que fica fora desta decisão

Esta decisão não define:

```text
- implementação dos endpoints
- framework final de validação de contrato
- geração automática de código a partir do OpenAPI
- ferramenta final para documentação interativa da API
- AsyncAPI completo
- contrato de eventos futuros
- endpoints administrativos de rebuild
- endpoint público de consulta de lançamentos
```

Esses pontos podem ser tratados em incrementos posteriores.

---

## 5. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar como base da solução |
|---|---|---|
| Implementar primeiro e documentar contratos depois | O código definiria implicitamente payloads, erros e eventos. | Aumenta risco de inconsistência, dificulta testes e enfraquece rastreabilidade arquitetural. |
| Documentar contratos apenas em Markdown | Os contratos seriam descritos em texto livre. | Ajuda na leitura, mas não favorece validação, automação e uso por ferramentas. |
| OpenAPI para HTTP e JSON Schema para evento | Contratos ficam estruturados, versionáveis e verificáveis. | Alternativa adotada. Equilibra simplicidade, clareza e prontidão para implementação. |
| AsyncAPI completo desde o início | Todo o fluxo assíncrono seria descrito com especificação própria. | Pode ser útil futuramente, mas adiciona complexidade documental para o baseline local. |

---

## 6. Consequências positivas

```text
- reduz decisões implícitas durante a implementação
- melhora rastreabilidade entre arquitetura, contrato, código e testes
- define payloads mínimos antes do desenvolvimento
- explicita códigos de resposta esperados
- reforça idempotência de entrada e consumo
- facilita testes de contrato e integração
- melhora a comunicação com avaliadores técnicos
```

---

## 7. Consequências negativas e trade-offs

```text
- exige manutenção dos contratos junto com o código
- pode exigir ajustes quando a implementação revelar detalhes adicionais
- adiciona uma etapa antes da implementação funcional
- OpenAPI e JSON Schema não substituem testes automatizados
- JSON Schema isolado não descreve toda a topologia assíncrona como AsyncAPI faria
```

---

## 8. Relação com requisitos, ASRs, ABBs e SBBs

Esta decisão sustenta principalmente:

```text
- RF-001: registrar lançamento de crédito
- RF-002: registrar lançamento de débito
- RF-005: consultar consolidado diário
- RNF-001: Lançamentos não deve ficar indisponível caso Consolidado falhe
- RNF-002: Consolidado deve suportar 50 RPS no pico
- ASR-006: Tentativas repetidas de registro não devem criar lançamentos duplicados indevidamente
- ASR-007: Eventos duplicados não devem duplicar efeitos no Consolidado
- ASR-009: Acesso deve ser autenticado e autorizado por comerciante
- ASR-010: O fluxo deve ser observável
- ABB-004: Idempotência de Entrada
- ABB-005: Outbox Durável
- ABB-007: Canal Assíncrono Confiável
- ABB-009: Consumo Idempotente
- ABB-010: Projeção Materializada do Consolidado
- ABB-015: Segurança de Acesso
- SBB-013: API Contracts
- SBB-014: Authentication and Authorization
- SBB-019: Configuração por Ambiente
```

---

## 9. Relação com documentos

Esta decisão complementa:

```text
- contracts/README.md
- contracts/openapi.yaml
- contracts/events/entry-created-v1.schema.json
- docs/architecture/08-implementation-readiness.md
- docs/architecture/05-arquitetura-da-solucao.md
- docs/architecture/07-rastreabilidade.md
- docs/security/arquitetura-de-seguranca.md
```

---

## 10. Status

Decisão aceita para o escopo inicial de implementação.

Os contratos permanecem sujeitos a ajustes durante implementação e testes, mas mudanças relevantes devem preservar versionamento e rastreabilidade.
