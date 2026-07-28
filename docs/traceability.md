# Rastreabilidade de Implementação

Este documento resume o estado de implementação materializado pelos incrementos de Ledger write path e projeção do Consolidado, sem substituir a rastreabilidade arquitetural em `docs/architecture/07-rastreabilidade.md`.

## Estado por capacidade

| Capacidade | Estado atual | Observação |
|---|---|---|
| Registro de lançamentos | Implementado e migrado para Arquitetura Hexagonal | `POST /entries` chama `IRegisterFinancialEntryUseCase`; `Ledger.Application` orquestra o registro e `Ledger.Infrastructure` persiste Entry, InputIdempotency e Outbox em transação local. |
| Autenticação do Ledger | Implementada com Keycloak real | Keycloak (OIDC/RS256, discovery, JWKS reais) valida assinatura, expiração, issuer e audience distinta por API, com `merchant_id` derivado do token autenticado (ADR-0007). Hardening produtivo (IdP gerenciado real) permanece pendente. |
| Idempotência de entrada | Implementada no Ledger | Escopo por `merchant_id + Idempotency-Key`, com fingerprint canônico e conflito para payload divergente. |
| Publicação assíncrona | Implementada com Publisher hexagonal | Outbox transacional, `IOutboxStore`, `IIntegrationEventPublisher`, claim recuperável com `FOR UPDATE SKIP LOCKED` e publicação em SQS local via LocalStack. |
| Independência do Consolidado | Materializada por Outbox/SQS | `POST /entries` não depende de chamada síncrona ao Consolidado; `Consolidation.Worker` consome `FinancialEntryRegistered.v1` via SQS e mantém compatibilidade transitória com `EntryCreated.v1`. |
| Projeção DailyBalance | Implementada em arquitetura hexagonal | `DailyBalance` é atualizada pelo `IApplyFinancialEntryUseCase` com upsert atômico PostgreSQL para CREDIT/DEBIT e deduplicação por `eventId` em `ProcessedEvent`. |
| Consumo SQS do Consolidado | Implementado localmente | Sucesso e duplicado excluem a mensagem; erro de validação, JSON inválido e falha transitória mantêm a mensagem para redelivery e DLQ por redrive policy. |
| DLQ básica do Consolidado | Implementada localmente via SQS | Terraform local provisiona `financial-entry-registered` e `financial-entry-registered-dlq` no LocalStack. |
| Consulta do consolidado diário | Implementada | `GET /daily-balances/{businessDate}` consulta por `merchant_id` derivado do token e retorna 404 para projeção indisponível sem afirmar saldo zero. |
| Rebuild/reprocessamento operacional | Pendente/parcialmente documentado | Estratégia documentada, mas mecanismo operacional completo ainda não implementado. |
| Testes automatizados | Implementados no baseline local atual | Existem testes de contrato, persistência, Ledger write path, Outbox publisher, projeção, consumer, APIs, idempotência concorrente e validação runtime de evento. O teste de carga do Consolidado foi criado e executado localmente/container-first. |
| CI | Implementado para validação container-first | `.github/workflows/ci.yml` executa build, testes e `git diff --check` via Docker Compose. |
| CI/CD, imagens e Terraform | Implementado e validado estruturalmente | ADR-0013 e ADR-0014 definem GitHub Actions com OIDC para AWS, publicação no ECR, Terraform e deploy no ECS. Terraform validado (`fmt`/`init`/`validate`/`plan`) e workflows validados estruturalmente; nenhuma execução real contra AWS ainda ocorreu. |
| AWS como plataforma de referência | Documentado | ADR-0011 mapeia ABB/SBB para API Gateway com WAF, VPC Link V2, ALB interno, ECS Fargate, ECR, RDS PostgreSQL, SQS/DLQ, Secrets Manager/SSM, KMS, CloudWatch, X-Ray, ADOT e Terraform. |
| Execução end-to-end local via Compose | Implementada | `docker-compose.yml` inclui APIs, workers, bancos, LocalStack SQS, Terraform local e serviços efêmeros de migration para schema local. |
| Obtenção de token para testes locais | Implementada via Keycloak real | O helper `local-jwt` (HS256) foi removido. Tokens são obtidos por client-credentials real contra o Keycloak (`merchant-a-test-client`/`merchant-b-test-client`) via `https://keycloak.localhost:8443`, conforme [runbook-demonstracao-local.md](operations/runbook-demonstracao-local.md). |
| Fundação hexagonal do Ledger | Implementada e testada | `Ledger.Domain`, `Ledger.Application` e `Ledger.Infrastructure` separam domínio, caso de uso, porta transacional e persistência EF/Npgsql. Testes arquiteturais protegem a direção das dependências. |
| Evento FinancialEntryRegistered.v1 | Implementado com compatibilidade transitória | Novos lançamentos publicam `FinancialEntryRegistered.v1`; `EntryCreated.v1` permanece aceito pelo Consolidation para mensagens antigas. |
| 50 RPS do Consolidado | Validado localmente/container-first | Execução autenticada por credenciais reais do Keycloak contra `Consolidation.Api` real atingiu 3000 requisições planejadas/executadas na janela sustentada, 50.01 req/s, 0% falhas, p95 4.23 ms e p99 6.39 ms, validando throughput mínimo observado de 50 RPS (ADR-0012). Validação produtiva permanece pendente. |
| Health/readiness/liveness das APIs HTTP | Implementado | `Ledger.Api` e `Consolidation.Api` expõem `GET /health/live` e `GET /health/ready`; readiness valida o PostgreSQL da respectiva API e retorna 503 quando indisponível. |
| Rate limiting básico das APIs HTTP | Implementado localmente | `POST /entries` e `GET /daily-balances/{businessDate}` usam rate limiting local/in-memory, retornam 429 no padrão de erro da API e preservam `correlationId` quando informado. Endpoints de health não aplicam rate limit. Rate limiting distribuído/produtivo permanece pendente. |
| Instrumentação OpenTelemetry | Implementada como baseline local | As quatro unidades implantáveis usam `ILogger`, `ActivitySource`, `Meter` e OTLP exporter configurável; `docker-compose.yml` inclui Aspire Dashboard para demonstração local. |
| Runbook final de demonstração local | Documentado | `docs/operations/runbook-demonstracao-local.md` consolida pré-requisitos, subida, health, fluxo end-to-end, idempotência, DLQ/retry, observabilidade, testes e limpeza local. |
| Evidências finais do case | Documentado | `docs/operations/evidencias-do-case.md` mapeia requisitos do desafio contra evidências do repositório, status e limitações sem afirmar prontidão produtiva. |
| DLQ e retry do Consolidado | Implementado localmente e documentado | `Consolidation.Worker` mantém mensagens inválidas ou com falha na fila para redelivery do SQS; a redrive policy envia mensagens excedidas para DLQ. Reprocessamento assistido permanece pendente. |
| Observabilidade operacional completa | Pendente | Plataforma produtiva, dashboards produtivos, alertas, retenção centralizada, evidências operacionais completas e sinais aprofundados de workers, Outbox e broker ainda não estão prontos. |
| Borda HTTPS/WAF | Implementada localmente | `edge-proxy` real (HTTPS, ModSecurity/OWASP CRS) na frente de `Ledger.Api` e `Consolidation.Api`, que não publicam porta própria (ADR-0008). |
| Menor privilégio PostgreSQL | Implementada localmente | Um role PostgreSQL por componente de runtime (owner separado do runtime), incluindo role somente leitura para `Consolidation.Api` (ADR-0009). |
| Secrets Manager/SSM/KMS/IAM | Implementada localmente via LocalStack | Um secret por componente, SSM como fonte autoritativa de issuer/audience (leitura única no startup, sem polling), KMS com round-trip comprovado, IAM provisionado como referência (enforcement não comprovável no LocalStack Hobby). Rotação de credencial de banco comprovada ponta a ponta por execução real (ADR-0009). |
| Isolamento de disponibilidade Ledger/Consolidation | Implementado e testado | `tests/Consolidation.IntegrationTests/SystemFlowIntegrationTests.cs::Consolidation_indisponivel_nao_bloqueia_Ledger_e_converge_apos_recuperacao_sem_duplicar_saldo` prova que a indisponibilidade do processamento do Consolidado não bloqueia o Ledger e que a projeção converge sem reparo manual nem duplicidade após a recuperação. |
| Diagramas C4 Component | Implementado | [06-diagramas.md](architecture/06-diagramas.md), seções 10-13, cobrindo as quatro unidades implantáveis com componentes mapeados ao código real. |
| Priorização formal (MoSCoW/escopo) | Documentado | [09-escopo-priorizacao-e-limites.md](architecture/09-escopo-priorizacao-e-limites.md) consolida MoSCoW, escopo, riscos, premissas, dependências e adiamentos deliberados. |

## Pendências principais

```text
- validação de capacidade em ambiente produtivo ou equivalente declarado
- rate limiting distribuído/produtivo em API Gateway, WAF, ingress ou service mesh
- observabilidade produtiva
- dashboards produtivos, alertas produtivos e retenção centralizada de logs
- backoff avançado e operação produtiva de mensagens isoladas
- hardening produtivo de autenticação/autorização
- reconstrução/reprocessamento operacional completo
- re-drive assistido da DLQ
- remoção da compatibilidade transitória com EntryCreated.v1
- validação produtiva de múltiplos workers, backlog e autoscaling para Consolidation.Worker
- deploy produtivo/IaC
- publicação de imagens no ECR
- Terraform plan/apply em ambiente AWS
- smoke tests pós-deploy AWS
```
