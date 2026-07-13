---
doc_id: ARCH-003
titulo: Blocos de Arquitetura
versao: 1.0
status: Atualizado
responsavel: Arquitetura de Soluções
ultima_atualizacao: 2026-07-10
etapa_relacionada: Definition and Decision
---

# Blocos de Arquitetura

## 1. Objetivo

Este documento define os ABBs, Architecture Building Blocks, necessários para atender aos requisitos arquiteturais da solução.

Os ABBs representam capacidades arquiteturais que a solução precisa possuir, sem definir ainda tecnologias, produtos, frameworks ou serviços específicos.

A escolha das tecnologias correspondentes será tratada em `04-blocos-de-solucao.md`.

Neste case, AWS não aparece dentro dos ABBs. A AWS aparece na transição para SBBs, quando os papéis arquiteturais são materializados em componentes, tecnologias e serviços de referência.

---

## 2. Relação com os requisitos arquiteturais

Os ABBs deste documento derivam principalmente dos seguintes direcionadores:

```text
- Lançamentos deve continuar disponível mesmo se Consolidado falhar
- Consolidado deve suportar 50 RPS em pico
- Consolidado deve limitar falhas ou perdas de requisições a no máximo 5% no pico
- Lançamentos registrados devem permanecer confiáveis
- Consolidado pode ficar temporariamente defasado, desde que observável e recuperável
- Tentativas repetidas de registro não devem gerar duplicidade indevida
- Eventos ou mensagens duplicadas não devem duplicar efeitos no Consolidado
- APIs, dados e comunicação entre serviços devem ser protegidos
- O fluxo deve ser observável e recuperável
```

---

## 3. Visão geral dos ABBs

| ID | ABB | Papel arquitetural |
|---|---|---|
| ABB-001 | Fronteira de Lançamentos | Isolar a responsabilidade de registro financeiro. |
| ABB-002 | Fonte de Verdade Financeira | Preservar lançamentos como base confiável da solução. |
| ABB-003 | Persistência Transacional de Lançamentos | Garantir gravação consistente dos lançamentos. |
| ABB-004 | Idempotência de Entrada | Evitar duplicidade indevida em requisições repetidas. |
| ABB-005 | Outbox Durável | Registrar eventos a publicar junto da transação de negócio. |
| ABB-006 | Publicação Recuperável | Publicar eventos de forma retomável após falhas. |
| ABB-007 | Canal Assíncrono Confiável | Desacoplar Lançamentos e Consolidado. |
| ABB-008 | Fronteira de Consolidado | Isolar a responsabilidade de leitura consolidada. |
| ABB-009 | Consumo Idempotente | Evitar duplicidade de efeito no processamento. |
| ABB-010 | Projeção Materializada do Consolidado | Manter visão de leitura otimizada para consulta. |
| ABB-011 | Persistência do Consolidado | Armazenar a visão consolidada de forma independente. |
| ABB-012 | API de Consulta do Consolidado | Expor consulta do saldo diário consolidado. |
| ABB-013 | Observabilidade do Fluxo | Permitir diagnóstico de registro, publicação, consumo e consulta. |
| ABB-014 | Recuperação Operacional | Permitir retry, reprocessamento e reconstrução. |
| ABB-015 | Segurança de Acesso | Proteger APIs, dados e acesso por comerciante. |
| ABB-016 | Controle de Comunicação entre Serviços | Proteger integrações internas da solução. |

---

## 4. Detalhamento dos ABBs

### ABB-001 — Fronteira de Lançamentos

Representa a fronteira responsável pelo controle de lançamentos.

Responsabilidades:

```text
- receber comandos de registro de crédito e débito
- validar dados básicos do lançamento
- aplicar regra de idempotência de entrada
- persistir o lançamento
- produzir informação para atualização posterior do Consolidado
```

Esta fronteira protege o caminho crítico de escrita financeira.

---

### ABB-002 — Fonte de Verdade Financeira

Representa a base confiável dos lançamentos financeiros.

Responsabilidades:

```text
- manter lançamentos registrados
- preservar histórico de movimentações
- permitir rastreabilidade
- servir como origem para reconstrução do Consolidado
```

O Consolidado não substitui a fonte de verdade financeira. Ele é uma visão derivada dela.

---

### ABB-003 — Persistência Transacional de Lançamentos

Representa a capacidade de gravar lançamentos de forma consistente.

Responsabilidades:

```text
- gravar o lançamento financeiro
- associar o lançamento ao comerciante
- registrar dados de rastreabilidade
- garantir que o registro do lançamento e a intenção de publicação sejam persistidos de forma consistente
```

Esse bloco é necessário para impedir que uma falha no fluxo de consolidação comprometa o registro financeiro.

---

### ABB-004 — Idempotência de Entrada

Representa a capacidade de tratar requisições repetidas de registro.

Responsabilidades:

```text
- identificar tentativas repetidas
- evitar duplicidade indevida de lançamentos
- retornar resposta consistente para repetição equivalente
- detectar repetição com payload divergente quando aplicável
```

Esse bloco reduz riscos causados por retries de clientes, timeouts e falhas intermitentes.

---

### ABB-005 — Outbox Durável

Representa a capacidade de registrar eventos a publicar junto da transação de negócio.

Responsabilidades:

```text
- registrar evento de lançamento criado
- manter evento pendente de publicação
- permitir retomada após falhas
- evitar perda silenciosa entre persistência e publicação
```

Esse bloco conecta a gravação confiável do lançamento com a comunicação assíncrona.

---

### ABB-006 — Publicação Recuperável

Representa a capacidade de publicar eventos pendentes de forma segura e retomável.

Responsabilidades:

```text
- buscar eventos pendentes
- publicar eventos no canal assíncrono
- marcar eventos publicados
- aplicar retry em falhas temporárias
- expor falhas persistentes para operação
```

Esse bloco evita que a disponibilidade do canal assíncrono seja requisito para registrar lançamentos.

---

### ABB-007 — Canal Assíncrono Confiável

Representa o meio de comunicação entre Lançamentos e Consolidado.

Responsabilidades:

```text
- transportar eventos de lançamento
- desacoplar produtor e consumidor
- permitir entrega posterior quando o consumidor estiver indisponível
- apoiar retries e controle de falhas
```

Esse bloco permite que o Consolidado falhe sem indisponibilizar Lançamentos.

---

### ABB-008 — Fronteira de Consolidado

Representa a fronteira responsável pela leitura consolidada.

Responsabilidades:

```text
- consumir eventos de lançamentos
- atualizar a visão consolidada
- expor consulta do consolidado diário
- manter a responsabilidade de leitura separada da escrita financeira
```

Essa fronteira concentra a visão derivada e otimizada para consulta.

---

### ABB-009 — Consumo Idempotente

Representa a capacidade de processar eventos sem duplicar efeitos.

Responsabilidades:

```text
- identificar eventos já processados
- impedir reaplicação do mesmo lançamento
- permitir reprocessamento seguro
- manter rastreabilidade do processamento
```

Esse bloco é necessário porque fluxos assíncronos recuperáveis podem entregar a mesma mensagem mais de uma vez.

No escopo inicial, a consolidação não depende de ordenação global dos eventos, pois o saldo diário é calculado a partir da aplicação idempotente de lançamentos imutáveis.

---

### ABB-010 — Projeção Materializada do Consolidado

Representa a visão calculada para consulta do saldo diário.

Responsabilidades:

```text
- manter total de créditos por comerciante e data
- manter total de débitos por comerciante e data
- manter saldo diário
- manter quantidade de lançamentos considerados
- manter data e hora da última atualização
- indicar status da consolidação quando aplicável
```

Esse bloco evita que a consulta precise recalcular o consolidado a partir de todos os lançamentos a cada requisição.

---

### ABB-011 — Persistência do Consolidado

Representa o armazenamento da visão consolidada.

Responsabilidades:

```text
- armazenar projeções consolidadas
- permitir leitura por comerciante e data
- preservar informações de processamento
- apoiar reconstrução da visão quando necessário
```

Essa persistência é separada da fonte de verdade financeira.

---

### ABB-012 — API de Consulta do Consolidado

Representa a interface de leitura do relatório diário consolidado.

Responsabilidades:

```text
- consultar consolidado por comerciante e data
- retornar totais, saldo e metadados da consolidação
- aplicar autorização por comerciante
- responder ao pico informado de 50 RPS
```

Esse bloco atende diretamente à necessidade de consulta do relatório diário.

---

### ABB-013 — Observabilidade do Fluxo

Representa a capacidade de acompanhar o comportamento da solução.

Responsabilidades:

```text
- registrar logs estruturados
- expor métricas de APIs, publicação, consumo e consulta
- correlacionar requisições e eventos
- monitorar backlog, lag, erros e mensagens isoladas para análise
- permitir diagnóstico operacional
```

Esse bloco sustenta operação, monitoramento e investigação de falhas.

---

### ABB-014 — Recuperação Operacional

Representa a capacidade de recuperar falhas no fluxo.

Responsabilidades:

```text
- aplicar retry em falhas temporárias
- isolar falhas persistentes
- permitir reprocessamento de eventos
- permitir reconstrução da projeção consolidada
- apoiar investigação operacional e retomada segura do processamento
```

Esse bloco permite que falhas de publicação, consumo ou consolidação sejam tratadas sem perda de controle.

---

### ABB-015 — Segurança de Acesso

Representa a proteção de usuários, APIs e dados sensíveis.

Responsabilidades:

```text
- autenticar chamadas externas
- autorizar acesso por comerciante
- proteger dados sensíveis
- aplicar validação de entrada
- aplicar rate limit e proteções contra abuso
```

Esse bloco atende aos requisitos obrigatórios de segurança do desafio.

---

### ABB-016 — Controle de Comunicação entre Serviços

Representa a proteção das integrações internas da solução.

Responsabilidades:

```text
- controlar permissões entre componentes internos
- proteger comunicação entre serviços
- limitar acesso aos recursos necessários
- evitar exposição indevida de componentes internos
```

Esse bloco complementa a segurança externa com segurança interna entre serviços.

---

## 5. Relação entre ASRs e ABBs

| ASR | ABBs relacionados |
|---|---|
| ASR-001 | ABB-001, ABB-007, ABB-008 |
| ASR-002 | ABB-010, ABB-011, ABB-012 |
| ASR-003 | ABB-010, ABB-012, ABB-013 |
| ASR-004 | ABB-002, ABB-003, ABB-005, ABB-006 |
| ASR-005 | ABB-007, ABB-009, ABB-010, ABB-013, ABB-014 |
| ASR-006 | ABB-004 |
| ASR-007 | ABB-009 |
| ASR-008 | ABB-010, ABB-011, ABB-012 |
| ASR-009 | ABB-015 |
| ASR-010 | ABB-013 |
| ASR-011 | ABB-006, ABB-014 |
| ASR-012 | ADRs em `docs/decisions/` |

---

## 6. Decisões arquiteturais relacionadas

Os ABBs definidos neste documento são sustentados por decisões arquiteturais registradas em ADRs.

Decisões já registradas:

```text
ADR-0000 -> semântica do consolidado diário
ADR-0001 -> fronteiras entre Lançamentos e Consolidado
ADR-0002 -> Outbox e publicação confiável
ADR-0003 -> consumo at-least-once e idempotente
ADR-0004 -> projeção materializada do Consolidado
```

As decisões complementares estão registradas em `docs/decisions/registro-de-decisoes.md` e sustentam os blocos de solução, persistência, mensageria, runtime, segurança e operação.

---

## 7. Relação com os próximos documentos

Este documento define os blocos arquiteturais necessários.

A materialização desses blocos em tecnologias, produtos e componentes será tratada em:

```text
- 04-blocos-de-solucao.md
- 05-arquitetura-da-solucao.md
- docs/decisions/
- docs/security/
- docs/operations/
```

---

## 8. Transição para SBBs

Os ABBs permanecem independentes de tecnologia.

A transição para SBBs segue esta sequência:

```text
ABB sem tecnologia
-> SBB com componente e responsabilidade concreta
-> materialização local reproduzível
-> materialização AWS de referência do case
```

A escolha da AWS como plataforma de referência ocorre nessa transição e está registrada na ADR-0010. Essa escolha não afirma plataforma real do Banco Carrefour e preserva a substituição por padrões corporativos equivalentes.

---

## 9. Status

Documento atualizado como baseline dos ABBs que sustentam a implementação local e as evoluções produtivas documentadas.
