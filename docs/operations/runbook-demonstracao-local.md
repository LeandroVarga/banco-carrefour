# Runbook de Demonstracao Local

Este runbook orienta a demonstracao local/container-first da solucao para avaliacao do desafio tecnico.

Ele cobre subida da solucao, health checks, fluxo end-to-end, idempotencia, inspecao operacional de DLQ/retry, observabilidade local e testes automatizados.

Este documento nao representa runbook produtivo completo. A implantacao AWS de referencia esta descrita em `runbook-implantacao-aws.md`.

## 1. Pre-requisitos

Obrigatorios:

```text
- Git
- Docker com Docker Compose
- um shell local: PowerShell, Bash ou Zsh
```

Opcional:

```text
- GitHub CLI, somente para inspecao local de PRs/workflows quando aplicavel
```

Nao e necessario instalar .NET SDK localmente, PowerShell 7, Python, Node, OpenSSL ou ferramentas externas para JWT. O caminho oficial de build, testes, execucao e geracao de token local e container-first via Docker Compose.

Os servicos rodam em containers. Windows, Linux e macOS sao suportados desde que Docker e Docker Compose estejam disponiveis. A compatibilidade com Linux/macOS e esperada pelo modelo container-first; este documento nao afirma validacao executada nesses sistemas.

## 2. Subida da solucao

Subir dependencias principais:

```powershell
docker compose up -d ledger-postgres consolidation-postgres rabbitmq
```

Aplicar migrations explicitamente:

```powershell
docker compose run --rm ledger-migrations
docker compose run --rm consolidation-migrations
```

Subir a solucao local completa:

```powershell
docker compose up -d --build ledger-api ledger-outbox-publisher consolidation-worker consolidation-api aspire-dashboard
```

## 3. URLs locais

| Servico | URL |
|---|---|
| Ledger.Api | `http://localhost:8080` |
| Consolidation.Api | `http://localhost:8081` |
| RabbitMQ Management | `http://localhost:15672` |
| Aspire Dashboard | `http://localhost:18888` |

Credenciais locais do RabbitMQ Management:

```text
usuario: ledger
senha: ledger
```

O Aspire Dashboard e usado somente como visualizacao local/dev para telemetria OpenTelemetry.

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

Interpretacao:

```text
- /health/live indica que o processo HTTP responde.
- /health/ready valida a dependencia PostgreSQL minima da respectiva API.
- Workers nao expõem endpoint HTTP neste incremento.
```

## 5. Fluxo end-to-end

Gerar token local para o comerciante de demonstracao:

Windows/PowerShell:

```powershell
$token = docker compose run --rm local-jwt --merchant-id merchant-001
```

Linux/macOS:

```bash
token=$(docker compose run --rm local-jwt --merchant-id merchant-001)
```

Opcionalmente, a expiracao pode ser definida em minutos:

```bash
docker compose run --rm local-jwt --merchant-id merchant-001 --expires-in-minutes 120
```

O helper emite `iss` e `aud` locais compativeis com as APIs por padrao. Para testar outro emissor ou audiencia, use `--issuer` e `--audience`.

O script `scripts/generate-local-jwt.ps1` permanece disponivel por compatibilidade para usuarios de PowerShell, mas nao e requisito para a demonstracao container-first.

Registrar um lancamento no Ledger.

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
- o evento EntryCreated.v1 e persistido na Outbox e publicado pelo Ledger.OutboxPublisher.
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
- HTTP 200 OK quando a projecao DailyBalance ja foi materializada.
- merchantId: merchant-001.
- totalCredits: 150.75.
- balance: 150.75, se nao houver outros lancamentos para o mesmo comerciante e data.
```

`404 Not Found` em `GET /daily-balances/{businessDate}` significa ausencia de projecao disponivel para o comerciante e data. Nao confirma saldo zero.

## 6. Validacao de idempotencia

Repetir a mesma requisicao com a mesma `Idempotency-Key` e o mesmo payload:

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
- HTTP 200 OK para repeticao idempotente equivalente.
- resposta equivalente ao registro original.
- nenhum novo efeito financeiro duplicado deve ser produzido.
```

Payload divergente com a mesma `Idempotency-Key` deve retornar:

```text
HTTP 409 Conflict
```

## 7. Validacao operacional de DLQ

O `Consolidation.Worker` isola mensagens irrecuperaveis em DLQ local.

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
- JSON invalido e enviado para DLQ.
- evento EntryCreated.v1 semanticamente invalido e enviado para DLQ.
- mensagem isolada e confirmada com ack para nao bloquear todo o consumo.
```

Como inspecionar localmente:

```text
1. abrir http://localhost:15672
2. autenticar com ledger / ledger
3. acessar Queues
4. procurar consolidation.entry-created.dlq
5. inspecionar quantidade de mensagens, headers e payload quando houver mensagens isoladas
```

Reprocessamento assistido da DLQ ainda e pendente. Este runbook nao define procedimento produtivo completo de correcao e replay de mensagens isoladas.

## 8. Validacao operacional de retry

Erros desconhecidos ou transitorios no `Consolidation.Worker` usam retry local finito.

Topologia relevante:

| Finalidade | Nome |
|---|---|
| Retry exchange | `consolidation.retry` |
| Retry queue | `consolidation.entry-created.retry` |
| Retry routing key | `consolidation.entry-created.retry` |
| Header de controle | `x-retry-count` |

Comportamento documentado e coberto por testes automatizados:

```text
- falha desconhecida/transitoria publica a mensagem na fila de retry.
- x-retry-count controla a quantidade de tentativas.
- o TTL da fila de retry devolve a mensagem para ledger.events.
- apos RabbitMq__MaxRetryAttempts, a mensagem vai para DLQ.
```

Nao ha procedimento manual simples e robusto neste runbook para forcar retry sem fragilizar a demonstracao. A validacao operacional recomendada para avaliacao e por testes automatizados e inspecao da topologia no RabbitMQ Management.

## 9. Observabilidade local

A solucao possui baseline local de OpenTelemetry nas quatro unidades implantaveis:

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
- metricas customizadas via Meter
- exportacao OTLP quando OTEL_EXPORTER_OTLP_ENDPOINT esta configurado
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
- criacao de lancamento no Ledger.Api
- publicacao de evento pelo Ledger.OutboxPublisher
- consumo do evento pelo Consolidation.Worker
- atualizacao de DailyBalance
- retry de mensagem, quando houver falha transitoria
- envio para DLQ, quando houver mensagem irrecuperavel
- consulta do consolidado na Consolidation.Api
- correlationId comum entre as etapas quando informado
```

A validacao visual do Aspire Dashboard pode depender do ambiente local e do navegador. A presenca do servico no Compose e da configuracao OTLP demonstra o caminho local/dev, mas nao substitui plataforma produtiva de observabilidade, dashboards produtivos, alertas ou retencao centralizada.

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

O teste de carga nao faz parte do `dotnet test` padrao e nao deve ser declarado como executado no CI sem evidencia especifica.

## 11. Limpeza local

Parar e remover containers da solucao:

```powershell
docker compose down
```

Parar e remover containers e volumes locais:

```powershell
docker compose down -v
```

`docker compose down -v` remove os volumes de dados locais dos bancos e do broker. Use esse comando apenas quando a perda dos dados locais de demonstracao for aceitavel.

## 12. Limites preservados

Este runbook demonstra a execucao local do case, mas nao afirma:

```text
- prontidao produtiva completa
- validacao de capacidade em producao
- observabilidade produtiva completa
- dashboards ou alertas produtivos
- reprocessamento operacional completo da DLQ
- deploy/IaC produtivo
- health/readiness/liveness HTTP dos workers
```
