---
doc_id: ARCH-007
titulo: Rastreabilidade
versao: 1.0
status: Atualizado
responsavel: Arquitetura de Soluções
ultima_atualizacao: 2026-07-12
etapa_relacionada: Definition and Decision
---

# Rastreabilidade

## 1. Objetivo

Este documento consolida a rastreabilidade entre requisitos, ASRs, ABBs, ADRs, SBBs, testes e evidências de implementação.

A rastreabilidade demonstra como os requisitos do desafio foram transformados em decisões arquiteturais, blocos de arquitetura, blocos de solução e critérios de validação.

---

## 2. Cadeia de rastreabilidade

A documentação segue a seguinte cadeia:

```text
Requisito
-> ASR
-> ABB
-> ADR
-> SBB
-> Teste
```

Essa cadeia reduz decisões implícitas e facilita revisão técnica da solução.

---

## 3. Documentos base

| Documento | Papel na rastreabilidade |
|---|---|
| `01-contexto-de-negocio.md` | Define problema, capacidades, requisitos funcionais e semântica de negócio. |
| `02-requisitos-arquiteturais.md` | Define RNFs, ASRs, cenários de qualidade e critérios arquiteturais de aceitação. |
| `03-blocos-de-arquitetura.md` | Define ABBs necessários para atender aos ASRs. |
| `04-blocos-de-solucao.md` | Define SBBs que materializam os ABBs. |
| `05-arquitetura-da-solucao.md` | Consolida a arquitetura alvo, fluxos, consistência, falhas, escala e recuperação. |
| `06-diagramas.md` | Representa visualmente contexto, containers, componentes e fluxos. |
| `docs/decisions/` | Registra as decisões arquiteturais que sustentam a solução. |

---

## 4. Rastreabilidade de capacidades para requisitos funcionais

| Capacidade | Requisitos funcionais relacionados |
|---|---|
| CAP-001 — Registrar lançamentos | RF-001, RF-002, RF-003, RF-004, RF-009 |
| CAP-002 — Preservar histórico de lançamentos | RF-008 |
| CAP-003 — Consolidar movimentações diárias | RF-006, RF-008 |
| CAP-004 — Consultar consolidado diário | RF-005, RF-006 |
| CAP-005 — Disponibilizar relatório diário | RF-005, RF-006, RF-007 |
| CAP-006 — Rastrear movimentações | RF-004, RF-008, RF-009 |
| CAP-007 — Recuperar visão consolidada | RF-008 |

---

## 5. Rastreabilidade de RNFs e exigências para ASRs

| Origem | ASRs relacionados |
|---|---|
| RNF-001 — Lançamentos não deve ficar indisponível caso Consolidado falhe | ASR-001, ASR-004, ASR-005, ASR-011 |
| RNF-002 — Consolidado recebe 50 RPS em pico | ASR-002, ASR-008 |
| RNF-003 — Consolidado deve limitar falhas ou perdas a no máximo 5% no pico | ASR-003, ASR-010 |
| Natureza financeira da solução | ASR-004, ASR-006, ASR-007 |
| Segurança obrigatória | ASR-009 |
| Operação e observabilidade obrigatórias | ASR-010, ASR-011 |
| Decisões arquiteturais justificadas | ASR-012 |

---

## 6. Rastreabilidade de ASRs para ABBs

| ASR | ABBs relacionados |
|---|---|
| ASR-001 — Lançamentos deve continuar disponível mesmo se Consolidado falhar | ABB-001, ABB-007, ABB-008 |
| ASR-002 — Consolidado deve suportar 50 RPS em pico | ABB-010, ABB-011, ABB-012 |
| ASR-003 — Consolidado deve limitar falhas ou perdas a no máximo 5% no pico | ABB-010, ABB-012, ABB-013 |
| ASR-004 — Lançamentos registrados devem permanecer confiáveis | ABB-002, ABB-003, ABB-005, ABB-006 |
| ASR-005 — Consolidado pode ficar temporariamente defasado, desde que observável e recuperável | ABB-007, ABB-009, ABB-010, ABB-013, ABB-014 |
| ASR-006 — Requisições repetidas não devem criar duplicidade indevida | ABB-004 |
| ASR-007 — Eventos duplicados não devem duplicar efeitos no Consolidado | ABB-009 |
| ASR-008 — Consulta do Consolidado deve usar estrutura adequada para leitura | ABB-010, ABB-011, ABB-012 |
| ASR-009 — Acesso aos dados deve respeitar comerciante autenticado e autorizado | ABB-015, ABB-016 |
| ASR-010 — Fluxo deve ser observável | ABB-013 |
| ASR-011 — Falhas devem ser recuperáveis | ABB-006, ABB-014 |
| ASR-012 — Decisões relevantes devem ser registradas em ADRs | ADRs em `docs/decisions/` |

---

## 7. Rastreabilidade de ABBs para ADRs

| ABB | ADRs relacionados |
|---|---|
| ABB-001 — Fronteira de Lançamentos | ADR-0001, ADR-0008 |
| ABB-002 — Fonte de Verdade Financeira | ADR-0001, ADR-0005, ADR-0006 |
| ABB-003 — Persistência Transacional de Lançamentos | ADR-0002, ADR-0005, ADR-0006 |
| ABB-004 — Idempotência de Entrada | ADR-0006 |
| ABB-005 — Outbox Durável | ADR-0002, ADR-0006 |
| ABB-006 — Publicação Recuperável | ADR-0002, ADR-0007, ADR-0008 |
| ABB-007 — Canal Assíncrono Confiável | ADR-0001, ADR-0007 |
| ABB-008 — Fronteira de Consolidado | ADR-0001, ADR-0008 |
| ABB-009 — Consumo Idempotente | ADR-0003, ADR-0006 |
| ABB-010 — Projeção Materializada do Consolidado | ADR-0000, ADR-0004, ADR-0006 |
| ABB-011 — Persistência do Consolidado | ADR-0005, ADR-0006 |
| ABB-012 — API de Consulta do Consolidado | ADR-0004, ADR-0008, ADR-0009 |
| ABB-013 — Observabilidade do Fluxo | ADR-0008, ADR-0009, ADR-0010, ADR-0012, ADR-0014 |
| ABB-014 — Recuperação Operacional | ADR-0002, ADR-0003, ADR-0007, ADR-0010, ADR-0012 |
| ABB-015 — Segurança de Acesso | ADR-0010, ADR-0011 |
| ABB-016 — Controle de Comunicação entre Serviços | ADR-0010, ADR-0011 |

---

## 8. Rastreabilidade de ADRs para SBBs

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

---

## 9. Rastreabilidade de ASRs para testes e validações

| ASR | Testes ou validações | Status |
|---|---|---|
| ASR-001 | Testes do Ledger write path e separação assíncrona via Outbox/RabbitMQ; `POST /entries` não chama o Consolidado de forma síncrona. | Implementado em testes automatizados e arquitetura |
| ASR-002 | Teste de carga local/container-first no endpoint de consulta do Consolidado para 50 RPS. | Validado localmente/container-first |
| ASR-003 | Medição de taxa de falhas no pico de consulta do Consolidado pelo teste de carga. | Validado localmente/container-first |
| ASR-004 | Testes de persistência de Entry, InputIdempotency e Outbox na transação local do Ledger. | Implementado |
| ASR-005 | Fluxo assíncrono, Outbox, worker e evidência end-to-end local com atualização eventual do DailyBalance. | Implementado localmente |
| ASR-006 | Testes de repetição com mesma chave de idempotência e conflito para payload divergente. | Implementado |
| ASR-007 | Testes de deduplicação por `ProcessedEvent` e reentrega sem duplicar saldo consolidado. | Implementado |
| ASR-008 | Testes de consulta por comerciante e data usando DailyBalance. | Implementado |
| ASR-009 | Testes de autenticação, autorização por `merchant_id` e bloqueio de consulta cruzada. | Implementado |
| ASR-010 | Baseline local de logs, métricas, traces, correlationId, OpenTelemetry e Aspire Dashboard. | Implementado localmente |
| ASR-011 | Retry local finito e DLQ local do Consolidado implementados; re-drive assistido da DLQ e rebuild operacional completo permanecem pendentes. | Parcialmente implementado |
| ASR-012 | ADRs e vínculos com SBBs documentados. | Documentado |

---

## 10. Rastreabilidade dos fluxos principais

| Fluxo | Documentos relacionados | ADRs relacionados |
|---|---|---|
| Registro de lançamento | `05-arquitetura-da-solucao.md`, `06-diagramas.md` | ADR-0001, ADR-0002, ADR-0005, ADR-0006, ADR-0008, ADR-0009 |
| Publicação via Outbox | `05-arquitetura-da-solucao.md`, `06-diagramas.md` | ADR-0002, ADR-0007, ADR-0008 |
| Consolidação | `05-arquitetura-da-solucao.md`, `06-diagramas.md` | ADR-0003, ADR-0004, ADR-0005, ADR-0006, ADR-0007, ADR-0008 |
| Consulta do consolidado | `05-arquitetura-da-solucao.md`, `06-diagramas.md` | ADR-0000, ADR-0004, ADR-0008, ADR-0009 |
| Recuperação operacional | `05-arquitetura-da-solucao.md`, `docs/operations/` | ADR-0002, ADR-0003, ADR-0007, ADR-0010 |
| Execução local | `04-blocos-de-solucao.md`, `06-diagramas.md` | ADR-0008, ADR-0009, ADR-0010 |

---

## 11. Cobertura dos requisitos obrigatórios do desafio

| Exigência | Onde está coberta | Status |
|---|---|---|
| Domínios, capacidades e limites | `01-contexto-de-negocio.md`, `03-blocos-de-arquitetura.md`, `05-arquitetura-da-solucao.md` | Documentado |
| Requisitos funcionais e não funcionais | `01-contexto-de-negocio.md`, `02-requisitos-arquiteturais.md` | Documentado |
| Arquitetura alvo | `05-arquitetura-da-solucao.md` | Documentado |
| Diagramas | `06-diagramas.md` | Documentado |
| Decisões arquiteturais | `docs/decisions/` | Documentado |
| Segurança | `docs/security/arquitetura-de-seguranca.md` | Documentado |
| Operação e monitoramento | `docs/operations/arquitetura-operacional.md`, `docs/operations/observabilidade-sli-slo-e-recuperacao.md` | Documentado |
| Estimativa de custos | `docs/operations/estimativa-de-custos.md` | Documentado |
| Implementação | Código da solução | Implementado para o escopo local do desafio; pendências produtivas preservadas |
| Testes automatizados | Testes da solução | Implementado para contratos, Ledger, Outbox, Consolidado, APIs e rate limiting |
| Deploy e execução local | `ADR-0010`, documentação operacional e arquivos de execução | Execução local/container-first implementada; deploy produtivo/IaC pendente |

---

## 12. Itens pendentes produtivos preservados

Os seguintes itens continuam fora do escopo implementado e devem ser tratados em evolução produtiva:

```text
- rate limiting distribuído/produtivo
- re-drive assistido da DLQ
- rebuild/reprocessamento operacional completo
- observabilidade produtiva completa
- dashboards e alertas produtivos
- retenção centralizada de logs
- IaC/deploy produtivo
- hardening produtivo de identidade
- validação produtiva de múltiplos workers, backlog e autoscaling
- multi-publisher seguro
```

---

## 13. Status

Documento atualizado com o estado implementado local/container-first. A solução ainda não declara prontidão produtiva.

---

## Contratos de implementação

| Contrato | Finalidade | Decisão relacionada |
|---|---|---|
| `contracts/openapi.yaml` | Define os contratos HTTP iniciais de `POST /entries` e `GET /daily-balances/{businessDate}`. | ADR-0013 |
| `contracts/events/entry-created-v1.schema.json` | Define o evento assíncrono `EntryCreated.v1` usado entre Lançamentos e Consolidado. | ADR-0013 |

Os contratos conectam a arquitetura documental à implementação, aos testes de integração e à validação dos requisitos não funcionais.
