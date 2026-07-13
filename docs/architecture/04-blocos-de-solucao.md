---
doc_id: ARCH-004
titulo: Blocos de Solução
versao: 1.0
status: Atualizado
responsavel: Arquitetura de Soluções
ultima_atualizacao: 2026-07-10
etapa_relacionada: Definition and Decision
---

# Blocos de Solução

## 1. Objetivo

Este documento define os SBBs, Solution Building Blocks, utilizados para materializar os blocos de arquitetura definidos em `03-blocos-de-arquitetura.md`.

Os SBBs descrevem componentes, tecnologias, bancos, filas, APIs, workers e mecanismos concretos que compõem a solução.

A arquitetura completa da solução será consolidada em `05-arquitetura-da-solucao.md`.

---

## 2. Diretrizes de materialização

A definição dos SBBs segue as decisões arquiteturais já registradas.

Decisões fundacionais:

```text
ADR-0000 -> semântica do consolidado diário
ADR-0001 -> fronteiras entre Lançamentos e Consolidado
ADR-0002 -> Outbox e publicação confiável
ADR-0003 -> consumo at-least-once e idempotente
ADR-0004 -> projeção materializada do Consolidado
```

Decisões de materialização:

```text
ADR-0005 -> persistências independentes por fronteira
ADR-0006 -> persistência relacional e PostgreSQL
ADR-0007 -> canal assíncrono, broker e RabbitMQ local
ADR-0008 -> unidades implantáveis e topologia de runtime
ADR-0009 -> stack tecnológica da solução
ADR-0010 -> execução local, portabilidade cloud e padrões corporativos
```

Essas decisões orientam as seguintes diretrizes:

```text
- manter Lançamentos e Consolidado como fronteiras separadas
- proteger o caminho crítico de registro financeiro
- tratar Lançamentos como fonte de verdade financeira
- tratar Consolidado como visão derivada e reconstruível
- evitar dependência síncrona do Consolidado no registro de Lançamentos
- usar comunicação assíncrona entre as fronteiras
- permitir publicação, consumo e reprocessamento recuperáveis
- tratar duplicidade de entrada e duplicidade de consumo
- preparar o Consolidado para leitura por comerciante e data
- manter observabilidade do fluxo fim a fim
- preservar portabilidade para ambiente cloud ou plataforma corporativa
```

---

## 3. Stack de referência

A stack abaixo representa a materialização de referência para o desafio.

| Camada | SBB de referência | Papel |
|---|---|---|
| Backend | .NET em versão LTS | Implementação das APIs e workers. |
| APIs HTTP | ASP.NET Core | Exposição dos endpoints de Lançamentos e Consolidado. |
| Workers | .NET Worker Service | Execução do Outbox Publisher e do consumidor do Consolidado. |
| Persistência | PostgreSQL | Armazenamento transacional, Outbox e projeções. |
| Mensageria local | RabbitMQ | Canal assíncrono de referência para execução local do desafio. |
| Mensageria cloud | Fila ou broker gerenciado equivalente | Alternativa para ambiente corporativo ou cloud. |
| Containers | Docker | Empacotamento das unidades implantáveis. |
| Execução local | Docker Compose | Orquestração local para avaliação e testes. |
| Contratos | OpenAPI | Documentação dos contratos HTTP. |
| Observabilidade | Logs estruturados, métricas e traces | Diagnóstico e operação do fluxo. |

As escolhas de persistência, mensageria, unidades implantáveis, stack tecnológica, execução local e portabilidade são sustentadas pelos ADRs de materialização. Decisões específicas de segurança e observabilidade serão detalhadas em documentos próprios.

---

## 4. Unidades implantáveis

A solução é composta por quatro unidades implantáveis principais.

| Unidade | Tipo | Responsabilidade |
|---|---|---|
| Ledger.Api | API | Registrar lançamentos financeiros. |
| Ledger.OutboxPublisher | Worker | Publicar eventos pendentes da Outbox. |
| Consolidation.Worker | Worker | Consumir eventos e atualizar a projeção consolidada. |
| Consolidation.Api | API | Consultar o consolidado diário. |

Essa separação permite operar, escalar e recuperar partes do fluxo de forma independente.

---

## 5. SBBs principais

| ID | SBB | Materializa | Responsabilidade principal |
|---|---|---|---|
| SBB-001 | Ledger.Api | Fronteira de Lançamentos | Receber, validar e registrar lançamentos. |
| SBB-002 | Ledger Database | Fonte de verdade financeira | Armazenar lançamentos, idempotência e Outbox. |
| SBB-003 | Entries | Lançamentos financeiros | Persistir créditos e débitos registrados. |
| SBB-004 | Input Idempotency | Idempotência de entrada | Controlar requisições repetidas. |
| SBB-005 | Outbox | Outbox durável | Registrar eventos pendentes de publicação. |
| SBB-006 | Ledger.OutboxPublisher | Publicação recuperável | Publicar eventos da Outbox no broker. |
| SBB-007 | Message Broker | Canal assíncrono confiável | Transportar eventos entre Lançamentos e Consolidado. |
| SBB-008 | Consolidation.Worker | Consumo do Consolidado | Processar eventos e atualizar projeções. |
| SBB-009 | Consolidation Database | Persistência do Consolidado | Armazenar projeções e eventos processados. |
| SBB-010 | Processed Events | Consumo idempotente | Evitar duplicidade de efeito no Consolidado. |
| SBB-011 | DailyBalance | Projeção materializada | Armazenar o consolidado diário por comerciante e data. |
| SBB-012 | Consolidation.Api | API de consulta | Expor consulta do relatório diário consolidado. |
| SBB-013 | API Contracts | Contratos de integração | Padronizar endpoints, payloads e respostas. |
| SBB-014 | Authentication and Authorization | Segurança de acesso | Proteger APIs e acesso por comerciante. |
| SBB-015 | Service-to-Service Security | Comunicação entre serviços | Proteger credenciais, permissões e integrações internas. |
| SBB-016 | Observability | Observabilidade do fluxo | Registrar logs, métricas, traces e correlação. |
| SBB-017 | Operational Recovery | Recuperação operacional | Permitir retry, isolamento, reprocessamento e reconstrução. |
| SBB-018 | Containers and Local Runtime | Runtime local | Executar a solução de forma reproduzível. |
| SBB-019 | Configuration and Secrets | Configuração | Centralizar parâmetros e valores sensíveis. |

---

## 6. Persistência de Lançamentos

A fronteira de Lançamentos utiliza uma persistência própria.

Componentes principais:

```text
Ledger Database
├── Entries
├── Input Idempotency
└── Outbox
```

Responsabilidades:

```text
- persistir lançamentos financeiros
- manter dados de rastreabilidade
- controlar idempotência de entrada
- registrar intenção de publicação na Outbox
- permitir retomada da publicação após falhas
```

O registro do lançamento e a intenção de publicação devem ocorrer de forma consistente dentro da fronteira de Lançamentos.

---

## 7. Persistência do Consolidado

A fronteira de Consolidado utiliza uma persistência própria para leitura e controle de processamento.

Componentes principais:

```text
Consolidation Database
├── DailyBalance
└── Processed Events
```

Responsabilidades:

```text
- armazenar a projeção diária por comerciante e data
- controlar eventos já processados
- evitar duplicidade de efeito no saldo consolidado
- permitir consulta eficiente do relatório diário
- apoiar reconstrução da visão consolidada
```

O Consolidado permanece uma visão derivada. A fonte de verdade financeira continua em Lançamentos.

---

## 8. Comunicação assíncrona

A comunicação entre Lançamentos e Consolidado é materializada por um canal assíncrono.

Fluxo principal:

```text
Ledger.Api
-> Ledger Database / Outbox
-> Ledger.OutboxPublisher
-> Message Broker
-> Consolidation.Worker
-> Consolidation Database / DailyBalance
-> Consolidation.Api
```

No ambiente local, o canal assíncrono pode ser materializado com RabbitMQ.

Em ambiente cloud ou corporativo, pode ser substituído por fila ou broker gerenciado equivalente, mantendo o mesmo papel arquitetural.

---

## 9. Contratos principais

Os contratos iniciais da solução são:

```text
POST /entries
GET /daily-balances/{businessDate}
```

A identificação do comerciante deve ser obtida do contexto autenticado. Endpoints administrativos ou internos podem incluir o comerciante explicitamente, desde que passem por autorização adequada.

O contrato de registro de lançamento deve contemplar, no mínimo:

```text
- identificação do comerciante obtida do contexto autenticado ou validada contra ele
- tipo do lançamento
- valor
- moeda
- data de ocorrência
- chave de idempotência
- identificadores de rastreabilidade
```

O contrato de consulta do consolidado deve retornar, no mínimo:

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

## 10. Observabilidade e recuperação

A solução deve expor sinais operacionais nos principais pontos do fluxo.

Sinais principais:

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
```

Mecanismos de recuperação:

```text
- retry de publicação
- retry de consumo
- isolamento de mensagens com falha persistente
- reprocessamento controlado
- reconstrução da projeção DailyBalance a partir dos lançamentos
```

---

## 11. Segurança

A solução deve materializar segurança em dois níveis.

Segurança de acesso externo:

```text
- autenticação das chamadas externas
- autorização por comerciante
- proteção contra consulta cruzada entre comerciantes
- validação de entrada
- rate limit e proteções contra abuso
```

Segurança interna:

```text
- menor privilégio entre componentes, com segregação completa de credenciais como requisito produtivo
- acesso restrito aos bancos
- acesso restrito ao broker
- proteção da comunicação entre serviços
- uso de secrets por ambiente
```

O detalhamento completo será tratado em `docs/security/arquitetura-de-seguranca.md`.

---

## 12. Portabilidade local e cloud

A solução separa materialização local de equivalentes corporativos ou cloud.

| Necessidade | Execução local | Ambiente cloud/corporativo |
|---|---|---|
| APIs e workers | Containers Docker | Plataforma de containers ou serviço gerenciado equivalente. |
| Banco de dados | PostgreSQL em container | PostgreSQL gerenciado ou banco relacional equivalente. |
| Mensageria | RabbitMQ em container | Fila ou broker gerenciado equivalente. |
| Secrets | Variáveis de ambiente | Secret manager corporativo ou cloud. |
| Observabilidade | Logs e métricas locais | Stack corporativa ou cloud de logs, métricas e traces. |
| Autenticação | Representação simplificada para avaliação | Provedor de identidade corporativo. |

Essa abordagem mantém a solução executável no desafio sem amarrar a arquitetura a um único provedor.

---

## 13. Mapeamento ABB para SBB

| ABB | SBBs relacionados |
|---|---|
| ABB-001 — Fronteira de Lançamentos | SBB-001 |
| ABB-002 — Fonte de Verdade Financeira | SBB-002, SBB-003 |
| ABB-003 — Persistência Transacional de Lançamentos | SBB-002, SBB-003, SBB-005 |
| ABB-004 — Idempotência de Entrada | SBB-004 |
| ABB-005 — Outbox Durável | SBB-005 |
| ABB-006 — Publicação Recuperável | SBB-006 |
| ABB-007 — Canal Assíncrono Confiável | SBB-007 |
| ABB-008 — Fronteira de Consolidado | SBB-008, SBB-012 |
| ABB-009 — Consumo Idempotente | SBB-010 |
| ABB-010 — Projeção Materializada do Consolidado | SBB-011 |
| ABB-011 — Persistência do Consolidado | SBB-009, SBB-010, SBB-011 |
| ABB-012 — API de Consulta do Consolidado | SBB-012, SBB-013 |
| ABB-013 — Observabilidade do Fluxo | SBB-016 |
| ABB-014 — Recuperação Operacional | SBB-006, SBB-017 |
| ABB-015 — Segurança de Acesso | SBB-014 |
| ABB-016 — Controle de Comunicação entre Serviços | SBB-015, SBB-019 |

---

## 14. Mapeamento SBB para ADR

| ADR | SBBs sustentados |
|---|---|
| ADR-0000 — Semântica do consolidado diário | SBB-011, SBB-012 |
| ADR-0001 — Fronteiras entre Lançamentos e Consolidado | SBB-001, SBB-008, SBB-012 |
| ADR-0002 — Outbox e publicação confiável | SBB-002, SBB-005, SBB-006 |
| ADR-0003 — Consumo at-least-once e idempotente | SBB-007, SBB-008, SBB-010, SBB-017 |
| ADR-0004 — Projeção materializada do Consolidado | SBB-009, SBB-011, SBB-012 |
| ADR-0005 — Persistências independentes por fronteira | SBB-002, SBB-003, SBB-005, SBB-009, SBB-010, SBB-011 |
| ADR-0006 — Persistência relacional e PostgreSQL | SBB-002, SBB-003, SBB-004, SBB-005, SBB-009, SBB-010, SBB-011 |
| ADR-0007 — Canal assíncrono, broker e RabbitMQ local | SBB-006, SBB-007, SBB-008, SBB-017 |
| ADR-0008 — Unidades implantáveis e topologia de runtime | SBB-001, SBB-006, SBB-008, SBB-012, SBB-018 |
| ADR-0009 — Stack tecnológica da solução | SBB-001, SBB-006, SBB-008, SBB-012, SBB-013, SBB-016, SBB-018, SBB-019 |
| ADR-0010 — Execução local, portabilidade cloud e padrões corporativos | SBB-001, SBB-002, SBB-006, SBB-007, SBB-008, SBB-009, SBB-012, SBB-014, SBB-015, SBB-016, SBB-017, SBB-018, SBB-019 |
| ADR-0011 — Decisões de segurança | SBB-014, SBB-015, SBB-019 |
| ADR-0012 — Observabilidade e prontidão operacional | SBB-016, SBB-017, SBB-018, SBB-019 |
| ADR-0013 — Contratos HTTP e Evento EntryCreated.v1 | SBB-013 |
| ADR-0014 — Instrumentação de observabilidade com OpenTelemetry | SBB-016, SBB-018 |

Decisões de segurança, observabilidade e prontidão operacional estão registradas em ADRs específicos.

---

## 15. Relação com os próximos documentos

Este documento materializa os ABBs em blocos concretos de solução.

A composição desses blocos está detalhada em:

```text
- 05-arquitetura-da-solucao.md
- 06-diagramas.md
- docs/decisions/
- docs/security/
- docs/operations/
```

---

## 16. Status

Documento atualizado como baseline dos SBBs materializados ou planejados para evolução produtiva.
