# Teste de carga do Consolidado

Este documento descreve o teste reproduzível para validar o endpoint `GET /daily-balances/{businessDate}` em carga sustentada de 50 RPS.

O teste é uma evidência local/container-first. Ele não substitui validação produtiva, teste em infraestrutura dimensionada, observabilidade produtiva completa ou análise de capacidade em ambiente real.

## O que o teste cobre

```text
- preparação de dataset no Consolidation Database
- múltiplos merchants e businessDates com projeções DailyBalance existentes
- tokens JWT locais com merchant_id, issuer, audience e expiração válidos
- requisições autenticadas para GET /daily-balances/{businessDate}
- rampa inicial configurável
- carga sustentada configurável, com padrão de 50 RPS por 60 segundos
- medição de total planejado, total executado, sucesso, falha, p95, p99, throughput observado e throughput mínimo esperado
```

O dataset é preparado antes da execução para evitar que `404 Not Found` seja tratado como falso negativo.

## Pré-requisitos

Subir a solução local completa:

```powershell
docker compose up -d --build ledger-api ledger-outbox-publisher consolidation-worker consolidation-api
```

Também é possível subir somente a infraestrutura e aplicar migrations explicitamente:

```powershell
docker compose up -d ledger-postgres consolidation-postgres rabbitmq
docker compose run --rm consolidation-migrations
```

Em outro terminal, executar o teste de carga:

```powershell
docker compose run --rm dotnet-sdk dotnet run --project tests/Consolidation.LoadTests
```

## Configuração

Variáveis de ambiente suportadas:

| Variável | Padrão | Descrição |
|---|---:|---|
| `CONSOLIDATION_API_BASE_URL` | `http://host.docker.internal:8081` | URL base da `Consolidation.Api` vista a partir do container do teste. |
| `CONSOLIDATION_CONNECTION_STRING` | `Host=consolidation-postgres;Port=5432;Database=consolidation;Username=consolidation;Password=consolidation` | Connection string usada para preparar o dataset. |
| `CONSOLIDATION_AUTH_SIGNING_KEY` | `ledger-local-development-signing-key-32-bytes` | Chave local usada para assinar os tokens JWT do teste. |
| `CONSOLIDATION_AUTH_ISSUER` | `banco-carrefour-local` | Issuer local incluído no JWT do teste. |
| `CONSOLIDATION_AUTH_AUDIENCE` | `banco-carrefour-api` | Audience local incluído no JWT do teste. |
| `LOADTEST_MERCHANTS` | `20` | Quantidade de merchants no dataset. |
| `LOADTEST_BUSINESS_DATES` | `5` | Quantidade de datas por merchant. |
| `LOADTEST_BASE_BUSINESS_DATE` | `2026-07-01` | Primeira data de negócio do dataset. |
| `LOADTEST_RPS` | `50` | Carga sustentada alvo. |
| `LOADTEST_MIN_OBSERVED_RPS` | `50` | Throughput mínimo observado exigido na janela sustentada. |
| `LOADTEST_RAMP_SECONDS` | `30` | Duração da rampa inicial. |
| `LOADTEST_DURATION_SECONDS` | `60` | Duração da janela sustentada. |
| `LOADTEST_REQUEST_TIMEOUT_SECONDS` | `5` | Timeout por requisição. |
| `LOADTEST_MAX_FAILURE_RATE` | `0.05` | Taxa máxima de falha elegível. |
| `LOADTEST_MAX_P95_MS` | `500` | Limite de p95 em milissegundos. |
| `LOADTEST_MAX_P99_MS` | `1000` | Limite de p99 em milissegundos. |

## Critérios de sucesso

Na janela sustentada:

```text
- falhas elegíveis <= 5%
- throughput observado >= 50 req/s por padrão, ou valor configurado em `LOADTEST_MIN_OBSERVED_RPS`
- p95 <= 500 ms
- p99 <= 1000 ms
```

O processo retorna código `0` quando esses critérios são atendidos e código não zero quando falham.

## Status

O teste de carga foi criado e executado localmente/container-first contra a `Consolidation.Api` em `http://host.docker.internal:8081`.

Essa execução é evidência local/container-first. Ela não prova prontidão produtiva, não substitui teste em infraestrutura dimensionada e não indica que observabilidade produtiva completa, operação produtiva completa de DLQ, health/sinais operacionais aprofundados dos workers ou deploy produtivo estejam prontos.

## Evidência local/container-first

Comando usado:

```powershell
docker compose run --rm dotnet-sdk dotnet run --project tests/Consolidation.LoadTests
```

Pré-condição:

```text
Consolidation.Api rodando em http://host.docker.internal:8081
```

Dataset:

```text
- 20 merchants
- 5 datas por merchant
- projeções DailyBalance existentes para todas as consultas do teste
```

Perfil de carga:

```text
- rampa: 30s
- carga sustentada: 60s a 50 RPS
```

Resultado total:

```text
- total planejado: 3785
- total executado: 3785
- sucessos: 3785
- falhas: 0
- taxa de sucesso: 100.00%
- taxa de falha: 0.00%
- p95: 6.81 ms
- p99: 9.08 ms
- throughput observado: 42.06 req/s
```

Resultado da janela sustentada:

```text
- total planejado: 3000
- total executado: 3000
- sucessos: 3000
- falhas: 0
- taxa de sucesso: 100.00%
- taxa de falha: 0.00%
- p95: 6.49 ms
- p99: 8.68 ms
- throughput observado: 50.02 req/s
- throughput mínimo: 50.00 req/s
```

Critérios esperados:

```text
- falhas elegíveis <= 5.00%
- throughput observado >= 50.00 req/s
- p95 <= 500 ms
- p99 <= 1000 ms
```

Resultado:

```text
critérios atendidos na janela sustentada da execução local/container-first
```
