---
adr_id: ADR-0012
titulo: Observabilidade e objetivos operacionais
status: Aceita
categoria: Operação
altitude_decisoria: Estrutural
data: 2026-07-28
responsavel: Arquitetura de Soluções
---

# ADR-0012 — Observabilidade e objetivos operacionais

## 1. Contexto

O Ledger precisa permanecer disponível mesmo com falha do Consolidation, o Consolidation precisa sustentar 50 RPS com no máximo 5% de falhas elegíveis, e o fluxo assíncrono entre as duas fronteiras precisa ser diagnosticável. Sem observabilidade estruturada, nenhum desses requisitos é verificável em operação.

## 2. Pergunta arquitetural

Como a solução é observada, medida, diagnosticada e operada em relação aos seus requisitos não funcionais?

## 3. Decisão

A solução emite logs estruturados, métricas e traces via OpenTelemetry, com correlação entre requisição, evento e processamento. Localmente, a telemetria é exportada por OTLP para o Aspire Dashboard; na referência AWS, a mesma instrumentação é materializada via ADOT, CloudWatch Logs/Metrics/Alarms e X-Ray.

Os SLIs e SLOs cobrem: taxa de sucesso do registro de lançamentos, latência e taxa de falha da consulta do Consolidado (limite de 50 RPS/≤5% de falha elegível), atraso entre lançamento e consolidação, backlog da Outbox, duplicidade detectada na aplicação de eventos, e o resultado de execuções da task de migração.

A observabilidade de mensageria usa a terminologia real de Amazon SQS e CloudWatch onde o serviço expõe um dado nativo — `ApproximateNumberOfMessagesVisible`, `ApproximateNumberOfMessagesNotVisible`, `ApproximateAgeOfOldestMessage`, `NumberOfMessagesSent`, `NumberOfMessagesReceived`, `NumberOfMessagesDeleted` — e nunca apresenta esses valores aproximados como contagens exatas. SQS não expõe um contador nativo de redelivery no estilo de gerenciamento de broker tradicional; onde esse dado é necessário, a solução usa métricas de aplicação (detecções de duplicidade/idempotência em `ProcessedEvent`, classificação de tentativas de reprocessamento), nunca uma métrica nativa inexistente.

Alarmes de deployment (ADR-0014) e de falha de migração (ADR-0015) usam eventos reais emitidos pela aplicação ou eventos nativos do ECS — nunca um nome de métrica fabricado sem emissão correspondente no código.

## 4. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar |
|---|---|---|
| Apenas logs textuais | Diagnóstico só por logs livres. | Dificulta medir as RNFs de disponibilidade, backlog e taxa de falha. |
| Métricas apenas de infraestrutura | CPU, memória e rede, sem métricas de fluxo de negócio. | Não demonstra se lançamentos, Outbox e consolidação estão saudáveis. |
| Terminologia de métrica de outro broker aplicada à mensageria real | Nomear métricas como filas "ready"/"unacked"/redelivery no estilo de gerenciamento de broker tradicional. | Rejeitada. SQS não expõe esse modelo de estado de fila; usar essa terminologia sugere um comportamento que o serviço real não tem. |
| OpenTelemetry com terminologia real de SQS/CloudWatch e métricas de aplicação onde o serviço não expõe o dado nativo | Instrumentação vendor-neutral, com nomes de métrica fiéis ao serviço real usado. | Alternativa adotada. Permite medir as RNFs sem sugerir comportamento inexistente do serviço de mensageria. |

## 5. Trade-offs

Manter nomes de métrica fiéis ao serviço real exige atenção redobrada ao migrar terminologia entre tecnologias de mensageria, em troca de dashboards e alarmes que nunca sugerem uma garantia que a AWS não oferece.

## 6. Consequências

Dashboards por ambiente expõem widgets para Ledger, Consolidation, APIs e deployment, com métricas já emitidas pela aplicação claramente distintas de métricas contratadas para instrumentação futura ainda não emitida.

## 7. Guardrails

- Nenhum nome de métrica no estilo de outros brokers (`broker_messages_ready_total`, `broker_messages_unacked_total`, `broker_redeliveries_total` ou equivalente) é usado para descrever SQS.
- Métricas `Approximate*` do SQS nunca são apresentadas como contagens exatas.
- Todo alarme de deployment ou de migração referencia um evento real emitido pela aplicação ou um evento nativo do ECS, nunca uma métrica fabricada.

## 8. Risco arquitetural evitado

Uma implementação futura não deve operar a solução sem correlação ponta a ponta, sem alarmes lastreados em SLO, nem usar terminologia de métrica de outro broker para descrever o comportamento real do SQS.

## 9. ASRs relacionados

ASR-001 (Ledger disponível mesmo com falha do Consolidation), ASR-002/ASR-003 (50 RPS, ≤5% de falha), ASR-005 (defasagem observável e recuperável), ASR-010 (fluxo observável), ASR-011 (falhas recuperáveis).

## 10. ABBs e SBBs relacionados

ABB-013 (Observabilidade do Fluxo), ABB-014 (Recuperação Operacional); SBB-016 (Observability), SBB-017 (Operational Recovery).

## 11. Evidências de implementação

`docs/operations/observabilidade-sli-slo-e-recuperacao.md`, `infra/terraform/modules/{observability,deployment-alarms}`, `infra/terraform/environments/{development,staging,production}/main.tf` (`aws_cloudwatch_dashboard.this`), `infra/terraform/modules/ecs-migration-task/observability.tf`.

## 12. ADRs relacionados

ADR-0004 (integração assíncrona confiável), ADR-0011 (plataforma AWS e isolamento de ambientes), ADR-0014 (promoção, deployment e rollback por workload), ADR-0015 (governança de migrations de banco de dados).
