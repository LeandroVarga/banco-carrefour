---
doc_id: ARCH-002
titulo: Requisitos Arquiteturais
versao: 1.0
status: Atualizado
responsavel: Arquitetura de Soluções
ultima_atualizacao: 2026-07-10
etapa_relacionada: Discovery and Definition
---

# Requisitos Arquiteturais

## 1. Objetivo

Este documento consolida os requisitos arquiteturais da solução, separando os requisitos não funcionais explicitamente informados no desafio, as exigências obrigatórias da entrega e os ASRs derivados da análise.

Os itens deste documento servem como base para os blocos de arquitetura, decisões arquiteturais, arquitetura da solução, segurança, operação e testes.

---

## 2. Classificação dos requisitos

Os requisitos arquiteturais foram organizados em três grupos:

| Grupo | Descrição |
|---|---|
| RNFs explícitos | Requisitos não funcionais informados diretamente no desafio. |
| Exigências obrigatórias | Itens que o desafio exige para arquitetura, segurança, operação, documentação, implementação e comunicação técnica. |
| ASRs | Requisitos arquiteturalmente significativos que direcionam decisões relevantes da solução. |

---

## 3. RNFs explícitos do desafio

A seção de requisitos não funcionais do desafio informa diretamente os seguintes pontos:

| ID | RNF explícito | Interpretação arquitetural |
|---|---|---|
| RNF-001 | O serviço de controle de lançamentos não deve ficar indisponível caso o serviço de consolidado diário falhe. | O registro de lançamentos deve ser isolado da disponibilidade do Consolidado. |
| RNF-002 | Em picos, o serviço de consolidado recebe 50 requisições por segundo. | A consulta do Consolidado deve suportar o volume de leitura informado para o pico. |
| RNF-003 | Em picos, o serviço de consolidado deve ter no máximo 5% de perda de requisições. | A arquitetura deve permitir medir e controlar a taxa de falha ou perda de requisições do Consolidado durante o pico. |

---

## 4. Exigências obrigatórias da entrega

Além dos RNFs explícitos, o desafio exige que a solução trate os seguintes aspectos:

| Área | Exigências |
|---|---|
| Arquitetura e domínios | Mapear domínios funcionais, capacidades de negócio e limites de responsabilidade. |
| Requisitos | Refinar requisitos funcionais e definir requisitos não funcionais. |
| Arquitetura alvo | Apresentar componentes, responsabilidades, fluxos de comunicação e padrões arquiteturais adotados. |
| Comunicação arquitetural | Apresentar diagramas estruturados, preferencialmente usando C4 Model. |
| Segurança | Definir autenticação, autorização, proteção de APIs, proteção de dados, criptografia, controle de acesso entre serviços e estratégias contra ataques comuns. |
| Operação | Definir deploy, monitoramento, logs, observabilidade, escalabilidade e recuperação de falhas. |
| Implementação | Entregar implementação funcional, testes automatizados e código versionado em repositório público. |
| Decisões | Registrar decisões arquiteturais relevantes com justificativas, alternativas e tradeoffs. |
| Documentação | Organizar a documentação em `/docs/architecture`, `/docs/security`, `/docs/decisions` e `/docs/operations`. |
| Custos e integração | Apresentar estimativa de custos, estratégia de observabilidade e critérios de segurança para integração entre serviços. |

---

## 5. ASRs

ASR significa Architecturally Significant Requirement.

| ID | ASR | Origem | Tipo |
|---|---|---|---|
| ASR-001 | Lançamentos deve continuar disponível mesmo se Consolidado falhar. | RNF-001 | Explícito |
| ASR-002 | Consolidado deve suportar 50 RPS em pico. | RNF-002 | Explícito |
| ASR-003 | Consolidado deve limitar falhas ou perdas de requisições a no máximo 5% no pico. | RNF-003 | Explícito |
| ASR-004 | Lançamentos registrados devem permanecer confiáveis mesmo quando a consolidação falhar temporariamente. | Natureza financeira da solução e RNF-001. | Derivado |
| ASR-005 | Consolidado pode ficar temporariamente defasado, desde que a defasagem seja observável e recuperável. | Separação entre Lançamentos e Consolidado. | Derivado |
| ASR-006 | Tentativas repetidas de registro não devem criar lançamentos duplicados indevidamente. | Controle financeiro e possibilidade de retries. | Derivado |
| ASR-007 | Eventos ou mensagens duplicadas não devem duplicar efeitos no Consolidado. | Recuperação de falhas e comunicação assíncrona. | Derivado |
| ASR-008 | A consulta do Consolidado deve usar uma estrutura adequada para leitura. | RNF-002 e RNF-003. | Derivado |
| ASR-009 | O acesso aos dados deve respeitar o comerciante autenticado e autorizado. | Segurança obrigatória. | Obrigatório |
| ASR-010 | O fluxo de registro, publicação, consumo e consolidação deve ser observável. | Operação e observabilidade obrigatórias. | Obrigatório |
| ASR-011 | Falhas de publicação, consumo ou consolidação devem ser recuperáveis. | Recuperação de falhas obrigatória. | Obrigatório |
| ASR-012 | Decisões arquiteturais relevantes devem ser registradas em ADRs. | Fundamentação das decisões. | Obrigatório |

---

## 6. Cenários de qualidade

Os cenários abaixo tornam os requisitos arquiteturais mais verificáveis.

| ID | Atributo | Cenário | Resposta esperada |
|---|---|---|---|
| QA-001 | Disponibilidade | Consolidado falha durante o registro de lançamentos. | Lançamentos continua aceitando novos registros. |
| QA-002 | Desempenho | Consolidado recebe 50 RPS em pico. | A consulta do Consolidado permanece operacional dentro da tolerância definida. |
| QA-003 | Tolerância a falhas | Durante pico, algumas requisições ao Consolidado falham. | A taxa de falha ou perda fica limitada a no máximo 5% das requisições elegíveis. |
| QA-004 | Consistência eventual | Lançamento foi registrado, mas ainda não apareceu no Consolidado. | A defasagem é temporária, observável e recuperável. |
| QA-005 | Idempotência de entrada | Cliente repete a mesma requisição de lançamento. | A solução não cria duplicidade indevida. |
| QA-006 | Idempotência de consumo | O mesmo evento ou mensagem é processado mais de uma vez. | O saldo consolidado não é impactado mais de uma vez pelo mesmo lançamento. |
| QA-007 | Recuperação | Componente de consolidação falha durante o processamento. | O processamento pode ser retomado sem perda ou duplicidade de efeito financeiro. |
| QA-008 | Segurança | Comerciante tenta consultar dados de outro comerciante. | A requisição é negada por autorização. |
| QA-009 | Observabilidade | Há atraso entre registro e consolidação. | Logs, métricas ou traces permitem identificar atraso, backlog, falha ou necessidade de reprocessamento. |
| QA-010 | Rastreabilidade | Um saldo consolidado precisa ser investigado. | A solução permite relacionar consolidado, lançamentos e processamento correspondente. |

---

## 7. Critérios arquiteturais de aceitação

A arquitetura proposta deve atender aos seguintes critérios:

| ID | Critério |
|---|---|
| CAA-001 | Separar claramente as responsabilidades entre Lançamentos e Consolidado. |
| CAA-002 | Manter Lançamentos como fonte de verdade financeira. |
| CAA-003 | Manter Consolidado como visão derivada. |
| CAA-004 | Evitar dependência síncrona do Consolidado no registro de Lançamentos. |
| CAA-005 | Definir mecanismo recuperável para atualizar o Consolidado a partir dos lançamentos. |
| CAA-006 | Tratar duplicidade de requisições de entrada. |
| CAA-007 | Tratar duplicidade de eventos ou mensagens no processamento do Consolidado. |
| CAA-008 | Permitir consulta eficiente do consolidado diário. |
| CAA-009 | Definir estratégia de autenticação, autorização e proteção de APIs. |
| CAA-010 | Definir proteção de dados sensíveis, criptografia e comunicação segura entre serviços. |
| CAA-011 | Definir estratégia de logs, monitoramento, observabilidade e recuperação de falhas. |
| CAA-012 | Documentar decisões arquiteturais relevantes em ADRs. |
| CAA-013 | Apresentar diagramas estruturados da arquitetura. |
| CAA-014 | Manter rastreabilidade entre requisitos, ASRs, ABBs, ADRs, SBBs e testes. |

---

## 8. Relação com decisões arquiteturais

Os requisitos deste documento direcionam decisões arquiteturais registradas em [registro-de-decisoes.md](../decisions/registro-de-decisoes.md).

Decisões registradas por grupo:

```text
Domínio e fronteiras:
- ADR-0000 -> semântica do consolidado diário
- ADR-0001 -> separação entre Lançamentos e Consolidado

Dados e consistência:
- ADR-0002 -> Outbox e publicação confiável
- ADR-0003 -> consumo at-least-once e idempotente
- ADR-0004 -> projeção materializada do Consolidado
- ADR-0005 -> persistências independentes por fronteira
- ADR-0006 -> persistência relacional e PostgreSQL

Runtime e tecnologia:
- ADR-0007 -> canal assíncrono, broker e RabbitMQ local
- ADR-0008 -> unidades implantáveis
- ADR-0009 -> stack tecnológica da solução
- ADR-0010 -> execução local, AWS como plataforma de referência e portabilidade por papéis

Segurança:
- ADR-0011 -> decisões de segurança

Operação e observabilidade:
- ADR-0012 -> observabilidade e prontidão operacional
- ADR-0014 -> instrumentação de observabilidade com OpenTelemetry

Contratos:
- ADR-0013 -> contratos HTTP e evento EntryCreated.v1
```

---

## 9. Relação com os próximos documentos

Este documento serve como base para:

- [03-blocos-de-arquitetura.md](03-blocos-de-arquitetura.md)
- [04-blocos-de-solucao.md](04-blocos-de-solucao.md)
- [05-arquitetura-da-solucao.md](05-arquitetura-da-solucao.md)
- [docs/security/](../security/)
- [docs/operations/](../operations/)
- [docs/decisions/](../decisions/)

---

## 10. Status

Documento atualizado como baseline de requisitos e ASRs para a implementação local. Pendências produtivas permanecem rastreadas nos documentos de prontidão e operação.
