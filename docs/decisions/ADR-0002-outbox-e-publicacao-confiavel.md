---
adr_id: ADR-0002
titulo: Outbox e Publicação Confiável
status: Aceita
data: 2026-07-10
responsavel: Arquitetura de Soluções
decisao_relacionada: Propagação confiável de eventos
---

# ADR-0002 — Outbox e Publicação Confiável

## 1. Contexto

O serviço de Lançamentos é responsável por registrar créditos e débitos de forma confiável.

O Consolidado depende das informações geradas a partir desses lançamentos para atualizar a visão diária por comerciante e data.

Como Lançamentos e Consolidado foram separados em fronteiras arquiteturais distintas, a atualização do Consolidado não deve ocorrer como dependência síncrona obrigatória do registro financeiro.

Existe um risco relevante no intervalo entre registrar o lançamento e publicar a informação necessária para o Consolidado.

Exemplo do risco:

```text
1. o lançamento é gravado com sucesso
2. a aplicação falha antes de publicar o evento
3. o Consolidado não recebe a atualização
4. a visão consolidada fica incompleta sem um mecanismo de recuperação
```

Esse risco precisa ser tratado no desenho arquitetural.

---

## 2. Decisão

A solução adotará o padrão Outbox para registrar a intenção de publicação junto da transação do lançamento.

Quando um lançamento for registrado, a fronteira de Lançamentos deve persistir na mesma transação:

```text
- o lançamento financeiro
- o registro de idempotência de entrada, quando aplicável
- o evento pendente de publicação para atualização do Consolidado
```

A publicação para o canal assíncrono será realizada posteriormente por um publicador recuperável.

Essa decisão evita que a disponibilidade imediata do canal assíncrono seja requisito para registrar lançamentos.

---

## 3. O que a decisão inclui

Esta decisão inclui:

```text
- persistir a intenção de publicação junto do lançamento
- publicar eventos pendentes de forma assíncrona
- permitir retry de publicação
- manter status de publicação
- permitir retomada após falha da aplicação, banco ou canal de comunicação
- permitir observabilidade sobre eventos pendentes, publicados e com falha
```

---

## 4. O que fica fora desta decisão

Esta decisão não define:

```text
- tecnologia final do banco de dados
- tecnologia final do broker ou fila
- formato definitivo do payload do evento
- política final de retenção da Outbox
- quantidade final de workers publicadores
- ferramenta final de observabilidade
```

Esses pontos serão detalhados em decisões posteriores e nos blocos de solução.

---

## 5. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar como base da solução |
|---|---|---|
| Publicar evento diretamente durante a requisição | A API de Lançamentos gravaria o lançamento e publicaria o evento no mesmo fluxo síncrono. | Torna o registro mais sensível a falhas ou lentidão do canal assíncrono. Também deixa uma janela de falha entre gravação e publicação. |
| Publicar evento somente após o commit, sem Outbox | A aplicação gravaria o lançamento e depois tentaria publicar o evento. | Se a aplicação falhar após o commit e antes da publicação, o Consolidado pode não receber a atualização. |
| Transação distribuída entre banco e broker | Banco e broker participariam de uma transação coordenada. | Aumenta complexidade, acoplamento operacional e dependência de suporte transacional entre componentes. |
| Recalcular o Consolidado periodicamente sem eventos | O Consolidado seria atualizado por varredura periódica dos lançamentos. | Pode ser útil como mecanismo complementar de reconstrução, mas não oferece atualização incremental clara e observável para o fluxo principal. |
| Outbox com publicação recuperável | A intenção de publicação é persistida junto do lançamento e publicada depois por processo recuperável. | Alternativa adotada. Reduz risco de perda silenciosa entre registro financeiro e atualização do Consolidado. |

---

## 6. Consequências

Consequências positivas:

```text
- preserva o registro financeiro mesmo se a publicação falhar temporariamente
- reduz risco de perda silenciosa de eventos
- desacopla o registro de lançamentos da disponibilidade do canal assíncrono
- permite retry e retomada da publicação
- melhora rastreabilidade entre lançamento e atualização do Consolidado
- cria base para observabilidade do fluxo de publicação
```

Consequências e tradeoffs:

```text
- adiciona uma estrutura de Outbox à fronteira de Lançamentos
- exige um publicador dedicado ou processo equivalente
- exige controle de status, tentativas e falhas de publicação
- exige monitoramento de eventos pendentes e atrasados
- introduz consistência eventual entre registro e consulta consolidada
```

---

## 7. Relação com requisitos e ABBs

Esta decisão sustenta principalmente:

```text
- RNF-001: Lançamentos não deve ficar indisponível caso Consolidado falhe
- ASR-004: Lançamentos registrados devem permanecer confiáveis mesmo quando a consolidação falhar temporariamente
- ASR-005: Consolidado pode ficar temporariamente defasado, desde que a defasagem seja observável e recuperável
- ASR-010: O fluxo de registro, publicação, consumo e consolidação deve ser observável
- ASR-011: Falhas de publicação, consumo ou consolidação devem ser recuperáveis
- ABB-003: Persistência Transacional de Lançamentos
- ABB-005: Outbox Durável
- ABB-006: Publicação Recuperável
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
