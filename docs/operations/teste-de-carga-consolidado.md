# Teste de carga do Consolidado

Este documento descreve o teste reproduzível para validar o endpoint `GET /daily-balances/{businessDate}` em carga sustentada de 50 RPS.

O teste é uma evidência local/container-first. Ele não substitui validação produtiva, teste em infraestrutura dimensionada, observabilidade produtiva ou análise de capacidade em ambiente real.

Este mesmo teste tem dois usos distintos, com critérios diferentes (ver ADR-0012):

- **evidência completa/manual** (esta página): rampa mais longa, inclui limites de p95/p99 além da taxa de falha;
- **smoke de CI** (`scripts/ci/run-performance-smoke.sh`, job `performance-smoke-gate`): execução mais curta, critério de aprovação restrito a RPS agendado + falhas elegíveis <= 5% (sem trava de latência — CI compartilhado tem ruído de latência que não deve reprovar a RNF de taxa de falha).

## O que o teste cobre

```text
- preparação de dataset no Consolidation Database, para os merchants reais disponíveis (merchant-a e/ou merchant-b)
- tokens reais do Keycloak via client-credentials (merchant-a-test-client / merchant-b-test-client, escopo consolidation.read) - nunca um bypass HS256/local, conforme OIDC/RS256 (ADR-0007)
- requisições autenticadas para GET /daily-balances/{businessDate}, direto no Consolidation.Api (rede interna do Compose, não pelo edge-proxy - ver "Alvo: serviço direto versus borda" abaixo)
- rampa inicial configurável
- carga sustentada configurável, com padrão de 50 RPS por 60 segundos
- medição de total planejado, total executado, sucesso, falha, timeouts, p50, p95, p99, throughput observado e throughput mínimo esperado
- validação explícita de que o total executado na janela sustentada é igual ao total planejado
- evidência opcional legível por máquina (JSON) e por humano (Markdown) via LOADTEST_RESULT_JSON_PATH / LOADTEST_RESULT_MARKDOWN_PATH
```

O dataset é preparado antes da execução para evitar que `404 Not Found` seja tratado como falso negativo.

## Alvo: serviço direto versus borda

O teste chama `Consolidation.Api` diretamente pelo alias de rede interno do Compose (`http://consolidation-api:8080`), não pelo `edge-proxy` (`https://localhost:8443/consolidation/...`).

Isso é deliberado: a RNF de 50 RPS mede a **capacidade do serviço**, não a política de rate limiting do WAF na borda (`limit_req_zone ... rate=20r/s` em `infra/edge-proxy/default.conf.template`, muito abaixo de 50 RPS). Medir pela borda reprovaria o teste por um controle de segurança que já é coberto separadamente por `tests/Security.IntegrationTests/Edge/WafTests.cs`, não por falta de capacidade do `Consolidation.Api`. Ver ADR-0012 para o trade-off completo.

O Keycloak, porém, ainda precisa ser alcançável pelo hostname `keycloak.localhost` usado no `Authority` do `Consolidation.Api` (resolvido via o alias de rede do próprio `edge-proxy`) — por isso o `edge-proxy` continua fazendo parte da stack subida, mesmo que o tráfego de carga não passe por ele.

## Pré-requisitos

Gerar o material de segurança local (uma vez, ou sempre que precisar de secrets novos):

```bash
sh scripts/security/bootstrap-local-security.sh
```

Subir a solução local completa (o `edge-proxy` precisa estar de pé para a resolução de `keycloak.localhost`, mesmo que o tráfego de carga não passe por ele):

```bash
docker compose up -d --build ledger-api ledger-outbox-publisher consolidation-worker consolidation-api edge-proxy
```

Garantir secrets atuais do Keycloak (auto-recupera se o banco do Keycloak tiver sido recriado):

```bash
docker compose up keycloak-bootstrap
```

Em outro terminal, executar o teste de carga (secrets carregados de `.local/security/.env.security`, nunca hardcoded):

```bash
set -a; . ./.local/security/.env.security; set +a
docker compose run --rm --no-deps \
  -e MERCHANT_A_TEST_CLIENT_SECRET="$MERCHANT_A_TEST_CLIENT_SECRET" \
  -e MERCHANT_B_TEST_CLIENT_SECRET="$MERCHANT_B_TEST_CLIENT_SECRET" \
  dotnet-sdk dotnet run --project tests/Consolidation.LoadTests
```

Ou, de forma automatizada (sobe a stack, aguarda prontidão, garante secrets, roda o smoke e limpa ao final — o mesmo script usado pelo gate de CI):

```bash
sh scripts/ci/run-performance-smoke.sh
```

## Configuração

Variáveis de ambiente suportadas:

| Variável | Padrão | Descrição |
|---|---:|---|
| `CONSOLIDATION_API_BASE_URL` | `http://consolidation-api:8080` | URL base da `Consolidation.Api` vista a partir do container do teste (alvo direto, não o edge-proxy). |
| `CONSOLIDATION_CONNECTION_STRING` | `Host=consolidation-postgres;Port=5432;Database=consolidation;Username=consolidation;Password=consolidation` | Connection string usada para preparar o dataset. |
| `LOADTEST_KEYCLOAK_TOKEN_URL` | `http://keycloak:8080/realms/banco-carrefour/protocol/openid-connect/token` | Endpoint de token do Keycloak real (rede interna do Compose). |
| `LOADTEST_KEYCLOAK_SCOPE` | `consolidation.read` | Escopo solicitado ao obter o token real. |
| `MERCHANT_A_TEST_CLIENT_SECRET` | — | Secret real de `merchant-a-test-client` (gerado por `scripts/security/keycloak-bootstrap.sh`, nunca hardcoded). Ao menos um dos dois merchants precisa estar presente. |
| `MERCHANT_B_TEST_CLIENT_SECRET` | — | Secret real de `merchant-b-test-client`. |
| `LOADTEST_BUSINESS_DATES` | `5` | Quantidade de datas por merchant. |
| `LOADTEST_BASE_BUSINESS_DATE` | `2026-07-01` | Primeira data de negócio do dataset. |
| `LOADTEST_RPS` | `50` | Carga sustentada alvo. |
| `LOADTEST_MIN_OBSERVED_RPS` | `50` | Throughput mínimo observado exigido na janela sustentada. |
| `LOADTEST_RAMP_SECONDS` | `30` (10 no smoke de CI) | Duração da rampa inicial. |
| `LOADTEST_DURATION_SECONDS` | `60` | Duração da janela sustentada. |
| `LOADTEST_REQUEST_TIMEOUT_SECONDS` | `5` | Timeout por requisição. |
| `LOADTEST_MAX_FAILURE_RATE` | `0.05` | Taxa máxima de falha elegível. |
| `LOADTEST_MAX_P95_MS` | `500` | Limite de p95 em milissegundos (só no critério de evidência completa). |
| `LOADTEST_MAX_P99_MS` | `1000` | Limite de p99 em milissegundos (só no critério de evidência completa). |
| `LOADTEST_RESULT_JSON_PATH` | — | Caminho opcional para escrever a evidência JSON (nunca inclui token/senha/connection string). |
| `LOADTEST_RESULT_MARKDOWN_PATH` | — | Caminho opcional para escrever o resumo em Markdown. |

## Critérios de sucesso

### Evidência completa/manual (código de saída do processo)

Na janela sustentada:

```text
- total executado == total planejado
- falhas elegíveis <= 5%
- throughput observado >= 50 req/s por padrão, ou valor configurado em `LOADTEST_MIN_OBSERVED_RPS`
- p95 <= 500 ms
- p99 <= 1000 ms
```

O processo retorna código `0` quando esses critérios são atendidos e código `2` quando falham.

### Smoke de CI (campo `Verdict` da evidência JSON)

```text
- 50 RPS agendados (RPS configurado x duração)
- total executado == total planejado (sem falha de infraestrutura/bootstrap)
- falhas elegíveis <= 5% (sem trava de latência)
```

**Requisição elegível**: qualquer requisição HTTP de fato disparada contra o `Consolidation.Api` real durante a janela de medição (sucesso, erro HTTP ou timeout de cliente) — nunca uma falha de bootstrap/infraestrutura (stack que não sobe, Keycloak indisponível, dataset não preparado), que reprova o job de CI antes mesmo do smoke rodar e nunca entra no denominador da taxa de falha elegível.

## Status

O teste de carga é executado localmente/container-first contra o `Consolidation.Api` real, direto na rede interna do Compose, e é reproduzível tanto manualmente quanto pelo gate de CI (`performance-smoke-gate`).

Essa execução é evidência local/container-first. Ela não prova prontidão produtiva, não substitui teste em infraestrutura dimensionada e não indica que observabilidade produtiva, operação produtiva completa de DLQ, health/sinais operacionais aprofundados dos workers ou deploy produtivo estejam prontos. Execução real em runner hospedado pelo GitHub permanece pendente até o branch ser enviado.

## Evidência local/container-first (Keycloak real, alvo direto)

Comando usado:

```bash
sh scripts/ci/run-performance-smoke.sh
```

Perfil de carga:

```text
- rampa: 10s
- carga sustentada: 60s a 50 RPS
```

Resultado da janela sustentada:

```text
- total planejado: 3000
- total executado: 3000
- executado conforme planejado: True
- sucessos: 3000
- falhas: 0
- das quais timeout de cliente: 0
- taxa de sucesso: 100.00%
- taxa de falha: 0.00%
- p50: 2.66 ms
- p95: 4.23 ms
- p99: 6.39 ms
- throughput observado: 50.01 req/s
- throughput mínimo: 50.00 req/s
```

Critérios esperados:

```text
- total executado == total planejado: True
- falhas elegíveis <= 5.00%
- throughput observado >= 50.00 req/s
- p95 <= 500 ms
- p99 <= 1000 ms
```

Resultado: critérios atendidos (evidência completa) e veredito do gate de CI: `pass`.
