# Runbook de Demonstração Local

Este runbook orienta a demonstração local/container-first da solução para avaliação do desafio técnico.

Ele cobre subida da solução, health checks, fluxo end-to-end, idempotência, inspeção operacional de DLQ/retry, observabilidade local e testes automatizados.

Este documento não representa runbook produtivo completo. A implantação AWS de referência está descrita em `runbook-implantacao-aws.md`.

## 1. Pré-requisitos

Obrigatórios:

```text
- Git
- Docker com Docker Compose
- shell local: PowerShell, Bash ou Zsh
```

Opcional:

```text
- GitHub CLI, somente para inspeção local de PRs/workflows quando aplicável
```

Não é necessário instalar .NET SDK localmente, PowerShell 7, Python, Node, OpenSSL ou ferramentas externas para JWT. O caminho oficial de build, testes, execução e geração de token local é container-first via Docker Compose.

Os serviços rodam em containers. Windows, Linux e macOS são suportados desde que Docker e Docker Compose estejam disponíveis. A compatibilidade com Linux/macOS é esperada pelo modelo container-first; este documento não afirma validação executada nesses sistemas.

## 2. Subida da solução

Subir a solução local completa:

```powershell
docker compose up -d --build ledger-api ledger-outbox-publisher consolidation-worker consolidation-api aspire-dashboard
```

Esse comando sobe PostgreSQL, LocalStack (SQS/Secrets Manager/SSM/KMS/IAM), provisiona fila, secrets, parâmetros, chave KMS e roles IAM via Terraform local (`terraform-provisioner`), grava os valores reais dos secrets via `secret-value-bootstrap` (AWS CLI containerizada, `PutSecretValue` — ver ADR-0009), executa migrations e inicia APIs, workers e dashboard.

A ordem oficial de bootstrap: `bootstrap-local-security` (antes deste `docker compose up`) → `localstack` saudável → `terraform-provisioner` (Secrets Manager/SSM/KMS/IAM/SQS provisionados como metadados/estrutura, sem valores) → `secret-value-bootstrap` (grava as senhas já geradas pelo `bootstrap-local-security` nos secrets) → `ledger-api`/`ledger-outbox-publisher`/`consolidation-api`/`consolidation-worker` (cada um lê, no startup, apenas o secret do seu próprio componente).

`ledger-api`/`consolidation-api` também resolvem issuer/audience OIDC via SSM no startup (fonte autoritativa, ADR-0009) — não há mais variável `Authentication__Authority/Audience` no compose. Sempre use `--build` (como no comando acima) ao reconstruir localmente após alterar código de qualquer um dos 4 componentes: uma imagem desatualizada de `ledger-outbox-publisher`/`consolidation-worker` falha ao autenticar no PostgreSQL com uma mensagem enganosa ("No password has been provided"), já que o binário antigo pode não refletir a integração atual com o Secrets Manager.

**Rotação de credencial**: ver seção 5 (`scripts/security/rotate-database-credential.sh`) para o procedimento reproduzível completo.

## 3. URLs locais

| Serviço | URL |
|---|---|
| Edge (Ledger/Consolidation via edge-proxy HTTPS) | `https://localhost:8443` (`/ledger/...`, `/consolidation/...`) |
| Keycloak (via edge-proxy, vhost `keycloak.localhost`) | `https://keycloak.localhost:8443` |
| LocalStack (SQS/Secrets Manager/SSM/KMS/IAM) | `http://localhost:4566` |
| Aspire Dashboard | `http://localhost:18888` |

`Ledger.Api`/`Consolidation.Api` não publicam porta própria — o único ponto de entrada de negócio é o `edge-proxy` HTTPS (ADR-0007, ADR-0008). O certificado é autoassinado (gerado por `bootstrap-local-security`); use `-k`/`--insecure` no curl para o ambiente local.

## 4. Health checks

Validar liveness e readiness das APIs através da borda real:

```bash
curl -sk https://localhost:8443/ledger/health/live -H "Host: localhost:8443"
curl -sk https://localhost:8443/ledger/health/ready -H "Host: localhost:8443"
curl -sk https://localhost:8443/consolidation/health/live -H "Host: localhost:8443"
curl -sk https://localhost:8443/consolidation/health/ready -H "Host: localhost:8443"
```

Interpretação:

```text
- /health/live indica que o processo HTTP responde.
- /health/ready valida a dependência PostgreSQL mínima da respectiva API.
- Ledger.OutboxPublisher/Consolidation.Worker não expõem endpoint HTTP; use `docker compose logs <serviço>` para inspecionar o ciclo de processamento.
```

## 5. Fluxo end-to-end

Obter um token real do Keycloak local via client credentials (`merchant-a-test-client`, cujo secret é gerado por `bootstrap-local-security`/`keycloak-bootstrap` e persistido em `.local/security/.env.security`, gitignored — nunca imprima esse valor):

```bash
MERCHANT_A_SECRET=$(grep '^MERCHANT_A_TEST_CLIENT_SECRET=' .local/security/.env.security | cut -d= -f2-)

TOKEN=$(curl -fsSk --resolve keycloak.localhost:8443:127.0.0.1 \
  -X POST "https://keycloak.localhost:8443/realms/banco-carrefour/protocol/openid-connect/token" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials" -d "client_id=merchant-a-test-client" \
  -d "client_secret=$MERCHANT_A_SECRET" -d "scope=ledger.write")
```

`--resolve keycloak.localhost:8443:127.0.0.1` substitui uma entrada de hosts file — o edge-proxy roteia por SNI/`Host` (vhost `keycloak.localhost`, ver `infra/edge-proxy/default.conf.template`).

Registrar um lançamento no Ledger:

```bash
curl -sk -i -X POST https://localhost:8443/ledger/entries \
  -H "Host: localhost:8443" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Idempotency-Key: idem-demo-001" \
  -H "X-Correlation-Id: corr-demo-001" \
  -H "Content-Type: application/json" \
  --data '{"type":"CREDIT","amount":"150.75","currency":"BRL","occurredAt":"2026-07-26T13:45:00Z","description":"Venda local"}'
```

Resultado esperado:

```text
- HTTP 201 Created na primeira requisicao valida.
- merchantId no corpo da resposta: merchant-a (derivado do token, mapper merchant-id-hardcoded do client de teste).
- businessDate calculado em America/Sao_Paulo a partir de occurredAt.
- o evento FinancialEntryRegistered.v1 é persistido na Outbox e publicado pelo Ledger.OutboxPublisher.
- o Consolidation.Worker consome o evento e atualiza DailyBalance.
```

Aguardar o processamento assíncrono (Outbox → SQS → Worker → Projeção):

```bash
sleep 15
```

Obter um token com escopo de leitura e consultar o consolidado diário:

```bash
TOKEN_READ=$(curl -fsSk --resolve keycloak.localhost:8443:127.0.0.1 \
  -X POST "https://keycloak.localhost:8443/realms/banco-carrefour/protocol/openid-connect/token" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials" -d "client_id=merchant-a-test-client" \
  -d "client_secret=$MERCHANT_A_SECRET" -d "scope=consolidation.read")

curl -sk -i "https://localhost:8443/consolidation/daily-balances/$(date -u +%Y-%m-%d)" \
  -H "Host: localhost:8443" \
  -H "Authorization: Bearer $TOKEN_READ" \
  -H "X-Correlation-Id: corr-demo-001"
```

Resultado esperado:

```text
- HTTP 200 OK quando a projeção DailyBalance já foi materializada.
- merchantId: merchant-a.
- totalCredits: 150.75 (mais qualquer outro lançamento já existente para o mesmo comerciante/data nesta execução).
```

**Rotação de credencial de banco (ADR-0009)**: use `sh scripts/security/rotate-database-credential.sh <componente>` (`ledger-api`, `ledger-outbox-publisher`, `consolidation-api` ou `consolidation-worker`) — script revisado que executa `ALTER ROLE` → confirma a senha antiga rejeitada pela rede real → atualiza o secret via `secret-value-bootstrap-impl.sh` real → reinicia somente o componente informado → confirma a credencial nova aceita → confirma ausência da senha nos logs. Nunca imprime valor de senha. Repita o fluxo end-to-end acima depois da rotação para confirmar que o componente segue funcional.

**Restart e SSM (ADR-0009)**: alterar um parâmetro do SSM (`/banco-carrefour/oidc/issuer`, `/banco-carrefour/oidc/ledger-audience`, `/banco-carrefour/oidc/consolidation-audience`) não afeta uma instância já em execução — só um `docker compose restart <ledger-api|consolidation-api>` carrega o novo valor. Prova automatizada real dessa semântica: `tests/Security.IntegrationTests/Edge/AuthenticatedEdgeFlowTests.cs::Mudanca_de_audience_no_SSM_nao_afeta_processo_em_execucao_e_exige_restart_real`.

`404 Not Found` em `GET /daily-balances/{businessDate}` significa ausência de projeção disponível para o comerciante e data. Não confirma saldo zero.

## 6. Validação de idempotência

Repetir a mesma requisição com a mesma `Idempotency-Key` e o mesmo payload (reaproveitando `$TOKEN` obtido na seção 5):

```bash
curl -sk -i -X POST https://localhost:8443/ledger/entries \
  -H "Host: localhost:8443" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Idempotency-Key: idem-demo-001" \
  -H "X-Correlation-Id: corr-demo-001-replay" \
  -H "Content-Type: application/json" \
  --data '{"type":"CREDIT","amount":"150.75","currency":"BRL","occurredAt":"2026-07-26T13:45:00Z","description":"Venda local"}'
```

Resultado esperado:

```text
- HTTP 200 OK para repetição idempotente equivalente.
- resposta equivalente ao registro original.
- nenhum novo efeito financeiro duplicado deve ser produzido.
```

Payload divergente com a mesma `Idempotency-Key` deve retornar:

```text
HTTP 409 Conflict
```

## 7. Validação operacional de DLQ

O `Consolidation.Worker` usa SQS Standard no LocalStack. Mensagens inválidas ou com falha persistente não são excluídas pelo worker e seguem para DLQ pela redrive policy da fila.

Topologia relevante:

| Finalidade | Nome |
|---|---|
| Fila principal | `financial-entry-registered` |
| Dead-letter queue | `financial-entry-registered-dlq` |
| Visibility timeout local | `5` segundos |
| Redrive policy local | `3` tentativas |

Comportamento documentado e coberto por testes automatizados:

```text
- JSON inválido não é excluído da fila pelo worker.
- evento FinancialEntryRegistered.v1 semanticamente inválido não é excluído da fila pelo worker.
- a DLQ é controlada pela política de redrive do SQS no LocalStack.
```

Reprocessamento assistido da DLQ ainda é pendente. Este runbook não define procedimento produtivo completo de correção e replay de mensagens isoladas.

## 8. Validação operacional de retry

Erros desconhecidos ou transitórios no `Consolidation.Worker` usam redelivery do SQS conforme visibility timeout.

Topologia relevante:

| Finalidade | Nome |
|---|---|
| Controle de nova entrega | Visibility timeout da fila SQS |
| Contador aproximado | `ApproximateReceiveCount` |
| Isolamento final | `financial-entry-registered-dlq` |

Comportamento documentado e coberto por testes automatizados:

```text
- falha desconhecida/transitória mantém a mensagem na fila.
- o SQS libera a mensagem novamente após o visibility timeout.
- após o limite da redrive policy, a mensagem vai para DLQ.
```

Não há procedimento manual simples e robusto neste runbook para forçar retry sem fragilizar a demonstração. A validação operacional recomendada para avaliação é por testes automatizados e inspeção dos logs do `consolidation-worker`.

## 9. Observabilidade local

A solução possui baseline local de OpenTelemetry nas quatro unidades implantáveis:

```text
- BancoCarrefour.Ledger.Api
- BancoCarrefour.Ledger.OutboxPublisher
- BancoCarrefour.Consolidation.Worker
- BancoCarrefour.Consolidation.Api
```

Instrumentacao:

```text
- logs estruturados via ILogger
- traces customizados via ActivitySource
- métricas customizadas via Meter
- exportação OTLP quando OTEL_EXPORTER_OTLP_ENDPOINT está configurado
- Aspire Dashboard local/dev no Docker Compose
```

Abrir a UI local:

```text
http://localhost:18888
```

Comandos para acompanhar logs:

```powershell
docker compose logs -f ledger-api
docker compose logs -f ledger-outbox-publisher
docker compose logs -f consolidation-worker
docker compose logs -f consolidation-api
```

O que procurar nos logs:

```text
- criação de lançamento no Ledger.Api
- publicação de evento pelo Ledger.OutboxPublisher
- consumo do evento pelo Consolidation.Worker
- atualizacao de DailyBalance
- redelivery de mensagem, quando houver falha transitoria
- envio para DLQ, quando houver mensagem irrecuperavel
- consulta do consolidado na Consolidation.Api
- correlationId comum entre as etapas quando informado
```

A validação visual do Aspire Dashboard pode depender do ambiente local e do navegador. A presença do serviço no Compose e da configuração OTLP demonstra o caminho local/dev, mas não substitui plataforma produtiva de observabilidade, dashboards produtivos, alertas ou retenção centralizada.

## 10. Testes automatizados

Build container-first:

```powershell
docker compose run --rm dotnet-sdk dotnet build
```

Testes automatizados:

```powershell
docker compose run --rm --no-deps -v /var/run/docker.sock:/var/run/docker.sock -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal dotnet-sdk dotnet test
```

Os testes de integração provisionam PostgreSQL e LocalStack/SQS efêmeros com Testcontainers. Não é necessário executar `docker compose up` antes dos testes. O Docker Compose continua sendo o caminho da demonstração local completa.

Smoke de desempenho (50 RPS) local/container-first do Consolidado — roda também em CI (`performance-smoke-gate`, ver ADR-0012), autenticado por credenciais reais do Keycloak (nunca um bypass local):

```bash
sh scripts/ci/run-performance-smoke.sh
```

Não faz parte do `dotnet test` padrão (é um executável dedicado, `tests/Consolidation.LoadTests`), mas tem execução automatizada própria em CI, com critério de aprovação específico (50 RPS agendados, falhas elegíveis <= 5%, sem trava de latência) — ver `docs/operations/teste-de-carga-consolidado.md` para a distinção entre essa execução de smoke e a evidência completa/manual (com limites de p95/p99).

## 11. Limpeza local

Parar e remover containers da solução:

```powershell
docker compose down
```

Parar e remover containers e volumes locais:

```powershell
docker compose down -v
```

`docker compose down -v` remove os volumes de dados locais dos bancos. Use esse comando apenas quando a perda dos dados locais de demonstração for aceitável.

## 12. Limites preservados

Este runbook demonstra a execução local do case, mas não afirma:

```text
- prontidão produtiva completa
- validação de capacidade em produção
- observabilidade produtiva
- dashboards ou alertas produtivos
- reprocessamento operacional completo da DLQ
- deploy/IaC produtivo
- health/readiness/liveness HTTP dos workers
```
