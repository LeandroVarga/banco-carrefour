# Evidências do Case

Este documento mapeia os requisitos do desafio técnico para evidências existentes no repositório.

Os status distinguem implementação, validação local/container-first, documentação, pipeline, IaC e pendências produtivas. Nenhum item deve ser interpretado como prontidão produtiva.

| Requisito do case | Evidência no repositório | Status | Observação |
|---|---|---|---|
| Serviço de controle de lançamentos | `POST /entries` em [contracts/openapi.yaml](../../contracts/openapi.yaml), implementação em `Ledger.Api`, persistência no Ledger Database e idempotência por `merchant_id + Idempotency-Key`. | Implementado | O comerciante é derivado do token local; hardening produtivo de identidade permanece pendente. |
| Serviço de consolidado diário | `Consolidation.Worker`, `Consolidation.Persistence`, `DailyBalance` e `ProcessedEvent`; documentos em [08-implementation-readiness.md](../architecture/08-implementation-readiness.md) e [traceability.md](../traceability.md). | Implementado | Consolidado é visão derivada e eventualmente consistente; `DailyBalance` usa upsert atômico para evitar lost update no banco. |
| Relatório diário consolidado | `GET /daily-balances/{businessDate}` em [contracts/openapi.yaml](../../contracts/openapi.yaml), `Consolidation.Api` e testes de integração. | Implementado | `404` significa projeção indisponível, não saldo zero confirmado. |
| Registro de lançamentos independente do Consolidado | Separação entre Ledger e Consolidado, Outbox transacional, comunicação RabbitMQ local, ADR-0001, ADR-0002 e ADR-0003. | Implementado | Falha no Consolidado pode atrasar a projeção, mas não cria chamada síncrona no registro. |
| 50 RPS com no máximo 5% de falha/perda | [teste-de-carga-consolidado.md](teste-de-carga-consolidado.md) e `tests/Consolidation.LoadTests`. | Validado localmente/container-first | Execução observada: 3000 requisições planejadas, 3000 executadas, 3000 sucessos, 0 falhas, 50.02 req/s, p95 5.80 ms e p99 7.51 ms. |
| Documentação em `docs/architecture` | Jornada, contexto, requisitos, blocos, solução, diagramas, rastreabilidade e prontidão. | Documentado | [08-implementation-readiness.md](../architecture/08-implementation-readiness.md) registra o que foi materializado e o que segue pendente. |
| Documentação em `docs/security` | [arquitetura-de-seguranca.md](../security/arquitetura-de-seguranca.md) e ADR-0011. | Documentado | Segurança produtiva e hardening de identidade permanecem pendentes. |
| Documentação em `docs/decisions` | [registro-de-decisoes.md](../decisions/registro-de-decisoes.md) e ADR-0000 a ADR-0015. | Documentado | ADR-0010 cobre AWS como plataforma de referência; ADR-0015 cobre CI/CD, imagens e Terraform. |
| Documentação em `docs/operations` | Arquitetura operacional, observabilidade, teste de carga, runbook local e esta matriz. | Documentado | Runbooks produtivos completos ainda não foram implementados. |
| ADRs | [registro-de-decisoes.md](../decisions/registro-de-decisoes.md) cobre fronteiras, Outbox, consumo, persistência, broker, runtime, segurança, observabilidade e contratos. | Documentado | Novas ADRs devem ser criadas apenas para novas decisões arquiteturais. |
| Segurança | JWT local com assinatura, expiração, issuer, audience e `merchant_id`, helper `local-jwt`, contratos sem `merchantId` no corpo, documentação de segurança e ADR-0011. | Implementado / Documentado | Identidade local é adequada para avaliação; produção exige provedor corporativo/cloud, HTTPS, rotação de chaves e secret manager. |
| Operação | Docker Compose, migrations efêmeras, health checks das APIs, RabbitMQ Management, DLQ/retry local e docs operacionais. | Implementado / Documentado | DLQ/retry local usa republicação confirmada e roteada antes do ack da original. |
| Observabilidade | OpenTelemetry nas quatro unidades, logs estruturados, traces customizados, métricas customizadas, OTLP configurável e Aspire Dashboard local. | Implementado / Documentado | Visualização local/dev não substitui plataforma produtiva, alertas, dashboards ou retenção centralizada. |
| Escalabilidade | Fronteiras independentes, APIs e workers separados, leitura via `DailyBalance` e requisito de 50 RPS validado localmente. | Documentado / Validado localmente/container-first | Capacidade produtiva ou equivalente permanece pendente. |
| Recuperação | Outbox recuperável, consumo at-least-once, deduplicação por `ProcessedEvent`, DLQ local, retry local finito e estratégia de rebuild documentada. | Implementado / Documentado | Reprocessamento assistido de DLQ e rebuild operacional completo ainda não foram implementados. |
| Testes | Testes de contrato, integração de Ledger, Outbox, projeção, concorrência do `DailyBalance`, JWT local, consumer e API do Consolidado. | Implementado | O teste de carga é executado separadamente de `dotnet test`. |
| Execução local | [docker-compose.yml](../../docker-compose.yml), [runbook-demonstracao-local.md](runbook-demonstracao-local.md), migrations, helper `local-jwt` e serviços de aplicação via Compose. | Implementado | Execução local não representa alta disponibilidade real nem topologia produtiva final. |
| CI | [.github/workflows/ci.yml](../../.github/workflows/ci.yml) executa build, testes e `git diff --check` via Docker Compose. | Implementado | Não publica imagens nem faz deploy AWS. |
| CI/CD AWS como referência | ADR-0015 e [runbook-implantacao-aws.md](runbook-implantacao-aws.md). | Documentado | OIDC, ECR, Terraform e ECS estão definidos como referência, mas não executados. |
| IaC como referência | [infra/README.md](../../infra/README.md) e ADR-0015. | Documentado | Não há Terraform funcional aplicado neste estado. |
| Custos | [estimativa-de-custos.md](estimativa-de-custos.md) inclui direcionadores AWS de referência. | Documentado | Sem valores fixos ou cotação oficial; deve ser recalculado antes de decisão produtiva. |

## Pendências preservadas

Permanecem fora do baseline local atual:

- validação de capacidade em ambiente produtivo ou equivalente;
- rate limiting distribuído/produtivo;
- observabilidade produtiva;
- dashboards, alertas e retenção centralizada de logs em produção;
- reprocessamento assistido da DLQ;
- reconstrução/reprocessamento operacional completo;
- multi-publisher seguro;
- validação produtiva de múltiplos workers, backlog e autoscaling;
- hardening produtivo de autenticação/autorização;
- publicação de imagens no ECR, execução de Terraform em ambiente AWS, deploy no ECS e smoke tests AWS.

## Documentos de apoio

| Tema | Documento |
|---|---|
| Runbook local | [runbook-demonstracao-local.md](runbook-demonstracao-local.md) |
| Teste de carga do Consolidado | [teste-de-carga-consolidado.md](teste-de-carga-consolidado.md) |
| Rastreabilidade de implementação | [traceability.md](../traceability.md) |
| Prontidão para implementação | [08-implementation-readiness.md](../architecture/08-implementation-readiness.md) |
| Observabilidade e recuperação | [observabilidade-sli-slo-e-recuperacao.md](observabilidade-sli-slo-e-recuperacao.md) |
| Registro de decisões | [registro-de-decisoes.md](../decisions/registro-de-decisoes.md) |
