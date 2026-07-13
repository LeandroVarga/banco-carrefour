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

Subir dependências principais:

```powershell
docker compose up -d ledger-postgres consolidation-postgres rabbitmq
```

Aplicar migrations explicitamente:

```powershell
docker compose run --rm ledger-migrations
docker compose run --rm consolidation-migrations
```

Subir a solução local completa:

```powershell
docker compose up -d --build ledger-api ledger-outbox-publisher consolidation-worker consolidation-api aspire-dashboard
```

## 3. URLs locais

| Serviço | URL |
|---|---|
| Ledger.Api | `http://localhost:8080` |
| Consolidation.Api | `http://localhost:8081` |
| RabbitMQ Management | `http://localhost:15672` |
| Aspire Dashboard | `http://localhost:18888` |

Credenciais locais do RabbitMQ Management:

```text
usuário: ledger
senha: ledger
```

O Aspire Dashboard é usado somente como visualização local/dev para telemetria OpenTelemetry.

## 4. Health checks

Validar liveness e readiness das APIs HTTP:

Windows/PowerShell:

```powershell
curl.exe http://localhost:8080/health/live
curl.exe http://localhost:8080/health/ready
curl.exe http://localhost:8081/health/live
curl.exe http://localhost:8081/health/ready
```

Linux/macOS:

```bash
curl http://localhost:8080/health/live
curl http://localhost:8080/health/ready
curl http://localhost:8081/health/live
curl http://localhost:8081/health/ready
```

Interpretação:

```text
- /health/live indica que o processo HTTP responde.
- /health/ready valida a dependência PostgreSQL mínima da respectiva API.
- Workers não expõem endpoint HTTP neste incremento.
```

## 5. Fluxo end-to-end

Gerar token local para o comerciante de demonstração pelo serviço `local-jwt` via Docker Compose:

Windows/PowerShell:

```powershell
$token = docker compose run --rm local-jwt --merchant-id merchant-001
```

Linux/macOS:

```bash
token=$(docker compose run --rm local-jwt --merchant-id merchant-001)
```

Opcionalmente, a expiração pode ser definida em minutos:

```bash
docker compose run --rm local-jwt --merchant-id merchant-001 --expires-in-minutes 120
```

O helper emite `iss` e `aud` locais compatíveis com as APIs por padrão. Para testar outro emissor ou audiência, use `--issuer` e `--audience`.

Registrar um lançamento no Ledger.

Windows/PowerShell:

```powershell
$body = @{
  type = "CREDIT"
  amount = "150.75"
  currency = "BRL"
  occurredAt = "2026-07-12T13:45:00Z"
  description = "Venda local"
} | ConvertTo-Json -Compress

curl.exe -i -X POST http://localhost:8080/entries `
  -H "Authorization: Bearer $token" `
  -H "Idempotency-Key: idem-demo-001" `
  -H "X-Correlation-Id: corr-demo-001" `
  -H "Content-Type: application/json" `
  --data $body
```

Linux/macOS:

```bash
curl -i -X POST http://localhost:8080/entries \
  -H "Authorization: Bearer $token" \
  -H "Idempotency-Key: idem-demo-001" \
  -H "X-Correlation-Id: corr-demo-001" \
  -H "Content-Type: application/json" \
  --data '{"type":"CREDIT","amount":"150.75","currency":"BRL","occurredAt":"2026-07-12T13:45:00Z","description":"Venda local"}'
```

Resultado esperado:

```text
- HTTP 201 Created na primeira requisicao valida.
- businessDate esperado: 2026-07-12.
- o evento EntryCreated.v1 é persistido na Outbox e publicado pelo Ledger.OutboxPublisher.
- o Consolidation.Worker consome o evento e atualiza DailyBalance.
```

Aguardar o processamento assincrono:

Windows/PowerShell:

```powershell
Start-Sleep -Seconds 5
```

Linux/macOS:

```bash
sleep 5
```

Consultar o consolidado diario:

Windows/PowerShell:

```powershell
curl.exe -i http://localhost:8081/daily-balances/2026-07-12 `
  -H "Authorization: Bearer $token" `
  -H "X-Correlation-Id: corr-demo-001"
```

Linux/macOS:

```bash
curl -i http://localhost:8081/daily-balances/2026-07-12 \
  -H "Authorization: Bearer $token" \
  -H "X-Correlation-Id: corr-demo-001"
```

Resultado esperado:

```text
- HTTP 200 OK quando a projeção DailyBalance já foi materializada.
- merchantId: merchant-001.
- totalCredits: 150.75.
- balance: 150.75, se não houver outros lançamentos para o mesmo comerciante e data.
```

`404 Not Found` em `GET /daily-balances/{businessDate}` significa ausência de projeção disponível para o comerciante e data. Não confirma saldo zero.

## 6. Validação de idempotência

Repetir a mesma requisição com a mesma `Idempotency-Key` e o mesmo payload:

Windows/PowerShell:

```powershell
curl.exe -i -X POST http://localhost:8080/entries `
  -H "Authorization: Bearer $token" `
  -H "Idempotency-Key: idem-demo-001" `
  -H "X-Correlation-Id: corr-demo-001-replay" `
  -H "Content-Type: application/json" `
  --data $body
```

Linux/macOS:

```bash
curl -i -X POST http://localhost:8080/entries \
  -H "Authorization: Bearer $token" \
  -H "Idempotency-Key: idem-demo-001" \
  -H "X-Correlation-Id: corr-demo-001-replay" \
  -H "Content-Type: application/json" \
  --data '{"type":"CREDIT","amount":"150.75","currency":"BRL","occurredAt":"2026-07-12T13:45:00Z","description":"Venda local"}'
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

O `Consolidation.Worker` isola mensagens irrecuperáveis em DLQ local.

Topologia relevante:

| Finalidade | Nome |
|---|---|
| Exchange principal | `ledger.events` |
| Fila principal | `consolidation.entry-created` |
| Dead-letter exchange | `consolidation.dlx` |
| Dead-letter queue | `consolidation.entry-created.dlq` |
| Routing key da DLQ | `consolidation.entry-created.dead` |

Comportamento documentado e coberto por testes automatizados:

```text
- JSON inválido é enviado para DLQ.
- evento EntryCreated.v1 semanticamente inválido é enviado para DLQ.
- mensagem isolada é confirmada com ack para não bloquear todo o consumo.
```

Como inspecionar localmente:

```text
1. abrir http://localhost:15672
2. autenticar com ledger / ledger
3. acessar Queues
4. procurar consolidation.entry-created.dlq
5. inspecionar quantidade de mensagens, headers e payload quando houver mensagens isoladas
```

Reprocessamento assistido da DLQ ainda é pendente. Este runbook não define procedimento produtivo completo de correção e replay de mensagens isoladas.

## 8. Validação operacional de retry

Erros desconhecidos ou transitórios no `Consolidation.Worker` usam retry local finito.

Topologia relevante:

| Finalidade | Nome |
|---|---|
| Retry exchange | `consolidation.retry` |
| Retry queue | `consolidation.entry-created.retry` |
| Retry routing key | `consolidation.entry-created.retry` |
| Header de controle | `x-retry-count` |

Comportamento documentado e coberto por testes automatizados:

```text
- falha desconhecida/transitória publica a mensagem na fila de retry.
- x-retry-count controla a quantidade de tentativas.
- o TTL da fila de retry devolve a mensagem para ledger.events.
- após RabbitMq__MaxRetryAttempts, a mensagem vai para DLQ.
```

Não há procedimento manual simples e robusto neste runbook para forçar retry sem fragilizar a demonstração. A validação operacional recomendada para avaliação é por testes automatizados e inspeção da topologia no RabbitMQ Management.

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
- retry de mensagem, quando houver falha transitoria
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
docker compose run --rm dotnet-sdk dotnet test
```

Teste de carga local/container-first do Consolidado:

```powershell
docker compose run --rm dotnet-sdk dotnet run --project tests/Consolidation.LoadTests
```

O teste de carga não faz parte do `dotnet test` padrão e não deve ser declarado como executado no CI sem evidência específica.

## 11. Limpeza local

Parar e remover containers da solução:

```powershell
docker compose down
```

Parar e remover containers e volumes locais:

```powershell
docker compose down -v
```

`docker compose down -v` remove os volumes de dados locais dos bancos e do broker. Use esse comando apenas quando a perda dos dados locais de demonstração for aceitável.

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
