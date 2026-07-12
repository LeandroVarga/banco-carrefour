# Evidencias do Case

Este documento mapeia requisitos do desafio tecnico para evidencias existentes no repositorio.

Os status abaixo distinguem implementacao, validacao local/container-first, documentacao e pendencias produtivas. Nenhum item deve ser interpretado como "completo em producao".

| Requisito do case | Evidencia no repositorio | Status | Observacao |
|---|---|---|---|
| Servico de controle de lancamentos | `POST /entries` em `contracts/openapi.yaml`; implementacao em `Ledger.Api`; persistencia no Ledger Database; idempotencia por `merchant_id + Idempotency-Key`. | Implementado | O merchant e derivado do token local; hardening produtivo de identidade permanece pendente. |
| Servico de consolidado diario | `Consolidation.Worker`, `Consolidation.Persistence`, `DailyBalance` e `ProcessedEvent`; documentos em `docs/architecture/08-implementation-readiness.md` e `docs/traceability.md`. | Implementado | Consolidado e visao derivada e eventualmente consistente. |
| Relatorio/consulta diaria consolidada | `GET /daily-balances/{businessDate}` em `contracts/openapi.yaml`; `Consolidation.Api`; testes de integracao. | Implementado | `404` significa projecao indisponivel, nao saldo zero confirmado. |
| Registro de lancamentos nao fica indisponivel se Consolidado falhar | Separacao entre Ledger e Consolidado; Outbox transacional; comunicacao RabbitMQ; ADR-0001, ADR-0002 e ADR-0003. | Implementado | Falha no Consolidado pode gerar atraso de projecao, mas nao chamada sincrona no registro. |
| Consolidado suporta pico de 50 RPS com no maximo 5% de falha/perda | `docs/operations/teste-de-carga-consolidado.md`; `tests/Consolidation.LoadTests`. | Validado localmente/container-first | Evidencia observada: 50.01 req/s sustentado, 0% falhas, p95 4.50 ms e p99 5.68 ms. Nao substitui validacao produtiva. |
| Documentacao em `docs/architecture` | Jornada, contexto, requisitos, blocos, solucao, diagramas, rastreabilidade e prontidao para implementacao. | Documentado | `docs/architecture/08-implementation-readiness.md` registra materializado e pendente. |
| Documentacao em `docs/security` | `docs/security/arquitetura-de-seguranca.md`; ADR-0011. | Documentado | Segurança produtiva e hardening de identidade permanecem pendentes. |
| Documentacao em `docs/decisions` | Registro de decisoes e ADR-0000 a ADR-0014. | Documentado | ADR-0014 cobre OpenTelemetry e Aspire local. |
| Documentacao em `docs/operations` | Arquitetura operacional, observabilidade, teste de carga, runbook de demonstracao e esta matriz de evidencias. | Documentado | Runbooks produtivos completos ainda nao estao implementados. |
| ADRs | `docs/decisions/registro-de-decisoes.md`; ADRs de fronteiras, Outbox, consumo, persistencia, broker, runtime, seguranca, observabilidade e contratos. | Documentado | Novas ADRs devem ser criadas apenas quando houver nova decisao arquitetural. |
| Segurança | JWT local, `merchant_id` derivado do token, helper container-first `local-jwt`, contratos sem `merchantId` no corpo, docs de seguranca e ADR-0011. | Implementado / Documentado | Identidade local e adequada para avaliacao; producao exige provedor corporativo/cloud e secrets manager. |
| Rate limiting basico | `Ledger.Api` e `Consolidation.Api` aplicam rate limiting local/in-memory em `POST /entries` e `GET /daily-balances/{businessDate}`; testes cobrem `HTTP 429` com limite baixo configurado. | Implementado / Testado | Health endpoints ficam fora do rate limit. Rate limiting distribuido/produtivo permanece pendente. |
| Operacao | Docker Compose, migrations efemeras, health das APIs, RabbitMQ Management, DLQ/retry local, docs operacionais. | Implementado / Documentado | Operacao produtiva completa de mensagens isoladas e rebuild/reprocessamento permanecem pendentes. |
| Monitoramento, logs e observabilidade | OpenTelemetry nas quatro unidades, logs estruturados, traces customizados, metricas customizadas, OTLP configuravel e Aspire Dashboard local. | Implementado / Documentado | Visualizacao local/dev; nao substitui plataforma produtiva, alertas, dashboards produtivos ou retencao centralizada. |
| Escalabilidade | Fronteiras independentes, APIs e workers separados, leitura via DailyBalance, requisito de 50 RPS validado localmente. | Documentado / Validado localmente/container-first | Capacidade produtiva ou equivalente permanece pendente. Baseline atual recomenda uma replica para `Ledger.OutboxPublisher` e uma para `Consolidation.Worker` ate evolucao de concorrencia. |
| Recuperacao | Outbox recuperavel, consumo at-least-once, deduplicacao por `ProcessedEvent`, DLQ local, retry local finito e estrategia documentada de rebuild. | Implementado / Documentado | Reprocessamento assistido de DLQ e rebuild operacional completo ainda nao foram implementados. |
| Testes | Testes de contrato, integracao de Ledger, Outbox, projecao, consumer e API do Consolidado; 90 testes automatizados documentados no contexto do incremento. | Implementado | O teste de carga e executado separadamente de `dotnet test`. |
| Execucao local | `docker-compose.yml`; `docs/operations/runbook-demonstracao-local.md`; migrations, helper `local-jwt` e servicos de aplicacao via Compose. | Implementado | Execucao local nao representa alta disponibilidade real nem topologia produtiva final. |
| Contratos HTTP | `contracts/openapi.yaml` com `POST /entries`, `GET /daily-balances/{businessDate}` e health das APIs. | Implementado | Contratos nao foram alterados por esta matriz. |
| Contrato de evento | `contracts/events/entry-created-v1.schema.json`; ADR-0013. | Implementado | Contrato de evento nao foi alterado por esta matriz. |
| Execucao end-to-end | Runbook local com Ledger.Api, Outbox, RabbitMQ, Consolidation.Worker e Consolidation.Api. | Validado localmente/container-first | Deve ser executado pelo avaliador localmente para reproduzir evidencia no ambiente dele. |
| Estimativa de custos | `docs/operations/estimativa-de-custos.md` inclui cenario monetario de referencia para producao minima. | Documentado | Faixa em R$ e ordem de grandeza, nao cotacao oficial. Deve ser recalculada antes de decisao produtiva. |

## Pendencias preservadas

Permanecem fora do escopo deste incremento documental:

```text
- validacao de capacidade em ambiente produtivo ou equivalente
- rate limiting distribuido/produtivo
- observabilidade produtiva completa
- dashboards produtivos, alertas produtivos e retencao centralizada de logs
- plataforma final de observabilidade
- sinais operacionais aprofundados dos workers, Outbox e broker
- reprocessamento assistido da DLQ
- reconstrucao/reprocessamento operacional completo
- multi-publisher seguro
- multi-worker seguro
- backoff avancado e operacao produtiva completa de mensagens isoladas
- hardening produtivo de autenticacao/autorizacao
- deploy produtivo/IaC
```

## Documentos de apoio

| Tema | Documento |
|---|---|
| Runbook local de demonstracao | `docs/operations/runbook-demonstracao-local.md` |
| Teste de carga do Consolidado | `docs/operations/teste-de-carga-consolidado.md` |
| Rastreabilidade de implementacao | `docs/traceability.md` |
| Prontidao para implementacao | `docs/architecture/08-implementation-readiness.md` |
| Observabilidade e recuperacao | `docs/operations/observabilidade-sli-slo-e-recuperacao.md` |
| Registro de decisoes | `docs/decisions/registro-de-decisoes.md` |
