---
doc_id: ARCH-008
titulo: Prontidão para Implementação
versao: 1.0
status: Baseline documental aceita
responsavel: Arquitetura de Soluções
ultima_atualizacao: 2026-07-12
etapa_relacionada: Definition and Decision
---

# Prontidão para Implementação

## 1. Objetivo

Este documento fecha decisões necessárias para transformar a arquitetura documental em implementação objetiva.

Ele complementa os documentos de arquitetura, segurança, operação, observabilidade e ADRs já criados.

O objetivo é reduzir decisões implícitas durante a implementação.

Estado atual: a main materializa o baseline local/container-first completo para entrega do desafio técnico. Este documento permanece como referência de prontidão e registra pendências operacionais/produtivas ainda não concluídas.

Para a implantação AWS de referência do case, a prontidão adicional envolve ECR, ECS Fargate, RDS PostgreSQL, SQS/DLQ, Terraform, CI/CD, secrets, smoke tests e rollback.

---

## 2. Escopo deste documento

Este documento define:

```text
- semântica operacional de businessDate
- cutoff inicial
- tratamento de lançamentos retroativos
- contratos esperados antes da implementação
- invariantes transacionais
- regras de idempotência
- concorrência no Ledger e no Consolidado
- estratégia inicial de rebuild do DailyBalance
- perfil de validação para 50 RPS e 5%
- autenticação local testável
- pontos ainda fora do escopo da implementação mínima
```

---

## 3. Business date

Para a implementação inicial, `businessDate` será derivado de `occurredAt`.

Regra:

```text
businessDate = data local de occurredAt convertida para America/Sao_Paulo
```

Exemplo:

```text
occurredAt: 2026-07-11T23:30:00Z
fuso de negócio: America/Sao_Paulo
businessDate: 2026-07-11
```

`createdAt` não define a data do consolidado.

`createdAt` representa quando o lançamento foi registrado no sistema.

`occurredAt` representa quando o evento financeiro ocorreu para fins de agrupamento diário.

---

## 4. Cutoff inicial

O cutoff inicial segue o dia calendário no fuso America/Sao_Paulo.

Janela de um businessDate:

```text
início inclusivo: 00:00:00.000 America/Sao_Paulo
fim exclusivo: próximo dia às 00:00:00.000 America/Sao_Paulo
```

Exemplo:

```text
businessDate 2026-07-11
início: 2026-07-11T00:00:00 America/Sao_Paulo
fim:    2026-07-12T00:00:00 America/Sao_Paulo
```

Não haverá horário de fechamento manual na implementação inicial.

Fechamento contábil, calendário bancário, feriados, múltiplos fusos e múltiplas moedas permanecem fora do escopo inicial.

---

## 5. Lançamentos retroativos

Lançamentos retroativos serão aceitos na implementação inicial.

Um lançamento retroativo é um lançamento registrado hoje com `occurredAt` pertencente a um businessDate anterior.

Regra:

```text
lançamentos retroativos atualizam o DailyBalance correspondente ao businessDate derivado de occurredAt
```

Consequências:

```text
- o Consolidado de dias anteriores pode mudar
- a API de Consolidado deve retornar lastUpdatedAt
- a documentação e os testes devem tratar o Consolidado como visão derivada
- consultas devem considerar que o saldo diário é eventualmente consistente
```

Não serão implementados estorno, cancelamento ou fechamento imutável de dia financeiro neste incremento.

---

## 6. Capacidade de consulta de lançamentos

A implementação mínima não terá endpoint público para listar lançamentos.

Os lançamentos serão preservados no Ledger Database como fonte de verdade para auditoria, idempotência, publicação, recuperação e rebuild.

Portanto, a capacidade inicial deve ser entendida como:

```text
preservar histórico de lançamentos registrados
```

e não como:

```text
fornecer consulta pública de lançamentos por API
```

Consulta pública de lançamentos pode ser tratada em evolução futura, caso o escopo seja ampliado.

---

## 7. Contratos HTTP mínimos

A implementação inicial deve expor dois contratos HTTP principais:

```text
POST /entries
GET /daily-balances/{businessDate}
```

`POST /entries` registra um lançamento financeiro.

`GET /daily-balances/{businessDate}` consulta o consolidado diário do comerciante autenticado.

Os contratos detalhados devem ser materializados em:

```text
contracts/openapi.yaml
```

---

## 8. Contrato assíncrono mínimo

O evento inicial de integração será:

```text
EntryCreated.v1
```

O contrato detalhado deve ser materializado em:

```text
contracts/events/entry-created-v1.schema.json
```

Campos mínimos esperados:

```text
- eventId
- eventType
- eventVersion
- occurredAt
- createdAt
- correlationId
- entryId
- merchantId derivado do token autenticado
- businessDate
- type
- amount
- currency
```

---

## 9. Invariantes transacionais do Ledger

O registro de lançamento deve ocorrer em transação local no Ledger Database.

A mesma transação deve persistir:

```text
- registro de idempotência de entrada, quando aplicável
- lançamento financeiro
- registro de Outbox pendente
```

Invariante:

```text
não pode existir lançamento financeiro confirmado sem registro de Outbox correspondente
```

Outra invariante:

```text
não pode existir resposta de sucesso ao cliente antes do commit da transação local do Ledger
```

---

## 10. Idempotência de entrada

`POST /entries` deve exigir uma chave de idempotência.

Escopo da chave:

```text
merchantId + idempotencyKey
```

Regras:

```text
- mesma chave e mesmo fingerprint retornam resposta equivalente
- mesma chave e payload divergente retornam conflito
- chave de idempotência deve ser associada ao comerciante autenticado
- chave de idempotência não deve ser global entre comerciantes
```

Fingerprint deve considerar os campos de negócio relevantes:

```text
- merchantId derivado do token autenticado
- type
- amount
- currency
- occurredAt
- description quando existir no contrato
```

A canonicalização do fingerprint deve ser determinística:

```text
- merchantId derivado exclusivamente do token autenticado
- amount normalizado para duas casas decimais
- currency normalizada
- occurredAt normalizado como instante UTC
- description normalizada de forma determinística: null permanece null; string passa por trim; string vazia após trim vira null; caixa e espaços internos são preservados
```

Resposta para repetição equivalente:

```text
retornar o mesmo entryId ou uma resposta equivalente ao registro original
```

Resposta para divergência:

```text
HTTP 409 Conflict
```

---

## 11. Invariantes transacionais do Consolidado

O processamento de um evento no Consolidado deve ocorrer em transação local no Consolidation Database.

A mesma transação deve:

```text
- verificar se eventId já foi processado
- atualizar ou criar DailyBalance
- registrar eventId em ProcessedEvents
```

Invariante:

```text
o mesmo eventId não pode produzir efeito financeiro mais de uma vez
```

Outra invariante:

```text
a atualização do DailyBalance e o registro em ProcessedEvents devem ser atômicos
```

---

## 12. Chaves únicas e concorrência

Constraints mínimas esperadas:

| Estrutura | Chave única | Objetivo |
|---|---|---|
| InputIdempotency | merchantId + idempotencyKey | Evitar duplicidade por retry de entrada. |
| Entries | entryId | Identificar lançamento financeiro. |
| Outbox | outboxId | Identificar mensagem pendente. |
| Outbox | eventId | Evitar publicação duplicada do mesmo evento lógico. |
| DailyBalance | merchantId + businessDate | Garantir uma projeção por comerciante e data. |
| ProcessedEvents | eventId | Impedir reaplicação do mesmo evento. |

Concorrência no Ledger:

```text
- requisições simultâneas com mesma chave de idempotência devem resultar em apenas um lançamento
- concorrência deve ser controlada por constraint única e transação
- conflitos devem ser tratados de forma determinística
```

Concorrência no Consolidado:

```text
- eventos do mesmo comerciante e businessDate podem chegar em paralelo
- atualização de DailyBalance deve ser protegida por transação e upsert atômico no PostgreSQL
- múltiplas entregas do mesmo eventId devem ser descartadas por ProcessedEvents
```

Concorrência no Outbox Publisher:

```text
- múltiplos publishers só podem processar o mesmo registro de Outbox se houver mecanismo de claim/lock
- na implementação inicial, pode haver apenas uma instância do publisher
- se houver paralelismo, o registro precisa ter status, tentativa, lockedAt ou mecanismo equivalente
```


---

## 12.1. Retenção inicial

Para a implementação mínima, não haverá expiração automática dos registros de controle operacional.

Registros mantidos sem expiração automática no MVP:

```text
- InputIdempotency
- Outbox
- ProcessedEvents
```

Essa decisão simplifica a avaliação, evita perda de rastreabilidade durante os testes e reduz risco de reprocessamento incorreto por limpeza prematura.

Políticas de retenção por prazo, volume, arquivamento ou compliance devem ser definidas em evolução futura.

---

## 12.2. Persistência monetária

Valores monetários devem ser persistidos com precisão decimal, sem uso de `float` ou `double`.

Tipo de referência para PostgreSQL:

```text
numeric(18,2)
```

Essa escala deve ser aplicada aos valores monetários persistidos em Entries, Outbox quando armazenar payload estruturado e DailyBalance.

---

## 13. Rebuild do DailyBalance

DailyBalance é uma projeção derivada e reconstruível.

A implementação inicial deve tratar rebuild como operação controlada, não como fluxo comum de usuário.

Para preservar o isolamento entre fronteiras:

```text
Consolidation.Worker não deve acessar diretamente o Ledger Database
```

Estratégia inicial de rebuild:

```text
1. operação administrativa define merchantId e período
2. um componente da fronteira de Lançamentos relê Entries do Ledger Database
3. eventos EntryCreated.v1 são republicados ou disponibilizados para rebuild controlado
4. o Consolidado reconstrói DailyBalance para o escopo definido
5. o processo registra início, fim, escopo, resultado e eventuais divergências
```

Na implementação mínima, o rebuild pode ser documentado e testado de forma restrita.

Antes de produção, o mecanismo deve definir:

```text
- componente executor
- autorização administrativa
- coordenação com eventos novos
- uso de staging ou troca atômica da projeção
- validação de totais
- estratégia de retomada em caso de falha
```

---

## 14. Perfil de validação para 50 RPS

O requisito de 50 RPS será validado no caminho de consulta do Consolidado.

Cenário inicial de teste:

```text
- endpoint: GET /daily-balances/{businessDate}
- duração mínima do pico: 60 segundos
- carga sustentada: 50 RPS
- rampa inicial: até 30 segundos
- dataset previamente carregado
- consultas distribuídas entre múltiplos comerciantes e datas
- tokens JWT locais com issuer, audience e merchant_id compatíveis com a configuração do Compose
- throughput observado mínimo configurável, com padrão de 50 RPS na janela sustentada
- ambiente de execução declarado no resultado do teste
```

Critérios de sucesso:

```text
- taxa de falhas ou perdas de requisições elegíveis <= 5%
- throughput observado >= 50 RPS por padrão, ou valor configurado para o cenário
- p95 de latência <= 500 ms
- p99 de latência <= 1000 ms
- sem perda de dados financeiros
- sem indisponibilizar POST /entries
```

Definição de requisição elegível:

```text
requisição autenticada, autorizada, bem formada e direcionada a um businessDate válido
```

Não entram no denominador de falha do requisito de 5% quando a requisição não for elegível:

```text
- 400 Bad Request por payload inválido
- 401 Unauthorized
- 403 Forbidden
- 404 para ausência de projeção DailyBalance disponível, somente quando a consulta não fizer parte do dataset previamente preparado para o teste de carga
```

Em `GET /daily-balances/{businessDate}`, `404 Not Found` significa ausência de projeção DailyBalance disponível para o comerciante e data informados. Não significa confirmação de saldo zero.

Entram como falha:

```text
- 5xx
- timeout
- conexão encerrada sem resposta
- resposta acima do timeout definido para o teste
- 429 durante o cenário-alvo de 50 RPS
- 404 para projeção esperada no dataset previamente preparado
- throughput observado abaixo do mínimo definido para o cenário
```

---

## 15. Autenticação local testável

A execução local não deve confiar em `merchantId` arbitrário enviado pelo cliente.

Para a implementação inicial, a autenticação local deve usar tokens de teste assinados localmente ou mecanismo equivalente de autenticação de desenvolvimento.

Claims mínimas esperadas:

```text
- sub
- merchant_id
- iss
- aud
- iat
- exp
```

Regras:

```text
- POST /entries deve derivar o comerciante exclusivamente do token autenticado; o corpo da requisição não deve conter merchantId
- GET /daily-balances/{businessDate} deve derivar o comerciante do token
- acesso administrativo, se existir, deve exigir role específica
```

A execução local pode simplificar o provedor de identidade, mas não pode remover a autorização por comerciante.

---

## 16. Fora do escopo da implementação mínima

Ficam fora da implementação mínima:

```text
- consulta pública de lançamentos
- estorno e cancelamento
- fechamento manual de dia financeiro
- múltiplas moedas
- múltiplos fusos por comerciante
- calendário bancário
- conciliação contábil
- liquidação financeira
- disaster recovery completo
- alta disponibilidade real
- API administrativa completa de rebuild
```

---

## 17. Critérios de prontidão e estado de implementação

Antes de iniciar a implementação funcional, deveriam existir:

```text
- contrato OpenAPI inicial
- contrato JSON Schema do evento EntryCreated.v1
- decisão documentada de businessDate e cutoff
- regras de idempotência de entrada
- invariantes transacionais do Ledger
- invariantes transacionais do Consolidado
- estratégia inicial de concorrência
- perfil de teste de 50 RPS
- estratégia de autenticação local testável
```

Esses critérios foram usados como baseline para iniciar a implementação.

Já materializado no baseline local atual:

```text
- baseline .NET container-first
- solution BancoCarrefour.sln
- Ledger.Api
- POST /entries
- autenticação JWT local para testes e desenvolvimento com validação de assinatura, expiração, issuer e audience
- merchant_id derivado exclusivamente do token autenticado
- idempotência de entrada por merchant_id + Idempotency-Key
- fingerprint canônico
- persistência PostgreSQL do Ledger
- transação local com Entry, InputIdempotency e Outbox
- evento EntryCreated.v1 persistido na Outbox
- Ledger.OutboxPublisher
- publicação RabbitMQ com publish confirm e mandatory routing
- tratamento de mensagem sem rota/fila mantendo Outbox Pending
- ErrorResponse padronizado nos principais erros do POST /entries
- testes de contrato e integração
- CI container-first com Docker Compose
```

Também materializado no baseline local atual:

```text
- persistência PostgreSQL separada do Consolidado
- DailyBalance
- ProcessedEvent
- deduplicação por eventId
- EntryCreatedProjectionProcessor
- aplicação de CREDIT e DEBIT em DailyBalance por upsert atômico no PostgreSQL
- Consolidation.Worker consumindo EntryCreated.v1 via RabbitMQ
- política básica de consumo no consumer: sucesso e duplicado com ack; erro de validação e JSON inválido encaminhados para DLQ; erro desconhecido/transitório com retry local finito e DLQ após exceder o limite; republicação para retry/DLQ confirmada e roteada antes do ack da original
- Consolidation.Api
- GET /daily-balances/{businessDate}
- consulta por merchant_id derivado do token autenticado
- 404 para projeção indisponível sem afirmar saldo zero
- testes de integração do processador, consumer e API
- teste de carga local/container-first do Consolidado a 50 RPS na janela sustentada, com JWT local contendo issuer/audience e validação de throughput mínimo observado
- health/readiness/liveness básicos das APIs HTTP
- rate limiting básico local/in-memory em `POST /entries` e `GET /daily-balances/{businessDate}`, com 429 no padrão de erro da API e health fora do limite
- execução end-to-end local via Compose com serviços de aplicação
- DLQ básica local do Consolidado para JSON inválido, evento semanticamente inválido e erro desconhecido/transitório com retries excedidos, com publicação confirmada e roteada antes do ack da original
- retry local do Consolidado com fila `consolidation.entry-created.retry`, TTL configurável, limite configurável de tentativas e publicação confirmada e roteada antes do ack da original
- instrumentação OpenTelemetry básica nas quatro unidades implantáveis com `ILogger`, `ActivitySource`, `Meter` e OTLP configurável
- Aspire Dashboard local no Docker Compose para demonstração de logs, traces e métricas
```

Ainda pendente:

```text
- reconstrução/reprocessamento operacional completo
- rate limiting distribuído/produtivo em API Gateway, WAF, ingress ou service mesh
- validação de capacidade em ambiente produtivo ou equivalente
- observabilidade produtiva completa
- dashboards produtivos, alertas produtivos e retenção centralizada de logs
- plataforma final de observabilidade
- sinais operacionais aprofundados dos Workers, Outbox e broker
- backoff avançado e operação produtiva de mensagens isoladas
- hardening produtivo de autenticação/autorização
- multi-publisher seguro para `Ledger.OutboxPublisher`
- validação produtiva de múltiplos workers, backlog e autoscaling para `Consolidation.Worker`
- deploy produtivo/IaC
```

---

## 18. Relação com documentos

Este documento complementa:

- [01-contexto-de-negocio.md](01-contexto-de-negocio.md)
- [02-requisitos-arquiteturais.md](02-requisitos-arquiteturais.md)
- [05-arquitetura-da-solucao.md](05-arquitetura-da-solucao.md)
- [observabilidade-sli-slo-e-recuperacao.md](../operations/observabilidade-sli-slo-e-recuperacao.md)
- [arquitetura-de-seguranca.md](../security/arquitetura-de-seguranca.md)
- [docs/decisions/](../decisions/)

---

## 19. Prontidão para implantação AWS de referência

Antes de tratar a referência AWS como evidência executada, devem existir:

| Área | Critério mínimo |
|---|---|
| Imagens | APIs e workers versionados e publicados no Amazon ECR. |
| Runtime | Services ou tasks no ECS Fargate para `Ledger.Api`, `Ledger.OutboxPublisher`, `Consolidation.Worker` e `Consolidation.Api`. |
| Persistência | RDS PostgreSQL separado para Ledger e Consolidation, com migrations controladas. |
| Mensageria | SQS Standard para `EntryCreated.v1`, DLQ, redrive policy, visibility timeout e alarmes. |
| Segurança | IAM roles por componente, Secrets Manager/SSM, KMS, security groups, VPC/subnets, WAF e TLS/mTLS onde aplicável. |
| Observabilidade | ADOT, CloudWatch Logs/Metrics/Alarms, X-Ray, dashboards e alarmes de DLQ/backlog. |
| CI/CD | GitHub Actions com OIDC para AWS, build/test, push no ECR, Terraform plan/apply e deploy no ECS. |
| IaC | Terraform modular ou organizado por rede, ECR, ECS, RDS, SQS, IAM, secrets, KMS, observabilidade, alarmes e parâmetros. |
| Validação | Smoke tests pós-deploy para health, registro de lançamento, publicação, consumo e consulta do consolidado. |
| Rollback | Procedimento por imagem anterior, reversão de task definition e plano controlado para mudanças Terraform. |

No estado atual, esses itens estão documentados como referência e não devem ser interpretados como execução AWS realizada.

---

## 20. Status

Baseline local/container-first final materializado na main para entrega do desafio técnico.

O estado atual representa o baseline local/container-first completo para entrega do desafio técnico, mas não representa prontidão produtiva completa. A implementação cobre o caminho de escrita do Ledger, a Outbox transacional, a projeção materializada do Consolidado com upsert atômico de `DailyBalance`, o worker de consumo, a consulta `GET /daily-balances/{businessDate}`, autenticação JWT local com assinatura, expiração, issuer e audience, health/readiness/liveness básicos das APIs HTTP, rate limiting básico local/in-memory nos endpoints de negócio, evidência local/container-first de 50 RPS do Consolidado com planned igual a executed e throughput mínimo observado, execução end-to-end local via Compose, DLQ básica local para mensagens inválidas do Consolidado, retry local finito para erros desconhecidos/transitórios do `Consolidation.Worker` com republicação confirmada e roteada antes do ack da original e baseline local de observabilidade com OpenTelemetry/Aspire Dashboard.

Permanecem pendentes para produção real: rate limiting distribuído/produtivo, validação produtiva ou equivalente de capacidade, reconstrução/reprocessamento operacional completo, re-drive assistido da DLQ, observabilidade produtiva completa, dashboards produtivos, alertas produtivos, retenção centralizada de logs, sinais operacionais aprofundados dos Workers, Outbox e broker/fila, backoff avançado, operação produtiva completa de mensagens isoladas, OIDC/TLS/mTLS/secret manager, multi-publisher seguro, validação produtiva de múltiplos workers/backlog/autoscaling, publicação de imagens no ECR, Terraform aplicado, deploy no ECS e smoke tests AWS.
