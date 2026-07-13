---
adr_id: ADR-0014
titulo: Instrumentação de Observabilidade com OpenTelemetry
status: Aceita
data: 2026-07-12
responsavel: Arquitetura de Soluções
decisao_relacionada: Instrumentação vendor-neutral de logs, traces e métricas
---

# ADR-0014 — Instrumentação de Observabilidade com OpenTelemetry

## 1. Contexto

A solução já define observabilidade, SLIs, SLOs e prontidão operacional em `ADR-0012`, mas ainda precisava materializar um baseline executável de logs estruturados, traces e métricas.

Esse baseline deve permitir demonstração local e preservar portabilidade por OTLP.

Como a ADR-0010 define AWS como plataforma de referência do case, a materialização cloud de referência deve usar ADOT, CloudWatch e X-Ray, sem abandonar OpenTelemetry como padrão vendor-neutral.

---

## 2. Decisão

A solução adotará OpenTelemetry como padrão vendor-neutral de instrumentação.

Serão usados:

```text
- ILogger para logs estruturados
- ActivitySource para traces customizados
- Meter para métricas customizadas
- OTLP exporter configurável por ambiente
- Aspire Dashboard como backend local de demonstração
- ADOT, CloudWatch e X-Ray como materialização AWS de referência
```

O Aspire Dashboard será usado somente no Docker Compose local para visualizar logs, traces e métricas em uma UI única.

Na AWS de referência, a exportação OTLP deve seguir via ADOT ou coletor aprovado para CloudWatch e X-Ray.

Aplicações devem exportar telemetria por OTLP quando `OTEL_EXPORTER_OTLP_ENDPOINT` estiver configurado. Na ausência desse endpoint, as aplicações devem continuar inicializando e executando normalmente.

---

## 3. Alternativas consideradas

| Alternativa | Descrição | Avaliação |
|---|---|---|
| Apenas logs | Manter diagnóstico somente por logs estruturados. | Simples, mas insuficiente para demonstrar traces e métricas do fluxo. |
| Jaeger isolado | Usar Jaeger para traces locais. | Bom para traces, mas não cobre logs e métricas em uma experiência única. |
| Prometheus isolado | Usar Prometheus para métricas. | Bom para métricas, mas não cobre logs e traces; exigiria outro backend. |
| Stack Grafana/Loki/Tempo/Prometheus | Stack local completa para logs, traces, métricas e dashboards. | Poderosa, mas amplia escopo e sugere uma plataforma produtiva antes da decisão final. |
| SigNoz | Plataforma integrada baseada em OpenTelemetry. | Boa opção, mas fixa um backend específico para este incremento. |
| ADOT, CloudWatch e X-Ray | Materialização AWS da instrumentação OpenTelemetry. | Alternativa adotada para a referência AWS do case. |
| Datadog | Backend SaaS gerenciado. | Pode ser adequado em produção, mas cria dependência comercial específica. |
| OpenTelemetry + Aspire Dashboard local | Instrumentação vendor-neutral com visualização local simples. | Alternativa adotada para baseline demonstrável sem lock-in produtivo. |

---

## 4. Trade-offs

Consequências positivas:

```text
- reduz lock-in de observabilidade
- permite trocar o backend por ambiente
- fornece baseline local de logs, traces e métricas
- melhora diagnóstico do fluxo entre API, Outbox, broker, worker e consulta
- mantém compatibilidade com plataformas futuras via OTLP
```

Custos e cuidados:

```text
- adiciona pacotes e configuração de instrumentação
- exige disciplina de nomes de métricas, spans e tags
- exige cuidado com cardinalidade, especialmente em métricas
- exige evitar payloads sensíveis, tokens e connection strings nos sinais
- Aspire Dashboard é local/dev e não substitui plataforma produtiva
```

---

## 5. Consequências

As unidades implantáveis passam a expor instrumentação própria:

```text
- BancoCarrefour.Ledger.Api
- BancoCarrefour.Ledger.OutboxPublisher
- BancoCarrefour.Consolidation.Worker
- BancoCarrefour.Consolidation.Api
```

O Docker Compose local passa a incluir `aspire-dashboard` para demonstração.

Backends produtivos continuam substituíveis, desde que aceitem OTLP diretamente ou por collector/gateway aprovado.

Esta decisão não implementa:

```text
- dashboards produtivos
- alertas produtivos
- retenção centralizada de logs
- plataforma produtiva aplicada fora da referência AWS
- OpenTelemetry Collector obrigatório
- backend cloud ou SaaS específico
```

---

## 6. Relação com documentos

Esta decisão complementa:

```text
- ADR-0012 Observabilidade e Prontidão Operacional
- docs/operations/arquitetura-operacional.md
- docs/operations/observabilidade-sli-slo-e-recuperacao.md
- docs/traceability.md
- docs/architecture/08-implementation-readiness.md
```

---

## 7. Status

Decisão aceita para materializar observabilidade operacional mínima e demonstrável no ambiente local/container-first.
