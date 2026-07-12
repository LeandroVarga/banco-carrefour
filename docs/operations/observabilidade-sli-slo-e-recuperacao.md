---
doc_id: OPS-002
titulo: Observabilidade, SLIs, SLOs e Recuperação
versao: 1.0
status: Rascunho
responsavel: Arquitetura de Soluções
ultima_atualizacao: 2026-07-11
etapa_relacionada: Production Validation and Evolution
---

# Observabilidade, SLIs, SLOs e Recuperação

## 1. Objetivo

Este documento define a estratégia de observabilidade, SLIs, SLOs, alertas e recuperação da solução de controle de lançamentos e consulta do consolidado diário.

A observabilidade deve permitir acompanhar o fluxo fim a fim:

```text
registro do lançamento
-> persistência transacional
-> Outbox
-> publicação no broker
-> consumo pelo Consolidado
-> atualização da projeção DailyBalance
-> consulta do consolidado diário
```

Este documento complementa `arquitetura-operacional.md`.

---

## 2. Objetivos operacionais

A observabilidade deve responder às seguintes perguntas:

```text
- Lançamentos está disponível?
- Consolidado está disponível?
- o Consolidado está suportando 50 RPS?
- a taxa de falhas do Consolidado está dentro do limite de 5% no pico?
- lançamentos estão sendo persistidos com sucesso?
- eventos estão ficando presos na Outbox?
- o broker está acumulando backlog?
- o worker está consumindo eventos?
- há eventos duplicados descartados?
- há mensagens isoladas para investigação?
- existe atraso relevante entre lançamento e consolidação?
- DailyBalance pode ser reconstruído quando necessário?
```

---

## 3. Princípios de observabilidade

A solução deve seguir os seguintes princípios:

```text
- logs estruturados
- métricas por unidade implantável
- traces ou correlação entre requisição, evento e consolidação
- correlation_id propagado no fluxo
- métricas de negócio e métricas técnicas separadas
- alertas baseados em impacto operacional
- dashboards por fronteira e por fluxo
- visibilidade de backlog, lag, retry e falhas
- proteção contra exposição de dados sensíveis em logs
- distinção entre execução local e produção
```

---

## 4. Identificadores de correlação

O fluxo deve permitir correlação entre requisição, lançamento, evento e consolidação.

Identificadores recomendados:

```text
- correlation_id
- request_id
- merchant_id
- entry_id
- idempotency_key
- event_id
- outbox_id
- processed_event_id
```

Diretrizes:

```text
- correlation_id deve acompanhar o fluxo sempre que possível
- event_id deve identificar unicamente o evento publicado
- entry_id deve relacionar o evento ao lançamento de origem
- merchant_id deve ser usado com cuidado em logs e dashboards
- tokens, secrets e payloads sensíveis não devem ser registrados integralmente
```

---

## 5. Logs estruturados

Logs devem ser estruturados e permitir diagnóstico sem depender de leitura manual de texto livre.

Campos recomendados:

```text
- timestamp
- level
- service
- operation
- correlation_id
- request_id
- merchant_id quando necessário
- entry_id quando aplicável
- event_id quando aplicável
- status
- error_code
- duration_ms
- retry_attempt
- message
```

Eventos mínimos de log:

| Ponto do fluxo | Evento de log esperado |
|---|---|
| Ledger.Api | requisição recebida, validação rejeitada, lançamento registrado, repetição idempotente. |
| Ledger.OutboxPublisher | evento pendente encontrado, publicação realizada, falha de publicação, retry agendado. |
| Message Broker | publicação, entrega, erro de entrega quando disponível pela plataforma. |
| Consolidation.Worker | evento recebido, evento duplicado descartado, DailyBalance atualizado, falha de consumo. |
| Consolidation.Api | consulta recebida, consulta autorizada, resposta emitida, erro de consulta. |

---

## 6. Métricas principais

As métricas devem cobrir APIs, workers, bancos, broker e fluxo de negócio.

### 6.1 Métricas de Ledger.Api

```text
- ledger_api_requests_total
- ledger_api_request_duration_ms
- ledger_api_errors_total
- ledger_entries_created_total
- ledger_idempotency_replays_total
- ledger_idempotency_conflicts_total
- ledger_validation_errors_total
```

### 6.2 Métricas de Outbox

```text
- outbox_pending_events_total
- outbox_oldest_pending_event_age_seconds
- outbox_published_events_total
- outbox_publish_errors_total
- outbox_retry_attempts_total
- outbox_dead_or_blocked_events_total
```

### 6.3 Métricas do broker

```text
- broker_messages_ready_total
- broker_messages_unacked_total
- broker_publish_rate
- broker_consume_rate
- broker_redeliveries_total
- broker_dlq_messages_total
```

### 6.4 Métricas de Consolidation.Worker

```text
- consolidation_worker_events_consumed_total
- consolidation_worker_events_processed_total
- consolidation_worker_duplicate_events_discarded_total
- consolidation_worker_processing_errors_total
- consolidation_worker_processing_duration_ms
- consolidation_worker_retries_total
- consolidation_lag_seconds
```

### 6.5 Métricas de Consolidation.Api

```text
- consolidation_api_requests_total
- consolidation_api_request_duration_ms
- consolidation_api_errors_total
- consolidation_api_5xx_total
- consolidation_api_4xx_total
- consolidation_api_rps
- daily_balance_queries_total
```

### 6.6 Métricas de banco de dados

```text
- database_connection_pool_usage
- database_query_duration_ms
- database_errors_total
- database_locks_total
- database_deadlocks_total
- database_storage_used_bytes
```

---

## 7. SLIs

SLI significa Service Level Indicator.

SLIs são indicadores usados para medir o comportamento real da solução.

| ID | SLI | Descrição | Fonte de medição |
|---|---|---|---|
| SLI-001 | Taxa de sucesso do registro de lançamentos | Percentual de requisições válidas de `POST /entries` concluídas com sucesso. | Ledger.Api |
| SLI-002 | Latência do registro de lançamentos | Tempo de resposta do `POST /entries`. | Ledger.Api |
| SLI-003 | Taxa de sucesso da consulta do Consolidado | Percentual de requisições elegíveis de `GET /daily-balances/{businessDate}` concluídas sem erro. | Consolidation.Api |
| SLI-004 | Latência da consulta do Consolidado | Tempo de resposta do `GET /daily-balances/{businessDate}`. | Consolidation.Api |
| SLI-005 | Taxa de falhas do Consolidado no pico | Percentual de falhas ou perdas nas consultas do Consolidado durante pico de 50 RPS. | Consolidation.Api e teste de carga |
| SLI-006 | Idade do evento mais antigo na Outbox | Tempo desde a criação do evento pendente mais antigo. | Ledger Database / Outbox |
| SLI-007 | Lag de consolidação | Tempo entre registro do lançamento e atualização do DailyBalance. | Ledger.Api, evento e Consolidation.Worker |
| SLI-008 | Taxa de eventos processados com sucesso | Percentual de eventos consumidos que atualizam ou confirmam processamento corretamente. | Consolidation.Worker |
| SLI-009 | Mensagens isoladas | Quantidade de mensagens em DLQ, tabela de rejeição ou mecanismo equivalente. | Broker / Worker |
| SLI-010 | Eventos duplicados descartados | Quantidade de eventos repetidos descartados sem novo efeito financeiro. | Consolidation.Worker / Processed Events |

---

## 8. SLOs propostos

SLO significa Service Level Objective.

Os SLOs abaixo são metas propostas para o escopo do desafio e devem ser validados por testes automatizados, teste de carga e observação operacional.

| ID | SLO proposto | Justificativa |
|---|---|---|
| SLO-001 | `POST /entries` deve manter alta taxa de sucesso para requisições válidas enquanto Ledger Database estiver disponível. | Protege o caminho crítico de registro financeiro. |
| SLO-002 | `POST /entries` não deve depender da disponibilidade do Consolidado. | Atende diretamente ao RNF-001. |
| SLO-003 | `GET /daily-balances/{businessDate}` deve suportar 50 RPS no pico. | Atende diretamente ao RNF-002. |
| SLO-004 | No pico de 50 RPS, falhas ou perdas de requisições elegíveis do Consolidado devem ficar em no máximo 5%. | Atende diretamente ao RNF-003. |
| SLO-005 | Eventos pendentes na Outbox devem ser publicados após recuperação do broker ou do publisher. | Garante publicação recuperável. |
| SLO-006 | Eventos duplicados não devem duplicar efeito financeiro no DailyBalance. | Garante idempotência de consumo. |
| SLO-007 | A defasagem entre lançamento e consolidação deve ser observável por métricas de lag e backlog. | Garante visibilidade da consistência eventual. |
| SLO-008 | Mensagens com falha persistente devem ser isoladas sem bloquear todo o consumo. | Garante recuperação operacional. |
| SLO-009 | DailyBalance deve ser reconstruível a partir da fonte de verdade financeira. | Garante recuperação da visão derivada. |

---

## 9. Error budget

O requisito de até 5% de falhas ou perdas no pico do Consolidado pode ser tratado como limite de erro para a consulta do Consolidado durante o teste de carga.

Interpretação operacional:

```text
Em uma janela de pico de consulta ao Consolidado, até 5% das requisições elegíveis podem falhar ou ser perdidas.
```

Esse limite não autoriza perda de lançamentos financeiros.

Lançamentos persistidos devem permanecer recuperáveis e rastreáveis.

O erro budget se aplica ao comportamento de consulta do Consolidado, não à perda silenciosa de dados financeiros.

---

## 10. Alertas recomendados

Alertas devem priorizar impacto no usuário, risco financeiro e risco operacional.

Os sinais HTTP básicos já disponíveis para APIs devem alimentar alertas e decisões operacionais simples:

```text
- Ledger.Api /health/live: processo HTTP vivo
- Ledger.Api /health/ready: conexão com Ledger Database disponível
- Consolidation.Api /health/live: processo HTTP vivo
- Consolidation.Api /health/ready: conexão com Consolidation Database disponível
```

Esses sinais não substituem métricas, tracing, dashboards, medição de backlog/lag, DLQ ou SLIs de negócio. Workers ainda devem ser acompanhados por supervisão de processo, logs, backlog/lag e métricas futuras; não há endpoint HTTP artificial para workers neste incremento.

| Alerta | Condição indicativa | Impacto |
|---|---|---|
| Alta taxa de erro em Ledger.Api | Aumento de erros em `POST /entries`. | Risco direto no registro financeiro. |
| Ledger Database indisponível | Falha de conexão ou health check negativo. | Registro de lançamentos indisponível. |
| Crescimento da Outbox | Eventos pendentes aumentando continuamente. | Risco de atraso no Consolidado. |
| Evento antigo na Outbox | Idade do evento mais antigo acima do limite operacional. | Publicação atrasada. |
| Broker indisponível | Publisher ou worker sem conexão com broker. | Atraso na consolidação. |
| Backlog no broker | Mensagens prontas ou não confirmadas crescendo. | Worker não acompanha volume. |
| Falhas no Consolidation.Worker | Erros de processamento acima do esperado. | DailyBalance pode ficar defasado. |
| Mensagens em DLQ ou isolamento | Mensagens com falha persistente. | Necessidade de investigação operacional. |
| Alta taxa de erro em Consolidation.Api | Erros no `GET /daily-balances/{businessDate}`. | Consulta do relatório diário degradada. |
| Latência alta no Consolidado | Latência acima do limite definido. | Risco de não atender pico de consulta. |
| Lag de consolidação elevado | Diferença entre lançamento e DailyBalance cresce. | Consistência eventual fora do esperado. |

---

## 11. Dashboards recomendados

Dashboards devem ser organizados por visão operacional.

### 11.1 Dashboard executivo da solução

```text
- saúde geral das APIs
- saúde geral dos workers
- taxa de sucesso de Lançamentos
- taxa de sucesso do Consolidado
- RPS do Consolidado
- taxa de erro do Consolidado
- backlog e lag de consolidação
- mensagens isoladas
```

### 11.2 Dashboard de Lançamentos

```text
- requisições por minuto
- latência p95 e p99
- lançamentos registrados
- erros de validação
- repetições idempotentes
- conflitos de idempotência
- erros de banco
```

### 11.3 Dashboard de Outbox e broker

```text
- eventos pendentes
- idade do evento mais antigo
- eventos publicados
- falhas de publicação
- retries
- mensagens prontas
- mensagens não confirmadas
- redeliveries
- DLQ ou isolamento
```

### 11.4 Dashboard do Consolidado

```text
- eventos consumidos
- eventos processados
- eventos duplicados descartados
- falhas de processamento
- lag de consolidação
- consultas ao Consolidado
- latência de consulta
- taxa de erro de consulta
- RPS no pico
```

---

## 12. Recuperação por cenário

| Cenário | Sinais observáveis | Ação de recuperação | Validação pós-recuperação |
|---|---|---|---|
| Broker indisponível | Falhas de publicação, Outbox crescendo. | Recuperar broker, manter publisher em retry. | Eventos pendentes diminuem e publicações retomam. |
| Outbox acumulando | Idade do evento mais antigo aumentando. | Verificar publisher, broker, credenciais e erros de publicação. | Outbox volta a drenar. |
| Worker parado | Backlog no broker crescendo. | Reiniciar worker, verificar conexão com broker e banco. | Consumo retoma e lag reduz. |
| Evento duplicado | Métrica de duplicados descartados aumenta. | Confirmar idempotência e ausência de duplicidade no saldo. | DailyBalance permanece correto. |
| Mensagem com falha persistente | DLQ ou isolamento com mensagens. | Investigar causa, corrigir e reprocessar de forma controlada. | Mensagem processada ou descartada com justificativa. |
| DailyBalance inconsistente | Divergência entre fonte de verdade e projeção. | Executar rebuild por comerciante e período. | Totais reconciliados após rebuild. |
| Consolidation.Api degradada | Latência e erro aumentam. | Escalar API, verificar banco e queries. | Latência e taxa de erro retornam ao limite. |
| Ledger Database indisponível | Ledger.Api falha em registros. | Acionar recuperação prioritária do banco. | Registro de lançamentos volta a operar. |

---

## 13. Reprocessamento controlado

Reprocessamento deve ser tratado como operação controlada.

Etapas recomendadas:

```text
1. identificar evento ou conjunto de eventos elegíveis
2. verificar causa raiz da falha
3. corrigir configuração, dado ou código quando necessário
4. registrar solicitação de reprocessamento
5. executar reprocessamento
6. validar ausência de duplicidade
7. validar DailyBalance
8. registrar resultado
```

Reprocessamento não deve criar novo lançamento financeiro.

Reprocessamento deve reaplicar o efeito esperado sobre a visão derivada, preservando idempotência.

---

## 14. Reconstrução do DailyBalance

DailyBalance é reconstruível a partir dos lançamentos persistidos.

Etapas recomendadas para rebuild:

```text
1. definir comerciante e período afetado
2. registrar motivo do rebuild
3. pausar ou coordenar consumo quando necessário
4. limpar ou marcar projeções afetadas
5. reler lançamentos da fonte de verdade
6. recalcular créditos, débitos, saldo e quantidade
7. atualizar DailyBalance
8. validar totais
9. retomar consumo normal
10. registrar resultado operacional
```

O rebuild deve ser rastreável e não deve alterar os lançamentos originais.

---

## 15. Evidências esperadas de validação

Para demonstrar que a observabilidade atende ao desafio, as seguintes evidências devem ser produzidas durante implementação e testes:

```text
- teste de registro de lançamento com Consolidado indisponível
- teste de consulta do Consolidado com 50 RPS, usando `tests/Consolidation.LoadTests`
- medição da taxa de erro do Consolidado durante pico, usando `tests/Consolidation.LoadTests`
- teste de retry da Outbox
- teste de consumo duplicado sem duplicar saldo
- teste de backlog e posterior drenagem
- teste de mensagem isolada
- teste de rebuild de DailyBalance
- evidência de logs com correlation_id
- evidência de métricas principais expostas
```

O teste de carga reproduzível foi criado em `tests/Consolidation.LoadTests` e executado localmente/container-first contra a `Consolidation.Api` em `http://host.docker.internal:8081`.

Resultado observado na janela sustentada de 60 segundos a 50 RPS:

```text
- total de requisições: 3000
- sucessos: 3000
- falhas: 0
- taxa de sucesso: 100.00%
- taxa de falha: 0.00%
- p95: 4.50 ms
- p99: 5.68 ms
- throughput observado: 50.01 req/s
```

Os critérios de falhas elegíveis <= 5%, p95 <= 500 ms e p99 <= 1000 ms foram atendidos nessa execução local/container-first. Essa evidência não substitui validação produtiva, observabilidade completa, dashboards ou análise de capacidade em ambiente real.

A execução end-to-end local via Docker Compose permite subir APIs, workers, bancos e RabbitMQ para inspeção operacional do fluxo. Essa execução ajuda a validar o encadeamento local entre `Ledger.Api`, Outbox, RabbitMQ, `Consolidation.Worker` e `Consolidation.Api`, mas não substitui observabilidade completa, dashboards, métricas produtivas, DLQ completa ou validação de capacidade em ambiente produtivo ou equivalente.

---

## 16. Relação com requisitos e ASRs

| Item | Relação com observabilidade e recuperação |
|---|---|
| RNF-001 | Observa e valida que falhas no Consolidado não indisponibilizam Lançamentos. |
| RNF-002 | Mede o pico de 50 RPS no Consolidado. |
| RNF-003 | Mede a taxa de falhas ou perdas no pico do Consolidado. |
| ASR-005 | Mede defasagem temporária e recuperável do Consolidado. |
| ASR-010 | Define sinais de observabilidade do fluxo. |
| ASR-011 | Define recuperação para falhas de publicação, consumo e consolidação. |

---

## 17. Relação com ADRs

| ADR | Relação com observabilidade e recuperação |
|---|---|
| ADR-0002 | Outbox precisa de métricas de pendência, publicação, retry e falhas. |
| ADR-0003 | Consumo at-least-once exige métricas de duplicidade, retry e falhas. |
| ADR-0004 | DailyBalance exige métricas de atualização e capacidade de rebuild. |
| ADR-0007 | Broker exige métricas de backlog, redelivery e isolamento de falhas. |
| ADR-0008 | Unidades implantáveis exigem health checks e métricas por componente. |
| ADR-0009 | Stack tecnológica deve emitir logs, métricas e traces. |
| ADR-0010 | Execução local e produção possuem níveis diferentes de observabilidade. |
| ADR-0011 | Logs e traces devem evitar exposição de secrets e dados sensíveis. |
| ADR-0012 | Consolida decisões de observabilidade, SLIs, SLOs, alertas, recuperação e prontidão operacional. |

A decisão específica de observabilidade e prontidão operacional está registrada em `docs/decisions/ADR-0012-observabilidade-e-prontidao-operacional.md`.

---

## 18. Critérios de aceitação

| ID | Critério |
|---|---|
| OBS-CA-001 | APIs emitem métricas de requisição, latência e erro. |
| OBS-CA-002 | Workers emitem métricas de processamento, falhas e retries. |
| OBS-CA-003 | Outbox expõe quantidade de eventos pendentes e idade do evento mais antigo. |
| OBS-CA-004 | Broker expõe backlog, redelivery e mensagens isoladas. |
| OBS-CA-005 | Consolidado mede RPS, taxa de erro e latência. |
| OBS-CA-006 | Lag de consolidação é mensurável. |
| OBS-CA-007 | Eventos duplicados descartados são mensuráveis. |
| OBS-CA-008 | Reprocessamento e rebuild deixam evidência operacional. |
| OBS-CA-009 | Logs possuem correlation_id. |
| OBS-CA-010 | Logs não expõem secrets, tokens completos ou payloads sensíveis completos. |

---

## 19. Status

Documento em rascunho até a implementação dos sinais, validação produtiva ou equivalente de capacidade, dashboards e validações operacionais completas.
