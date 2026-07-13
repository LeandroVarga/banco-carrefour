---
adr_id: ADR-0012
titulo: Observabilidade e Prontidão Operacional
status: Aceita
data: 2026-07-11
responsavel: Arquitetura de Soluções
decisao_relacionada: Logs, métricas, traces, SLIs, SLOs, alertas, recuperação e critérios operacionais
---

# ADR-0012 — Observabilidade e Prontidão Operacional

## 1. Contexto

A solução possui fluxo assíncrono entre Lançamentos e Consolidado.

Lançamentos é a fronteira responsável pela fonte de verdade financeira.

Consolidado é uma visão derivada e reconstruível, atualizada por eventos publicados a partir da Outbox.

O desafio exige disponibilidade, confiabilidade, desempenho, recuperação de falhas, monitoramento, logs, observabilidade, escalabilidade e demonstração de atendimento aos requisitos não funcionais.

Os principais requisitos operacionais são:

```text
- Lançamentos não deve ficar indisponível caso Consolidado falhe
- Consolidado deve suportar pico de 50 RPS
- Consolidado deve limitar falhas ou perdas de requisições elegíveis a no máximo 5% no pico
- falhas no fluxo assíncrono devem ser observáveis e recuperáveis
- DailyBalance deve poder ser reconstruído a partir da fonte de verdade
```

---

## 2. Decisão

A solução adotará observabilidade estruturada e critérios de prontidão operacional para APIs, workers, bancos, broker/fila, Outbox, processamento assíncrono e consulta do Consolidado.

A arquitetura deve emitir logs estruturados, métricas e correlação entre requisições, eventos e processamento.

Na execução local, OpenTelemetry exporta por OTLP para o Aspire Dashboard. Na AWS de referência do case, a materialização operacional usa ADOT, CloudWatch Logs, CloudWatch Metrics, CloudWatch Alarms, X-Ray, métricas de SQS, alarmes de DLQ, backlog da Outbox, latência entre lançamento e consolidação e dashboards operacionais.

Devem ser definidos SLIs e SLOs para registro de lançamentos, consulta do Consolidado, taxa de falhas, latência, Outbox, backlog, lag, eventos duplicados, mensagens isoladas e reconstrução do DailyBalance.

A operação deve prever health checks, readiness, liveness, alertas, dashboards, retry, backoff, isolamento de mensagens com falha persistente, reprocessamento controlado e rebuild do Consolidado.

Os documentos operacionais de referência são:

```text
- docs/operations/arquitetura-operacional.md
- docs/operations/observabilidade-sli-slo-e-recuperacao.md
- docs/operations/estimativa-de-custos.md
```

---

## 3. O que a decisão inclui

Esta decisão inclui:

```text
- logs estruturados
- métricas por unidade implantável
- correlação entre requisição, evento e consolidação
- SLIs e SLOs propostos
- error budget aplicado ao Consolidado no pico
- alertas operacionais
- dashboards recomendados
- health checks, readiness e liveness
- retry e backoff
- isolamento de mensagens com falha persistente
- reprocessamento controlado
- reconstrução da projeção DailyBalance
- evidências esperadas de validação
- critérios de prontidão operacional
- direcionadores de custo operacional
- materialização AWS de referência com ADOT, CloudWatch, X-Ray, métricas de SQS e alarmes de DLQ
```

---

## 4. O que fica fora desta decisão

Esta decisão não define:

```text
- conta, região, retenção e custos finais de CloudWatch/X-Ray
- desenho final de dashboards por ambiente
- limites finais de alarmes
- limites finais de retenção
- política final de plantão ou escalonamento
- RTO e RPO definitivos de produção
- política final de disaster recovery
- valores reais de custo de provedor cloud
```

Esses pontos dependem da plataforma corporativa, cloud, região, política operacional e criticidade final de produção.

---

## 5. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar como base da solução |
|---|---|---|
| Apenas logs textuais | Diagnóstico baseado somente em logs livres. | Dificulta medir RNFs, backlog, lag e taxa de falhas. |
| Métricas apenas de infraestrutura | Mede CPU, memória e rede, mas não mede fluxo de negócio. | Não demonstra se lançamentos, Outbox, broker e Consolidado estão saudáveis. |
| Observabilidade apenas depois da implementação | Sinais seriam definidos tarde, após decisões críticas já estarem codificadas. | Aumenta risco de lacunas em testes e operação. |
| Observabilidade estruturada por fluxo | Logs, métricas, correlação, SLIs, SLOs, alertas e evidências planejadas desde a arquitetura. | Alternativa adotada. Permite validar os RNFs do desafio e operar o fluxo assíncrono. |

---

## 6. Consequências

Consequências positivas:

```text
- permite demonstrar atendimento aos RNFs
- permite medir o pico de 50 RPS no Consolidado
- permite medir o limite de 5% de falhas no pico
- permite detectar atraso entre lançamento e consolidação
- permite diagnosticar Outbox, broker e workers
- permite recuperar mensagens com falha persistente
- permite validar idempotência e descarte de duplicidades
- permite reconstruir DailyBalance com evidência operacional
- melhora suporte, auditoria e investigação
```

Consequências e tradeoffs:

```text
- aumenta volume de logs e métricas
- exige cuidado com cardinalidade de métricas
- exige evitar exposição de dados sensíveis em logs
- exige disciplina de correlation_id
- exige dashboards e alertas úteis, não apenas técnicos
- pode gerar custo relevante em plataformas de observabilidade
- exige manutenção dos sinais conforme a arquitetura evolui
```

---

## 7. Relação com requisitos, ABBs e SBBs

Esta decisão sustenta principalmente:

```text
- RNF-001: Lançamentos não deve ficar indisponível caso Consolidado falhe
- RNF-002: Consolidado deve suportar 50 RPS em pico
- RNF-003: Consolidado deve limitar falhas ou perdas a no máximo 5% no pico
- ASR-005: Defasagem do Consolidado deve ser observável e recuperável
- ASR-010: Fluxo deve ser observável
- ASR-011: Falhas devem ser recuperáveis
- ABB-013: Observabilidade do Fluxo
- ABB-014: Recuperação Operacional
- SBB-016: Observability
- SBB-017: Operational Recovery
- SBB-018: Containers and Local Runtime
- SBB-019: Configuration and Secrets
```

---

## 8. Relação com documentos

Esta decisão sustenta:

```text
- docs/operations/arquitetura-operacional.md
- docs/operations/observabilidade-sli-slo-e-recuperacao.md
- docs/operations/estimativa-de-custos.md
- docs/architecture/04-blocos-de-solucao.md
- docs/architecture/05-arquitetura-da-solucao.md
- docs/architecture/07-rastreabilidade.md
```

---

## 9. Status

Decisão aceita para o escopo inicial da solução.
