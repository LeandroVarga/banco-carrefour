---
doc_id: OPS-001
titulo: Arquitetura Operacional
versao: 1.0
status: Baseline local
responsavel: Arquitetura de Soluções
ultima_atualizacao: 2026-07-12
etapa_relacionada: Realization and Governance
---

# Arquitetura Operacional

## 1. Objetivo

Este documento define a arquitetura operacional da solução de controle de lançamentos e consulta do consolidado diário.

A arquitetura operacional descreve como a solução deve ser executada, monitorada, escalada, recuperada e preparada para implantação em ambiente local, corporativo ou cloud.

Este documento complementa:

```text
- docs/architecture/05-arquitetura-da-solucao.md
- docs/architecture/06-diagramas.md
- docs/security/arquitetura-de-seguranca.md
- docs/decisions/
```

---

## 2. Escopo operacional

Este documento cobre:

```text
- execução local
- unidades implantáveis
- dependências operacionais
- health checks
- readiness e liveness
- implantação e configuração
- escalabilidade operacional
- tratamento de falhas
- retry e backoff
- isolamento de mensagens com falha persistente
- reprocessamento
- reconstrução da projeção DailyBalance
- backup e restore
- limites entre execução local e produção
- runbooks iniciais
```

Observabilidade, SLIs, SLOs e recuperação detalhada serão aprofundados em `observabilidade-sli-slo-e-recuperacao.md`.

Custos serão tratados em `estimativa-de-custos.md`.

---

## 3. Unidades operacionais

A solução possui quatro unidades implantáveis principais.

| Unidade | Tipo | Papel operacional |
|---|---|---|
| Ledger.Api | API | Recebe requisições de registro de lançamentos. |
| Ledger.OutboxPublisher | Worker | Publica eventos pendentes da Outbox. |
| Consolidation.Worker | Worker | Consome eventos e atualiza a projeção consolidada. |
| Consolidation.Api | API | Atende consultas do consolidado diário. |

Dependências operacionais principais:

| Dependência | Uso |
|---|---|
| Ledger Database | Persistência de lançamentos, idempotência de entrada e Outbox. |
| Consolidation Database | Persistência de DailyBalance e Processed Events. |
| Message Broker | Canal assíncrono entre Lançamentos e Consolidado. |
| Provedor de identidade | Autenticação e autorização em ambiente corporativo ou cloud. |
| Plataforma de observabilidade | Logs, métricas, traces e alertas. |
| Secret manager | Armazenamento seguro de secrets em ambiente corporativo ou cloud. |

---

## 4. Execução local

A execução local usa Docker e Docker Compose para tornar a avaliação reproduzível.

Componentes esperados na execução local:

```text
- ledger-api
- ledger-outbox-publisher
- consolidation-worker
- consolidation-api
- ledger-postgres
- consolidation-postgres
- rabbitmq
- aspire-dashboard
```

O Compose local materializa esses componentes com:

| Serviço Compose | Papel |
|---|---|
| `ledger-api` | API de Lançamentos publicada em `http://localhost:8080`. |
| `ledger-outbox-publisher` | Worker que publica eventos pendentes da Outbox. |
| `consolidation-worker` | Worker que consome `EntryCreated.v1` e atualiza `DailyBalance`. |
| `consolidation-api` | API do Consolidado publicada em `http://localhost:8081`. |
| `ledger-migrations` | Serviço efêmero que aplica migrations do Ledger. |
| `consolidation-migrations` | Serviço efêmero que aplica migrations do Consolidado. |
| `ledger-postgres` | PostgreSQL do Ledger. |
| `consolidation-postgres` | PostgreSQL do Consolidado. |
| `rabbitmq` | Broker local e console de management. |
| `aspire-dashboard` | UI local/dev para visualizar logs, traces e métricas recebidos por OTLP. |

As migrations são aplicadas por serviços efêmeros do Compose usando `dotnet ef database update --connection ...`. APIs e workers não executam migrations automaticamente no startup.

Objetivos da execução local:

```text
- validar os fluxos principais
- permitir testes automatizados
- demonstrar separação entre APIs e workers
- demonstrar persistências independentes
- demonstrar comunicação assíncrona
- demonstrar idempotência e reprocessamento
- permitir inspeção de logs e comportamento operacional
- permitir visualização local de telemetria OpenTelemetry no Aspire Dashboard
```

Docker Compose não representa alta disponibilidade real e não deve ser tratado como topologia definitiva de produção.

O Aspire Dashboard é componente local/dev. Ele não representa a plataforma produtiva final de observabilidade, não define retenção centralizada, não implementa alertas produtivos e não substitui dashboards operacionais formais.

---

## 5. Configuração por ambiente

A solução deve separar configuração de código.

Configurações esperadas:

```text
- connection string do Ledger Database
- connection string do Consolidation Database
- credenciais do broker
- endpoint do broker
- parâmetros de retry
- parâmetros de backoff
- limites de timeout
- configuração de autenticação
- configuração de logs
- configuração de métricas
- configuração de traces
- configuração de endpoint OTLP
- limites de rate limit
```

No ambiente local, essas configurações podem ser fornecidas por variáveis de ambiente e arquivos de exemplo sem secrets reais.

Em ambiente corporativo ou cloud, secrets e credenciais devem ser fornecidos por secret manager aprovado pela plataforma.

---

## 6. Health checks, readiness e liveness

APIs e workers devem expor sinais operacionais adequados ao seu papel.

Neste incremento, os sinais HTTP básicos foram implementados para as APIs:

| API | Liveness | Readiness | Dependência mínima verificada |
|---|---|---|---|
| Ledger.Api | `GET /health/live` | `GET /health/ready` | Ledger Database/PostgreSQL. |
| Consolidation.Api | `GET /health/live` | `GET /health/ready` | Consolidation Database/PostgreSQL. |

Liveness indica que o processo HTTP está vivo e responde ao runtime. Não valida banco, broker ou consistência do fluxo.

Readiness indica que a API está pronta para receber tráfego. Nas APIs HTTP atuais, readiness valida a conexão com o PostgreSQL da respectiva fronteira. Quando essa dependência mínima está indisponível, `GET /health/ready` retorna 503.

Os endpoints de health são públicos e não exigem autenticação. A resposta é um JSON simples com `status` e `checks`, sem connection string, SQL, stack trace ou detalhe interno.

Uso operacional recomendado:

```text
- usar /health/live para decisão de reinício do processo pelo orquestrador
- usar /health/ready para decisão de roteamento de tráfego
- usar /health/ready em pipelines ou smoke tests após subir dependências mínimas
- não usar /health/live como prova de capacidade de atender requisições de negócio
```

Workers não recebem endpoint HTTP artificial neste incremento. `Ledger.OutboxPublisher` e `Consolidation.Worker` ainda dependem de supervisão operacional por processo, logs, backlog/lag e métricas futuras.

Há baseline local de observabilidade com OpenTelemetry, OTLP e Aspire Dashboard para demonstração. Observabilidade produtiva completa, métricas Prometheus, tracing distribuído em plataforma produtiva, dashboards produtivos, alertas, retenção centralizada, backoff avançado e operação produtiva de mensagens isoladas permanecem pendentes.

### 6.5 Rate limiting das APIs HTTP

As APIs HTTP aplicam rate limiting básico local/in-memory somente aos endpoints de negócio:

```text
- Ledger.Api: POST /entries
- Consolidation.Api: GET /daily-balances/{businessDate}
```

Endpoints de health não aplicam rate limit:

```text
- GET /health/live
- GET /health/ready
```

Quando o limite é excedido, a API retorna `HTTP 429 Too Many Requests` no padrão `ErrorResponse`, preservando `correlationId` quando o cliente informa `X-Correlation-Id` válido.

O limiter usa janela fixa local por processo. A partição usa `merchant_id` quando a requisição está autenticada e fallback por IP/anonymous quando não há contexto autenticado. O limite padrão é intencionalmente permissivo para não interferir no teste local/container-first de 50 RPS do Consolidado; valores podem ser ajustados por configuração `RateLimit__PermitLimit` e `RateLimit__WindowSeconds`.

Esse mecanismo é um baseline local de proteção contra abuso. Ele não substitui rate limiting distribuído/produtivo em API Gateway, WAF, ingress, service mesh ou mecanismo equivalente, especialmente quando houver múltiplas réplicas de API.

### 6.1 Ledger.Api

Health esperado:

```text
- aplicação inicializada
- conexão com Ledger Database disponível
- configuração obrigatória carregada
```

Readiness esperado:

```text
- API pronta para receber requisições
- Ledger Database acessível
- migrations aplicadas quando aplicável
```

Liveness esperado:

```text
- processo da API está ativo
- runtime responde sem travamento
```

### 6.2 Ledger.OutboxPublisher

Health esperado:

```text
- worker inicializado
- conexão com Ledger Database disponível
- conexão com broker disponível quando necessária para publicação
```

Readiness esperado:

```text
- worker apto a buscar eventos pendentes
- worker apto a publicar no broker
```

Liveness esperado:

```text
- processo ativo
- loop de publicação sem travamento
```

### 6.3 Consolidation.Worker

Health esperado:

```text
- worker inicializado
- conexão com broker disponível
- conexão com Consolidation Database disponível
```

Readiness esperado:

```text
- worker apto a consumir eventos
- worker apto a atualizar DailyBalance
- worker apto a registrar eventos processados
```

Liveness esperado:

```text
- processo ativo
- loop de consumo sem travamento
```

### 6.4 Consolidation.Api

Health esperado:

```text
- aplicação inicializada
- conexão com Consolidation Database disponível
- configuração obrigatória carregada
```

Readiness esperado:

```text
- API pronta para receber consultas
- Consolidation Database acessível
```

Liveness esperado:

```text
- processo da API está ativo
- runtime responde sem travamento
```

---

## 7. Estratégia de implantação

A implantação local deve seguir esta ordem lógica:

```text
1. subir Ledger Database
2. subir Consolidation Database
3. subir Message Broker
4. aplicar migrations por `ledger-migrations` e `consolidation-migrations`
5. subir Ledger.Api
6. subir Ledger.OutboxPublisher
7. subir Consolidation.Worker
8. subir Consolidation.Api
9. executar testes e validações
```

Em ambiente corporativo ou cloud, a ordem pode ser automatizada por pipeline e orquestrador, mantendo os mesmos princípios:

```text
- dependências prontas antes dos consumidores
- migrations controladas
- health checks antes de roteamento de tráfego
- rollback ou plano de reversão definido
- configuração e secrets por ambiente
```

---

## 8. Estratégia de escala operacional

A arquitetura permite escala independente por unidade.

| Unidade | Estratégia de escala | Métrica orientadora |
|---|---|---|
| Ledger.Api | Escala horizontal conforme tráfego de escrita. | RPS, latência, erros, CPU, memória, pool de conexão. |
| Ledger.OutboxPublisher | Escala controlada conforme eventos pendentes. | Tamanho da Outbox, idade do evento mais antigo, falhas de publicação. |
| Consolidation.Worker | Escala conforme backlog e lag. | Mensagens pendentes, lag, tempo de processamento, falhas de consumo. |
| Consolidation.Api | Escala horizontal conforme tráfego de leitura. | RPS, latência, taxa de erro, CPU, memória, pool de conexão. |
| Ledger Database | Escala conforme escrita e transações. | Latência de query, conexões, locks, I/O, tamanho das tabelas. |
| Consolidation Database | Escala conforme leitura e atualização da projeção. | Latência de query, conexões, locks, I/O, índice por comerciante e data. |
| Message Broker | Escala conforme volume de mensagens. | Mensagens prontas, mensagens não confirmadas, taxa de publicação e consumo. |

O requisito de 50 RPS se aplica ao caminho de consulta do Consolidado.

A projeção DailyBalance deve reduzir o custo de leitura e apoiar o atendimento desse requisito.

### 8.1 Restrições atuais de escala horizontal dos workers

No baseline atual, `Ledger.OutboxPublisher` deve operar com uma réplica.

Motivo:

```text
- ainda não existe claim/lock transacional com SKIP LOCKED ou padrão equivalente para múltiplos publishers
- múltiplas réplicas podem selecionar o mesmo registro pendente e publicar eventos redundantes
- o consumidor idempotente reduz impacto financeiro, mas não remove custo, ruído operacional ou duplicidade observável no broker
```

Evolução futura esperada:

```text
- claim transacional de registros de Outbox
- campos como locked_at e locked_by
- ownership de retry
- SELECT ... FOR UPDATE SKIP LOCKED ou padrão equivalente
```

`Consolidation.Worker` não depende mais de uma única réplica para preservar a correção financeira do `DailyBalance` contra lost update no banco.

Motivo:

```text
- DailyBalance é atualizado por upsert atômico no PostgreSQL
- ProcessedEvent mantém deduplicação por eventId dentro da transação local
- eventos concorrentes para o mesmo merchantId/businessDate não devem perder incremento no banco
```

Isso não significa prontidão produtiva de escala horizontal. Múltiplos workers ainda exigem validação de carga, sizing, prefetch, contenção no banco, backlog, lag, autoscaling, métricas e operação produtiva antes de serem recomendados como topologia final.

Evolução futura esperada:

```text
- upsert atômico
- concurrency token ou controle de versão
- particionamento por merchantId
- fila particionada ou serialização por chave
```

Essas restrições não invalidam a demonstração local do desafio. Elas limitam a estratégia de escala horizontal até que os mecanismos de concorrência acima sejam implementados.

---

## 9. Tratamento de falhas

A arquitetura diferencia falhas por fronteira e por dependência.

| Falha | Impacto | Resposta operacional |
|---|---|---|
| Consolidation.Api indisponível | Consultas falham ou degradam. | Reiniciar, escalar, verificar banco, verificar logs e métricas. |
| Consolidation.Worker indisponível | Consolidado fica defasado. | Reiniciar worker, verificar broker, medir backlog e lag. |
| Broker indisponível | Publicação fica atrasada. | Outbox mantém eventos pendentes, publisher tenta novamente. |
| Consolidation Database indisponível | Consulta e atualização do Consolidado falham. | Verificar banco, pausar consumo se necessário, retomar após recuperação. |
| Ledger Database indisponível | Registro de lançamentos fica indisponível. | Acionar recuperação prioritária da fonte de verdade. |
| Evento com erro de negócio ou formato inválido | Processamento pode falhar repetidamente. | Isolar mensagem, registrar motivo e permitir investigação. |
| Evento duplicado | Risco de duplicidade no saldo. | Detectar por Processed Events e descartar sem novo efeito financeiro. |

Falhas no Consolidado não devem indisponibilizar o registro de novos lançamentos.

Falhas no Ledger Database afetam diretamente o registro financeiro e exigem maior prioridade operacional.

---

## 10. Retry e backoff

Retries devem ser aplicados para falhas temporárias.

Pontos com retry:

```text
- publicação de eventos da Outbox
- consumo de eventos do broker
- atualização da projeção DailyBalance quando houver falha transitória
- comunicação com dependências internas quando aplicável
```

Diretrizes:

```text
- aplicar limite de tentativas
- usar backoff progressivo quando aplicável
- diferenciar falha transitória de falha permanente
- registrar tentativa, erro e próximo horário de retry
- expor métricas de falhas e tentativas
- evitar retry infinito sem visibilidade operacional
```

Retries não devem gerar duplicidade de efeito financeiro.

A idempotência de entrada e o controle de eventos processados protegem os fluxos contra repetição indevida.

---

## 11. Isolamento de mensagens com falha persistente

Mensagens com falha persistente devem ser isoladas para investigação.

Esse isolamento pode ser materializado por:

```text
- DLQ
- tabela de mensagens rejeitadas
- status específico de erro operacional
- mecanismo equivalente do broker ou plataforma
```

Informações mínimas para investigação:

```text
- event_id
- entry_id quando disponível
- merchant_id quando necessário
- tipo do evento
- payload ou referência segura ao payload
- motivo da falha
- quantidade de tentativas
- última tentativa
- componente que falhou
- correlation_id
```

O isolamento evita que uma mensagem problemática bloqueie indefinidamente o processamento das demais.

No ambiente local atual, o `Consolidation.Worker` implementa isolamento básico para mensagens irrecuperáveis do consumo `EntryCreated.v1`:

| Papel | Nome |
|---|---|
| Exchange de eventos | `ledger.events` |
| Fila principal | `consolidation.entry-created` |
| Dead-letter exchange | `consolidation.dlx` |
| Dead-letter queue | `consolidation.entry-created.dlq` |
| Routing key da DLQ | `consolidation.entry-created.dead` |
| Retry exchange | `consolidation.retry` |
| Retry queue | `consolidation.entry-created.retry` |
| Routing key de retry | `consolidation.entry-created.retry` |

Comportamento atual:

```text
- evento válido: processa DailyBalance e confirma com ack
- evento duplicado: confirma com ack sem duplicar efeito financeiro
- JSON inválido: publica na DLQ com mandatory routing e publisher confirms antes de confirmar com ack
- erro de validação semântica: publica na DLQ com mandatory routing e publisher confirms antes de confirmar com ack
- erro desconhecido/transitório: publica na fila de retry, incrementa x-retry-count e confirma com ack somente após mandatory routing e publisher confirms
- erro desconhecido/transitório com x-retry-count >= RabbitMq__MaxRetryAttempts: publica na DLQ e confirma com ack somente após mandatory routing e publisher confirms
- falha ao republicar para retry/DLQ: não confirma a original e devolve a mensagem para reprocessamento com nack/requeue
```

O retry local usa TTL na fila `consolidation.entry-created.retry` e DLX de volta para `ledger.events` com routing key `ledger.entry.created.v1`. O TTL padrão é configurável por `RabbitMq__RetryDelayMilliseconds` e o limite por `RabbitMq__MaxRetryAttempts`.

Essa política evita descarte silencioso de mensagens inválidas ou com falha transitória persistente, condiciona o ack local à publicação confirmada e roteada para retry/DLQ e permite inspeção local pelo RabbitMQ Management. Backoff progressivo, reprocessamento assistido, alertas produtivos e operação produtiva completa de mensagens isoladas permanecem pendentes.

Evolução recomendada antes de produção: adicionar fault injection automatizado para timeout de publisher confirm, fechamento de canal e exceção durante confirmação de publicação. Esse teste não foi incluído no baseline local para evitar introduzir abstração artificial no worker apenas para simulação.

---

## 12. Reprocessamento

Reprocessamento deve ser controlado e rastreável.

Cenários de reprocessamento:

```text
- mensagem isolada após correção de causa raiz
- falha transitória resolvida
- ajuste operacional em evento não processado
- reconstrução parcial do Consolidado
- validação de consistência entre fonte de verdade e projeção
```

Diretrizes:

```text
- reprocessar somente eventos elegíveis
- preservar idempotência
- registrar quem ou o que iniciou o reprocessamento
- registrar data e hora do reprocessamento
- registrar resultado do reprocessamento
- evitar alteração direta do saldo sem rastreabilidade
```

Reprocessamento não deve criar novo lançamento financeiro.

Ele deve reaplicar ou corrigir o processamento da visão derivada.

---

## 13. Reconstrução da projeção DailyBalance

DailyBalance é uma projeção materializada e reconstruível.

A reconstrução deve partir da fonte de verdade financeira preservada em Lançamentos.

Cenários de rebuild:

```text
- inconsistência identificada no Consolidado
- perda ou corrupção da projeção
- alteração controlada de regra de consolidação
- necessidade de recomputar período específico
- validação operacional ou auditoria
```

Diretrizes:

```text
- definir escopo do rebuild por comerciante e período
- pausar ou coordenar consumo quando necessário
- limpar ou recalcular a projeção afetada
- reaplicar lançamentos de forma idempotente
- registrar início, fim e resultado do rebuild
- validar totais após reconstrução
```

O rebuild não substitui backup e restore.

Ele é mecanismo de reconstrução da visão derivada a partir da fonte de verdade.

---

## 14. Backup e restore

Backup e restore devem considerar a criticidade diferente das persistências.

| Persistência | Papel | Diretriz operacional |
|---|---|---|
| Ledger Database | Fonte de verdade financeira. | Prioridade máxima para backup, restore, retenção e proteção. |
| Consolidation Database | Visão derivada e controle de processamento. | Backup recomendado, com possibilidade de reconstrução a partir de Lançamentos. |

Diretrizes:

```text
- Ledger Database exige proteção operacional mais rigorosa
- Consolidation Database pode ser reconstruído, mas ainda deve ter backup operacional
- backups devem ser testados por restore
- restore deve ter procedimento documentado
- RTO e RPO finais dependem do ambiente de produção
```

Em ambiente local, backup e restore podem ser simplificados.

Em ambiente corporativo ou cloud, devem seguir política da plataforma.

---

## 15. Operação local versus produção

| Aspecto | Execução local | Ambiente corporativo ou cloud |
|---|---|---|
| Runtime | Docker Compose. | Plataforma de containers, orquestrador ou serviço gerenciado. |
| Alta disponibilidade | Não representada. | Definida por réplicas, zonas, serviços gerenciados e política de plataforma. |
| Banco de dados | PostgreSQL em container. | Banco gerenciado ou aprovado pela plataforma. |
| Broker | RabbitMQ em container. | Broker ou fila gerenciada equivalente. |
| Secrets | Variáveis locais e exemplos sem segredo real. | Secret manager. |
| Observabilidade | OpenTelemetry com OTLP e Aspire Dashboard local/dev. | Plataforma centralizada de logs, métricas, traces, alertas e retenção. |
| Segurança | Representação simplificada. | Identidade, rede, criptografia, gateway e políticas corporativas. |
| Backup e restore | Simplificado. | Procedimento formal com retenção, teste e auditoria. |

A execução local valida a solução e seus fluxos. O Compose demonstra separação de persistência e credenciais PostgreSQL por fronteira (`ledger` e `consolidation`), mas mantém uma credencial RabbitMQ local compartilhada para publisher e consumer. Separação completa de usuário/grants do broker, vhosts, credenciais administrativas e rotação de secrets permanece como hardening produtivo.

Produção exige decisões adicionais de plataforma, segurança, escalabilidade, disponibilidade e recuperação.

---

## 16. Runbooks iniciais

Runbooks devem ser criados para os principais cenários operacionais.

Runbooks recomendados:

```text
- verificar saúde das APIs
- verificar saúde dos workers
- investigar aumento de falhas no Consolidado
- investigar crescimento da Outbox
- investigar backlog no broker
- investigar mensagens isoladas
- reprocessar mensagem isolada
- reconstruir DailyBalance por comerciante e período
- restaurar Ledger Database
- restaurar Consolidation Database
- validar consistência entre lançamentos e consolidado
```

Cada runbook deve conter:

```text
- sintoma
- impacto
- sinais observáveis
- passos de diagnóstico
- ação de contenção
- ação de recuperação
- validação pós-recuperação
- critérios de escalonamento
```

---

## 17. Critérios de prontidão operacional

Antes de considerar a solução pronta para execução controlada, os seguintes critérios devem estar atendidos:

| ID | Critério |
|---|---|
| OPS-CA-001 | APIs possuem health checks. |
| OPS-CA-002 | Workers possuem sinais de execução e falha. |
| OPS-CA-003 | Outbox expõe quantidade e idade dos eventos pendentes. |
| OPS-CA-004 | Broker expõe backlog e falhas de entrega. |
| OPS-CA-005 | Consolidation.Worker expõe eventos processados, falhas e duplicidades descartadas. |
| OPS-CA-006 | Consolidation.Api expõe latência, RPS e taxa de erro. |
| OPS-CA-007 | Mensagens com falha persistente podem ser isoladas. |
| OPS-CA-008 | Reprocessamento é controlado e rastreável. |
| OPS-CA-009 | DailyBalance pode ser reconstruído a partir da fonte de verdade. |
| OPS-CA-010 | Diferenças entre execução local e produção estão documentadas. |

---

## 18. Relação com ASRs, ABBs e SBBs

| Item | Relação operacional |
|---|---|
| ASR-001 | Lançamentos permanece isolado de falhas do Consolidado. |
| ASR-002 | Consolidado deve suportar pico de consulta de 50 RPS. |
| ASR-003 | Falhas ou perdas de requisição no pico devem ser mensuradas. |
| ASR-005 | Defasagem do Consolidado deve ser observável e recuperável. |
| ASR-010 | Fluxo deve emitir sinais operacionais. |
| ASR-011 | Falhas de publicação, consumo ou consolidação devem ser recuperáveis. |
| ABB-013 | Observabilidade do fluxo. |
| ABB-014 | Recuperação operacional. |
| SBB-016 | Observability. |
| SBB-017 | Operational Recovery. |
| SBB-018 | Containers and Local Runtime. |
| SBB-019 | Configuration and Secrets. |

---

## 19. Relação com ADRs

| ADR | Relação operacional |
|---|---|
| ADR-0002 | Outbox permite publicação recuperável. |
| ADR-0003 | Consumo at-least-once exige idempotência e monitoramento. |
| ADR-0004 | DailyBalance é uma projeção reconstruível. |
| ADR-0005 | Persistências independentes exigem operação por fronteira. |
| ADR-0006 | PostgreSQL exige migrations, backup, restore, índices e monitoramento. |
| ADR-0007 | Broker exige monitoramento de filas, retry, backlog e isolamento de falhas. |
| ADR-0008 | Quatro unidades implantáveis exigem health checks e operação por componente. |
| ADR-0009 | Stack tecnológica define APIs, workers, containers e observabilidade base. |
| ADR-0010 | Execução local e produção possuem responsabilidades diferentes. |
| ADR-0011 | Segurança operacional exige secrets, menor privilégio e controle de acesso. |
| ADR-0012 | Observabilidade e prontidão operacional definem SLIs, SLOs, alertas, recuperação e evidências. |
| ADR-0014 | OpenTelemetry define instrumentação vendor-neutral e Aspire Dashboard local para demonstração. |

As decisões específicas de observabilidade estão registradas em `docs/decisions/ADR-0012-observabilidade-e-prontidao-operacional.md` e `docs/decisions/ADR-0014-instrumentacao-de-observabilidade-com-opentelemetry.md`.

---

## 20. Relação com documentos

Este documento complementa:

```text
- docs/architecture/05-arquitetura-da-solucao.md
- docs/architecture/06-diagramas.md
- docs/architecture/07-rastreabilidade.md
- docs/security/arquitetura-de-seguranca.md
- docs/decisions/
```

Este documento será complementado por:

```text
- docs/operations/observabilidade-sli-slo-e-recuperacao.md
- docs/operations/estimativa-de-custos.md
```

---

## 21. Status

Documento atualizado como baseline operacional local, com pendências produtivas preservadas para observabilidade completa, reprocessamento, rebuild, segurança e capacidade.
