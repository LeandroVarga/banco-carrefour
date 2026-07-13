---
doc_id: ARCH-006
titulo: Diagramas
versao: 1.0
status: Atualizado
responsavel: Arquitetura de Soluções
ultima_atualizacao: 2026-07-11
etapa_relacionada: Definition and Decision
---

# Diagramas

## 1. Objetivo

Este documento apresenta os diagramas arquiteturais da solução para controle de lançamentos e consulta do consolidado diário.

Os diagramas refletem a arquitetura descrita em `05-arquitetura-da-solucao.md` e as decisões registradas em `docs/decisions/`.

A documentação utiliza uma representação compatível com a leitura do C4 Model, cobrindo contexto, containers, componentes principais e fluxos arquiteturais.

---

## 2. Notas de leitura

Os diagramas usam Mermaid para facilitar visualização em ferramentas compatíveis com Markdown.

Os níveis seguem esta intenção:

```text
- Contexto: mostra o sistema e seus atores externos.
- Container: mostra APIs, workers, bancos e broker.
- Componentes: mostra responsabilidades internas relevantes.
- Fluxos: mostra sequências de comportamento da solução.
```

---

## 3. C4 Context — Visão de contexto

```mermaid
flowchart LR
    merchant["Comerciante"]
    system["Sistema de Controle de Fluxo de Caixa Diário"]
    idp["Provedor de Identidade"]
    obs["Plataforma de Observabilidade"]

    merchant -->|"registra créditos e débitos"| system
    merchant -->|"consulta consolidado diário"| system
    system -->|"autentica e autoriza chamadas"| idp
    system -->|"envia logs, métricas e traces"| obs
```

Neste nível, o sistema permite ao comerciante registrar lançamentos financeiros e consultar o consolidado diário.

O provedor de identidade e a plataforma de observabilidade representam capacidades externas ou corporativas que podem variar conforme o ambiente.

---

## 4. C4 Container — Visão de containers

```mermaid
flowchart LR
    merchant["Comerciante"]

    subgraph ledger["Fronteira de Lançamentos"]
        ledgerApi["Ledger.Api"]
        ledgerDb[("Ledger Database")]
        outboxPublisher["Ledger.OutboxPublisher"]
    end

    broker["Message Broker"]

    subgraph consolidation["Fronteira de Consolidado"]
        consolidationWorker["Consolidation.Worker"]
        consolidationDb[("Consolidation Database")]
        consolidationApi["Consolidation.Api"]
    end

    merchant -->|"POST /entries"| ledgerApi
    ledgerApi -->|"grava lançamento, idempotência e Outbox"| ledgerDb
    outboxPublisher -->|"lê eventos pendentes"| ledgerDb
    outboxPublisher -->|"publica eventos"| broker
    broker -->|"entrega eventos"| consolidationWorker
    consolidationWorker -->|"atualiza DailyBalance e Processed Events"| consolidationDb
    merchant -->|"GET /daily-balances/{businessDate}"| consolidationApi
    consolidationApi -->|"consulta DailyBalance"| consolidationDb
```

Esta visão mostra as quatro unidades implantáveis principais, as duas persistências independentes e o broker assíncrono.

---

## 5. Componentes — Lançamentos

```mermaid
flowchart TB
    client["Cliente"]

    subgraph ledgerApi["Ledger.Api"]
        auth["Autenticação e autorização"]
        validation["Validação de entrada"]
        idempotency["Idempotência de entrada"]
        entryService["Serviço de registro de lançamento"]
        outboxWriter["Gravação de evento na Outbox"]
    end

    ledgerDb[("Ledger Database")]

    client --> auth
    auth --> validation
    validation --> idempotency
    idempotency --> entryService
    entryService --> outboxWriter
    outboxWriter --> ledgerDb
```

A fronteira de Lançamentos concentra a escrita financeira e garante que o lançamento e a intenção de publicação sejam persistidos de forma consistente.

---

## 6. Componentes — Consolidado

```mermaid
flowchart TB
    broker["Message Broker"]

    subgraph worker["Consolidation.Worker"]
    eventConsumer["Consumo de eventos"]
    eventValidation["Validação do evento"]
        processedEvent["Registro idempotente de ProcessedEvent"]
        projectionUpdater["Upsert atômico de DailyBalance"]
    end

    subgraph api["Consolidation.Api"]
        auth["Autenticação e autorização"]
        queryHandler["Consulta de consolidado diário"]
    end

    consolidationDb[("Consolidation Database")]

    broker --> eventConsumer
    eventConsumer --> eventValidation
    eventValidation --> processedEvent
    processedEvent --> projectionUpdater
    projectionUpdater --> consolidationDb
    auth --> queryHandler
    queryHandler --> consolidationDb
```

A fronteira de Consolidado separa processamento assíncrono e consulta de leitura, mantendo a projeção materializada como visão derivada.

---

## 7. Fluxo — Registro de lançamento

```mermaid
sequenceDiagram
    participant C as Cliente
    participant API as Ledger.Api
    participant DB as Ledger Database

    C->>API: POST /entries
    API->>API: autenticar e autorizar
    API->>API: validar payload
    API->>DB: verificar idempotência
    API->>DB: gravar lançamento
    API->>DB: gravar evento pendente na Outbox
    DB-->>API: transação confirmada
    API-->>C: lançamento registrado
```

Esse fluxo mantém o registro financeiro dentro da fronteira de Lançamentos e não depende do Consolidado.

---

## 8. Fluxo — Publicação via Outbox

```mermaid
sequenceDiagram
    participant P as Ledger.OutboxPublisher
    participant DB as Ledger Database
    participant B as Message Broker

    P->>DB: buscar eventos pendentes
    DB-->>P: eventos pendentes
    P->>B: publicar evento
    B-->>P: publicação confirmada
    P->>DB: marcar evento como publicado
```

Esse fluxo torna a publicação recuperável e evita perda silenciosa entre persistência e envio ao broker.

---

## 9. Fluxo — Consolidação

```mermaid
sequenceDiagram
    participant B as Message Broker
    participant W as Consolidation.Worker
    participant DB as Consolidation Database

    B->>W: entregar evento de lançamento
    W->>W: validar evento
    W->>DB: iniciar transação local
    W->>DB: registrar ProcessedEvent por eventId
    alt eventId duplicado
        DB-->>W: duplicidade detectada
        W-->>B: confirmar sem novo efeito financeiro
    else evento novo
        W->>DB: upsert atômico de DailyBalance
        DB-->>W: transação confirmada
        W-->>B: confirmar processamento
    end
```

Esse fluxo materializa consumo at-least-once com processamento idempotente. `ProcessedEvent` e `DailyBalance` são tratados na mesma transação local; duplicidade concorrente de `eventId` não reaplica saldo.

---

## 10. Fluxo — Consulta do consolidado

```mermaid
sequenceDiagram
    participant C as Cliente
    participant API as Consolidation.Api
    participant DB as Consolidation Database

    C->>API: GET /daily-balances/{businessDate}
    API->>API: autenticar e autorizar
    API->>API: obter comerciante do contexto autorizado
    API->>DB: consultar DailyBalance por comerciante e data
    DB-->>API: totais, saldo e metadados
    API-->>C: consolidado diário
```

Esse fluxo atende a consulta do relatório diário sem recalcular o saldo a partir de todos os lançamentos em cada requisição.

---

## 11. Visão operacional local

```mermaid
flowchart TB
    subgraph docker["Docker Compose"]
        ledgerApi["ledger-api container"]
        outboxPublisher["ledger-outbox-publisher container"]
        consolidationWorker["consolidation-worker container"]
        consolidationApi["consolidation-api container"]
        ledgerDb[("ledger-postgres container")]
        consolidationDb[("consolidation-postgres container")]
        rabbitmq["rabbitmq container"]
    end

    ledgerApi --> ledgerDb
    outboxPublisher --> ledgerDb
    outboxPublisher --> rabbitmq
    rabbitmq --> consolidationWorker
    consolidationWorker --> consolidationDb
    consolidationApi --> consolidationDb
```

Esta visão representa a execução local do desafio.

Docker Compose não representa a topologia definitiva de produção. Ele materializa uma forma reproduzível para avaliação, testes e validação dos fluxos principais.

---

## 12. Relação com ADRs

| Diagrama | ADRs relacionados |
|---|---|
| C4 Context | ADR-0010 |
| C4 Container | ADR-0001, ADR-0005, ADR-0007, ADR-0008, ADR-0010 |
| Componentes de Lançamentos | ADR-0002, ADR-0005, ADR-0006, ADR-0008, ADR-0009 |
| Componentes de Consolidado | ADR-0003, ADR-0004, ADR-0005, ADR-0006, ADR-0008, ADR-0009 |
| Registro de lançamento | ADR-0001, ADR-0002, ADR-0005, ADR-0006 |
| Publicação via Outbox | ADR-0002, ADR-0007 |
| Consolidação | ADR-0003, ADR-0004, ADR-0007 |
| Consulta do consolidado | ADR-0000, ADR-0004 |
| Visão operacional local | ADR-0008, ADR-0009, ADR-0010 |

---

## 13. Relação com documentos

Este documento complementa:

```text
- 03-blocos-de-arquitetura.md
- 04-blocos-de-solucao.md
- 05-arquitetura-da-solucao.md
- docs/decisions/
```

Os aspectos de segurança e operação serão aprofundados em:

```text
- docs/security/arquitetura-de-seguranca.md
- docs/operations/arquitetura-operacional.md
- docs/operations/observabilidade-sli-slo-e-recuperacao.md
```

---

## 14. Status

Documento atualizado como baseline de diagramas para a implementação local e visão operacional documentada.
