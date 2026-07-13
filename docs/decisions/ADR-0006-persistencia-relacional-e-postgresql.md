---
adr_id: ADR-0006
titulo: Persistência Relacional e PostgreSQL
status: Aceita
data: 2026-07-10
responsavel: Arquitetura de Soluções
decisao_relacionada: Tecnologia de persistência
---

# ADR-0006 — Persistência Relacional e PostgreSQL

## 1. Contexto

A solução precisa registrar lançamentos financeiros, controlar idempotência de entrada, manter Outbox, processar eventos de consolidação e armazenar a projeção DailyBalance.

Esses dados possuem relações claras, necessidade de consistência transacional, chaves únicas, restrições, consultas por comerciante e data, e rastreabilidade operacional.

A fronteira de Lançamentos precisa garantir consistência entre o lançamento financeiro e a intenção de publicação na Outbox.

A fronteira de Consolidado precisa garantir consistência entre o registro de evento processado e a atualização da projeção consolidada.

Essas características favorecem uma persistência relacional no escopo inicial da solução.

---

## 2. Decisão

A solução adotará persistência relacional como modelo principal para Lançamentos e Consolidado.

PostgreSQL será usado como banco relacional de referência para o desafio.

A solução terá duas persistências relacionais independentes:

```text
- Ledger Database
- Consolidation Database
```

O Ledger Database armazenará:

```text
- lançamentos financeiros
- registros de idempotência de entrada
- Outbox de eventos pendentes de publicação
```

O Consolidation Database armazenará:

```text
- projeção DailyBalance
- registros de eventos processados
- dados necessários para consulta e reconstrução da visão consolidada
```

Na execução local, PostgreSQL roda em containers separados para Ledger e Consolidation.

Na AWS de referência do case, a materialização física é Amazon RDS for PostgreSQL, mantendo persistências separadas por fronteira.

---

## 3. O que a decisão inclui

Esta decisão inclui:

```text
- uso de banco relacional como modelo principal de persistência
- uso de PostgreSQL como banco relacional escolhido
- PostgreSQL em container para execução local e implementação do desafio
- Amazon RDS for PostgreSQL como materialização AWS de referência
- suporte a transações locais nas fronteiras
- suporte a constraints, chaves únicas e índices
- suporte a consultas por comerciante e data
- suporte ao padrão Outbox
- suporte ao controle de eventos processados
```

---

## 4. O que fica fora desta decisão

Esta decisão não define:

```text
- estratégia final de alta disponibilidade
- política final de backup e restore
- RTO e RPO finais
- estratégia completa de particionamento
- estratégia final de arquivamento e retenção
- configuração final de pool de conexões
- tuning final de índices
```

Esses pontos serão detalhados na arquitetura operacional, na estimativa de custos e em decisões futuras quando necessário.

---

## 5. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar como base da solução |
|---|---|---|
| Banco relacional com PostgreSQL | Persistência relacional com transações, constraints, índices e consultas estruturadas. | Alternativa adotada. Atende bem ao registro financeiro, Outbox, idempotência e projeção consolidada. |
| Banco NoSQL chave-valor ou documento | Persistência orientada a acesso por chave ou documento. | Pode ser útil para cenários específicos de escala ou baixa latência, mas adiciona complexidade para transações, constraints e consistência entre registros relacionados. |
| Cache como persistência principal | Armazenamento em memória ou cache distribuído como base do Consolidado. | Pode apoiar desempenho, mas não é adequado como fonte principal para rastreabilidade, reconstrução e durabilidade do fluxo financeiro. |
| Arquivos ou storage de objetos | Persistência em arquivos ou objetos para histórico e relatórios. | Pode ser útil para exportações ou arquivamento, mas não atende bem ao fluxo transacional inicial e às consultas operacionais por comerciante e data. |
| Banco relacional diferente | Uso de outro banco relacional, como SQL Server, MySQL ou Oracle. | Poderia atender tecnicamente, mas PostgreSQL oferece boa adequação ao desafio, execução local simples, ampla adoção e equivalência com serviços gerenciados em cloud. |

---

## 6. Consequências

Consequências positivas:

```text
- permite transações locais consistentes
- favorece controle de idempotência por chaves únicas
- favorece implementação do padrão Outbox
- permite modelagem clara de lançamentos, eventos e projeções
- facilita consultas por comerciante e data
- simplifica execução local com containers
- mantém caminho claro para Amazon RDS for PostgreSQL na AWS de referência
```

Consequências e tradeoffs:

```text
- exige gestão de schema e migrations
- exige atenção a índices e plano de consulta
- exige operação adequada de conexões e pool
- pode exigir particionamento ou arquivamento conforme crescimento histórico
- não elimina necessidade de observabilidade, backup, restore e estratégia operacional
```

---

## 7. Relação com requisitos, ABBs e SBBs

Esta decisão sustenta principalmente:

```text
- ASR-004: Lançamentos registrados devem permanecer confiáveis
- ASR-006: Tentativas repetidas de registro não devem criar lançamentos duplicados indevidamente
- ASR-007: Eventos ou mensagens duplicadas não devem duplicar efeitos no Consolidado
- ASR-008: A consulta do Consolidado deve usar uma estrutura adequada para leitura
- ABB-003: Persistência Transacional de Lançamentos
- ABB-004: Idempotência de Entrada
- ABB-005: Outbox Durável
- ABB-009: Consumo Idempotente
- ABB-010: Projeção Materializada do Consolidado
- ABB-011: Persistência do Consolidado
- SBB-002: Ledger Database
- SBB-003: Entries
- SBB-004: Input Idempotency
- SBB-005: Outbox
- SBB-009: Consolidation Database
- SBB-010: Processed Events
- SBB-011: DailyBalance
```

---

## 8. Relação com documentos

Esta decisão sustenta:

- [04-blocos-de-solucao.md](../architecture/04-blocos-de-solucao.md)
- [05-arquitetura-da-solucao.md](../architecture/05-arquitetura-da-solucao.md)
- [arquitetura-operacional.md](../operations/arquitetura-operacional.md)

---

## 9. Status

Decisão aceita para o escopo inicial da solução.
