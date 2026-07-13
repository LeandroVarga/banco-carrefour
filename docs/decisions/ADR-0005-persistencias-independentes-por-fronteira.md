---
adr_id: ADR-0005
titulo: Persistências Independentes por Fronteira
status: Aceita
data: 2026-07-10
responsavel: Arquitetura de Soluções
decisao_relacionada: Separação de persistência entre Lançamentos e Consolidado
---

# ADR-0005 — Persistências Independentes por Fronteira

## 1. Contexto

A solução separa Lançamentos e Consolidado em fronteiras arquiteturais distintas.

Lançamentos é responsável pelo registro financeiro e pela fonte de verdade dos lançamentos.

Consolidado é responsável pela visão derivada de leitura, calculada por comerciante e data.

Essa separação perde força se as duas fronteiras compartilharem a mesma persistência como modelo comum de integração.

Uma persistência compartilhada poderia aumentar acoplamento entre escrita financeira, consulta consolidada, evolução de schema, reconstrução da projeção e operação da solução.

Para manter responsabilidades claras, cada fronteira precisa controlar seus próprios dados e sua própria evolução de persistência.

---

## 2. Decisão

Lançamentos e Consolidado terão persistências independentes.

A fronteira de Lançamentos terá uma persistência própria para:

```text
- lançamentos financeiros
- registros de idempotência de entrada
- Outbox de eventos pendentes de publicação
```

A fronteira de Consolidado terá uma persistência própria para:

```text
- projeção DailyBalance
- registros de eventos processados
- dados necessários para consulta e reconstrução da visão consolidada
```

Essa decisão define independência de persistência entre as fronteiras.

A tecnologia específica de banco de dados será definida em ADR posterior.

---

## 3. O que a decisão inclui

Esta decisão inclui:

```text
- separação entre Ledger Database e Consolidation Database
- propriedade dos dados pela fronteira responsável
- ausência de dependência de leitura direta do Consolidado sobre tabelas internas de Lançamentos
- evolução independente dos modelos de persistência
- reconstrução do Consolidado a partir da fonte de verdade financeira
- redução de acoplamento entre escrita financeira e leitura consolidada
```

---

## 4. O que fica fora desta decisão

Esta decisão não define:

```text
- tecnologia final de banco de dados
- produto gerenciado em cloud
- topologia física final
- alta disponibilidade do banco
- política final de backup e restore
- RTO e RPO finais
- estratégia definitiva de particionamento, índices ou retenção
```

Esses pontos serão detalhados nos blocos de solução, na arquitetura operacional e em ADRs específicos.

---

## 5. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar como base da solução |
|---|---|---|
| Banco único compartilhado por todas as responsabilidades | Lançamentos e Consolidado usariam a mesma base e poderiam compartilhar tabelas ou consultas internas. | Aumenta acoplamento entre fronteiras e dificulta evolução independente. Também cria risco de o Consolidado depender diretamente da estrutura interna de Lançamentos. |
| Consolidado lendo diretamente as tabelas de Lançamentos | A consulta do Consolidado seria calculada ou montada a partir da persistência de Lançamentos. | Reaproxima leitura consolidada da fonte transacional, aumenta custo de leitura e reduz isolamento entre responsabilidades. |
| Mesmo banco físico com separação apenas por schema | As fronteiras ficariam em schemas diferentes dentro do mesmo banco físico. | Pode ser uma escolha operacional em ambientes específicos, mas não deve ser o contrato arquitetural principal entre as fronteiras. |
| Persistências independentes por fronteira | Cada fronteira controla sua própria persistência e se integra por fluxo assíncrono. | Alternativa adotada. Mantém isolamento, clareza de responsabilidade, evolução independente e reconstrução controlada do Consolidado. |

---

## 6. Consequências

Consequências positivas:

```text
- reforça a separação entre Lançamentos e Consolidado
- protege a fonte de verdade financeira
- evita acoplamento por banco compartilhado
- permite evolução independente dos modelos de dados
- facilita tratar Consolidado como visão derivada e reconstruível
- reduz impacto de consultas do Consolidado sobre a persistência transacional de Lançamentos
```

Consequências e tradeoffs:

```text
- introduz consistência eventual entre as fronteiras
- exige integração por eventos ou mensagens
- impede joins diretos entre dados internos de fronteiras diferentes
- exige mecanismos de reconciliação, reconstrução ou reprocessamento quando necessário
- aumenta a responsabilidade operacional sobre mais de uma persistência
```

---

## 7. Relação com requisitos, ABBs e SBBs

Esta decisão sustenta principalmente:

```text
- ASR-001: Lançamentos deve continuar disponível mesmo se Consolidado falhar
- ASR-004: Lançamentos registrados devem permanecer confiáveis
- ASR-005: Consolidado pode ficar temporariamente defasado, desde que observável e recuperável
- ASR-008: A consulta do Consolidado deve usar uma estrutura adequada para leitura
- ABB-002: Fonte de Verdade Financeira
- ABB-003: Persistência Transacional de Lançamentos
- ABB-011: Persistência do Consolidado
- SBB-002: Ledger Database
- SBB-003: Entries
- SBB-005: Outbox
- SBB-009: Consolidation Database
- SBB-010: Processed Events
- SBB-011: DailyBalance
```

---

## 8. Relação com documentos

Esta decisão sustenta:

- [03-blocos-de-arquitetura.md](../architecture/03-blocos-de-arquitetura.md)
- [04-blocos-de-solucao.md](../architecture/04-blocos-de-solucao.md)

---

## 9. Status

Decisão aceita para o escopo inicial da solução.
