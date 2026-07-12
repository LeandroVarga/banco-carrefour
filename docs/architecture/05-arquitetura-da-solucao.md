---
doc_id: ARCH-005
titulo: Arquitetura da Solução
versao: 1.0
status: Rascunho
responsavel: Arquitetura de Soluções
ultima_atualizacao: 2026-07-11
etapa_relacionada: Definition and Decision
---

# Arquitetura da Solução

## 1. Objetivo

Este documento consolida a arquitetura alvo da solução para controle de lançamentos e consulta do consolidado diário.

A arquitetura é derivada dos requisitos de negócio, requisitos arquiteturais, ASRs, ABBs, ADRs e SBBs já documentados.

O foco deste documento é explicar a composição da solução, as responsabilidades dos componentes, os fluxos principais, a estratégia de consistência, disponibilidade, escala e recuperação.

Os diagramas serão detalhados em `06-diagramas.md`.

---

## 2. Síntese da arquitetura

A solução é organizada em duas fronteiras principais:

```text
- Lançamentos
- Consolidado
```

Lançamentos é a fronteira responsável pelo registro financeiro e pela fonte de verdade dos lançamentos.

Consolidado é a fronteira responsável pela visão derivada de leitura, calculada por comerciante e data.

A comunicação entre as fronteiras é assíncrona.

O registro de lançamentos não depende de chamada síncrona ao Consolidado.

O Consolidado é atualizado por eventos publicados a partir da Outbox da fronteira de Lançamentos.

Essa arquitetura atende ao requisito de manter o serviço de controle de lançamentos disponível mesmo quando o Consolidado falhar.

---

## 3. Visão lógica da solução

A solução é composta pelas seguintes unidades implantáveis e dependências:

```text
Cliente / Comerciante
    |
    v
Ledger.Api
    |
    v
Ledger Database
    |
    v
Ledger.OutboxPublisher
    |
    v
Message Broker
    |
    v
Consolidation.Worker
    |
    v
Consolidation Database
    |
    v
Consolidation.Api
    |
    v
Cliente / Comerciante
```

As unidades principais são:

| Unidade | Papel na arquitetura |
|---|---|
| Ledger.Api | Recebe e registra lançamentos financeiros. |
| Ledger Database | Armazena lançamentos, idempotência de entrada e Outbox. |
| Ledger.OutboxPublisher | Publica eventos pendentes da Outbox no broker. |
| Message Broker | Transporta eventos de Lançamentos para Consolidado. |
| Consolidation.Worker | Consome eventos e atualiza a projeção consolidada. |
| Consolidation Database | Armazena DailyBalance e eventos processados. |
| Consolidation.Api | Expõe consulta do consolidado diário. |

---

## 4. Responsabilidades por fronteira

### 4.1 Lançamentos

A fronteira de Lançamentos protege o caminho crítico de escrita financeira.

Responsabilidades:

```text
- receber comandos de registro de crédito e débito
- validar dados de entrada
- identificar o comerciante a partir do contexto autenticado ou validado
- controlar idempotência de entrada
- persistir lançamentos financeiros
- registrar eventos pendentes na Outbox
- disponibilizar rastreabilidade do registro
- manter a fonte de verdade financeira
```

Essa fronteira não depende do Consolidado para registrar novos lançamentos.

### 4.2 Consolidado

A fronteira de Consolidado mantém a visão derivada de leitura.

Responsabilidades:

```text
- consumir eventos de lançamentos
- controlar eventos já processados
- atualizar a projeção DailyBalance
- permitir consulta por comerciante e data
- indicar metadados de atualização da visão consolidada
- apoiar reprocessamento e reconstrução da projeção
```

O Consolidado pode ficar temporariamente defasado, desde que essa defasagem seja observável e recuperável.

---

## 5. Fluxo de registro de lançamento

O fluxo de registro ocorre dentro da fronteira de Lançamentos.

Sequência:

```text
1. Cliente envia requisição para Ledger.Api.
2. Ledger.Api autentica e autoriza a requisição.
3. Ledger.Api valida o payload recebido.
4. Ledger.Api aplica idempotência de entrada.
5. Ledger.Api persiste o lançamento financeiro.
6. Ledger.Api registra evento pendente na Outbox na mesma transação local.
7. Ledger.Api retorna resposta ao cliente.
```

A transação local deve garantir consistência entre:

```text
- lançamento financeiro registrado
- registro de idempotência de entrada
- evento pendente na Outbox
```

Esse fluxo evita perda silenciosa entre o registro financeiro e a publicação posterior do evento.

---

## 6. Fluxo de publicação via Outbox

O fluxo de publicação é executado fora do ciclo síncrono da API.

Sequência:

```text
1. Ledger.OutboxPublisher busca eventos pendentes na Outbox.
2. Ledger.OutboxPublisher publica o evento no Message Broker.
3. Em caso de sucesso, o evento é marcado como publicado.
4. Em caso de falha temporária, o evento permanece recuperável para nova tentativa.
5. Em caso de falha persistente, o evento deve gerar sinal operacional para investigação.
```

Esse fluxo permite que falhas temporárias no broker ou no consumidor não impeçam o registro de novos lançamentos.

---

## 7. Fluxo de consolidação

O fluxo de consolidação atualiza a visão de leitura a partir dos eventos publicados.

Sequência:

```text
1. Consolidation.Worker recebe evento do Message Broker.
2. Consolidation.Worker valida o evento recebido.
3. Consolidation.Worker verifica se o evento já foi processado.
4. Se o evento já foi processado, ele é descartado sem novo efeito financeiro.
5. Se o evento ainda não foi processado, o worker atualiza a projeção DailyBalance.
6. O worker registra o evento como processado.
7. O processamento é confirmado no broker conforme a política de consumo.
```

O processamento deve ser idempotente.

No escopo inicial, a consolidação não depende de ordenação global dos eventos, pois o saldo diário é calculado pela aplicação idempotente de lançamentos imutáveis.

---

## 8. Fluxo de consulta do consolidado

O fluxo de consulta é atendido pela fronteira de Consolidado.

Sequência:

```text
1. Cliente solicita o consolidado diário por data.
2. Consolidation.Api autentica e autoriza a requisição.
3. A identificação do comerciante é obtida do contexto autenticado ou validada contra ele.
4. Consolidation.Api consulta a projeção DailyBalance.
5. Consolidation.Api retorna totais, saldo e metadados de atualização.
```

O contrato inicial de consulta é:

```text
GET /daily-balances/{businessDate}
```

A resposta deve conter, no mínimo:

```text
- comerciante
- data de negócio
- total de créditos
- total de débitos
- saldo diário
- quantidade de lançamentos considerados
- data e hora da última atualização
```

---

## 9. Estratégia de consistência

A arquitetura adota consistência eventual entre Lançamentos e Consolidado.

Lançamentos é consistente dentro da sua própria fronteira transacional.

Consolidado é atualizado de forma assíncrona a partir dos eventos publicados.

Essa decisão aceita uma defasagem temporária entre o registro do lançamento e a atualização da visão consolidada.

A defasagem deve ser tratada por:

```text
- Outbox durável
- publicação recuperável
- broker ou fila
- consumo at-least-once
- processamento idempotente
- registro de eventos processados
- métricas de atraso, backlog e falhas
- reprocessamento e reconstrução quando necessário
```

Essa estratégia protege o registro financeiro e mantém o Consolidado como visão derivada e reconstruível.

---

## 10. Estratégia de disponibilidade e falhas

A arquitetura foi desenhada para que falhas no Consolidado não indisponibilizem Lançamentos.

Cenários principais:

| Cenário de falha | Comportamento esperado |
|---|---|
| Consolidation.Api indisponível | Consultas ao consolidado falham ou degradam, mas novos lançamentos continuam sendo registrados. |
| Consolidation.Worker indisponível | Eventos ficam acumulados no broker ou pendentes de processamento; Lançamentos continua registrando. |
| Message Broker temporariamente indisponível | Outbox mantém eventos pendentes para publicação posterior. |
| Falha temporária no processamento | Worker aplica retry conforme política operacional. |
| Mensagem com falha persistente | Mensagem deve ser isolada para investigação e reprocessamento controlado. |
| Consolidation Database indisponível | Consultas e processamento do Consolidado ficam afetados; Lançamentos permanece isolado. |
| Ledger Database indisponível | Registro de lançamentos fica indisponível, pois essa é a fonte de verdade financeira. |

O principal limite da solução é que Lançamentos depende da sua própria persistência transacional.

Falhas na fonte de verdade financeira devem ser tratadas por alta disponibilidade, backup, restore e estratégia operacional adequada.

---

## 11. Estratégia de escala

A arquitetura permite escalar partes diferentes do fluxo de forma independente.

| Parte da solução | Estratégia de escala |
|---|---|
| Ledger.Api | Escala horizontal conforme volume de registros. |
| Ledger.OutboxPublisher | Escala controlada conforme volume de eventos pendentes e segurança de publicação. |
| Message Broker | Escala conforme capacidade do broker ou serviço gerenciado adotado. |
| Consolidation.Worker | Escala conforme backlog, lag e volume de eventos. |
| Consolidation.Api | Escala horizontal para suportar pico de consulta de 50 RPS. |
| Ledger Database | Índices, pool de conexões, capacidade de escrita e estratégia operacional. |
| Consolidation Database | Índices por comerciante e data, capacidade de leitura e atualização da projeção. |

O requisito de 50 RPS se aplica ao Consolidado.

A projeção DailyBalance reduz o custo de consulta porque evita recalcular o saldo a partir de todos os lançamentos em cada requisição.

A taxa máxima de 5% de falhas ou perdas de requisições no pico deve ser medida no caminho de consulta do Consolidado.

No baseline atual, há restrições explícitas para escala horizontal dos workers:

```text
- Ledger.OutboxPublisher deve operar com uma réplica até existir claim/lock transacional com SKIP LOCKED ou padrão equivalente.
- Consolidation.Worker deve operar com uma réplica até DailyBalance usar incremento atômico, controle de versão, particionamento por merchantId ou serialização por chave.
```

Múltiplas réplicas do publisher podem publicar eventos redundantes. O consumo idempotente reduz impacto financeiro, mas não elimina custo e ruído operacional. Múltiplos workers podem gerar conflito ou atualização perdida para o mesmo `merchantId/businessDate`.

---

## 12. Estratégia de recuperação

A recuperação da solução depende de preservar a fonte de verdade e tornar o fluxo assíncrono retomável.

Mecanismos principais:

```text
- lançamentos persistidos como fonte de verdade
- Outbox para eventos pendentes de publicação
- retry de publicação
- consumo at-least-once
- controle de eventos processados
- retry de consumo
- isolamento de mensagens com falha persistente
- reprocessamento controlado
- reconstrução da projeção DailyBalance
```

A reconstrução do Consolidado deve partir dos lançamentos financeiros registrados.

A projeção DailyBalance pode ser descartada e reconstruída quando houver necessidade operacional, desde que a fonte de verdade esteja preservada.

---

## 13. Segurança arquitetural

A segurança será detalhada em `docs/security/arquitetura-de-seguranca.md`.

Na arquitetura alvo, os pontos mínimos são:

```text
- autenticação das chamadas externas
- autorização por comerciante
- proteção contra consulta cruzada entre comerciantes
- validação de entrada
- rate limit e proteção contra abuso
- credenciais separadas por componente
- acesso restrito aos bancos
- acesso restrito ao broker
- proteção de secrets por ambiente
- comunicação segura conforme ambiente de execução
```

A identificação do comerciante não deve ser aceita de forma cega a partir do payload externo.

Quando o comerciante for informado explicitamente, ele deve ser validado contra o contexto autenticado e autorizado.

---

## 14. Observabilidade arquitetural

A observabilidade será detalhada em `docs/operations/observabilidade-sli-slo-e-recuperacao.md`.

A arquitetura deve emitir sinais nos seguintes pontos:

```text
- requisições recebidas em Ledger.Api
- lançamentos registrados
- tentativas repetidas de entrada
- eventos pendentes na Outbox
- falhas de publicação
- eventos publicados
- mensagens consumidas
- eventos já processados
- eventos duplicados descartados
- falhas de consumo
- atraso entre registro e consolidação
- consultas ao Consolidado
- taxa de erro do Consolidado
- backlog e lag de processamento
```

Esses sinais sustentam diagnóstico, alertas, análise de falhas e validação dos RNFs.

---

## 15. Relação com ADRs

A arquitetura alvo é sustentada pelas seguintes decisões:

| ADR | Decisão sustentada na arquitetura |
|---|---|
| ADR-0000 | Define a semântica do consolidado diário como movimento líquido do dia. |
| ADR-0001 | Separa Lançamentos e Consolidado em fronteiras distintas. |
| ADR-0002 | Adota Outbox para publicação confiável. |
| ADR-0003 | Adota consumo at-least-once com processamento idempotente. |
| ADR-0004 | Adota projeção materializada DailyBalance. |
| ADR-0005 | Define persistências independentes por fronteira. |
| ADR-0006 | Define persistência relacional com PostgreSQL como referência. |
| ADR-0007 | Define canal assíncrono com broker e RabbitMQ local. |
| ADR-0008 | Define quatro unidades implantáveis principais. |
| ADR-0009 | Define a stack tecnológica de referência. |
| ADR-0010 | Define execução local, portabilidade cloud e padrões corporativos. |
| ADR-0011 | Define decisões de segurança para autenticação, autorização, dados, secrets e comunicação entre serviços. |
| ADR-0012 | Define observabilidade, SLIs, SLOs, alertas, recuperação e prontidão operacional. |

---

## 16. Relação com SBBs

A arquitetura alvo materializa os SBBs definidos em `04-blocos-de-solucao.md`.

Principais SBBs usados:

```text
- Ledger.Api
- Ledger Database
- Entries
- Input Idempotency
- Outbox
- Ledger.OutboxPublisher
- Message Broker
- Consolidation.Worker
- Consolidation Database
- Processed Events
- DailyBalance
- Consolidation.Api
- API Contracts
- Authentication and Authorization
- Service-to-Service Security
- Observability
- Operational Recovery
- Containers and Local Runtime
- Configuration and Secrets
```

---

## 17. Relação com diagramas

Os diagramas da solução serão detalhados em `06-diagramas.md`.

Este documento deve ser refletido nos seguintes diagramas:

```text
- C4 Context
- C4 Container
- C4 Component quando aplicável
- fluxo de registro de lançamento
- fluxo de publicação via Outbox
- fluxo de consolidação
- fluxo de consulta do consolidado
- visão operacional local
```

---

## 18. Status

Documento em rascunho até a consolidação dos diagramas, segurança, operação e implementação.
