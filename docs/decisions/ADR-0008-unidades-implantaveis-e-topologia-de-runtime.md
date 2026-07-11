---
adr_id: ADR-0008
titulo: Unidades Implantáveis e Topologia de Runtime
status: Aceita
data: 2026-07-10
responsavel: Arquitetura de Soluções
decisao_relacionada: Separação das unidades de execução da solução
---

# ADR-0008 — Unidades Implantáveis e Topologia de Runtime

## 1. Contexto

A solução possui duas fronteiras principais: Lançamentos e Consolidado.

Lançamentos protege o caminho crítico de registro financeiro.

Consolidado mantém uma visão derivada de leitura, atualizada de forma assíncrona.

Cada fronteira possui responsabilidades de API, processamento e persistência.

A solução também precisa executar publicação de eventos da Outbox e consumo de eventos do Consolidado sem acoplar essas responsabilidades ao ciclo de requisição das APIs.

Para atender disponibilidade, recuperação, escalabilidade e clareza operacional, as responsabilidades de API e processamento assíncrono precisam ser separadas em unidades de runtime adequadas.

---

## 2. Decisão

A solução será organizada em quatro unidades implantáveis principais:

```text
- Ledger.Api
- Ledger.OutboxPublisher
- Consolidation.Worker
- Consolidation.Api
```

As unidades representam a topologia lógica de runtime da solução.

`Ledger.Api` será responsável por receber e registrar lançamentos financeiros.

`Ledger.OutboxPublisher` será responsável por publicar eventos pendentes da Outbox.

`Consolidation.Worker` será responsável por consumir eventos e atualizar a projeção consolidada.

`Consolidation.Api` será responsável por expor a consulta do consolidado diário.

Essa topologia materializa duas fronteiras de negócio em quatro unidades de execução, separando APIs de workers.

---

## 3. O que a decisão inclui

Esta decisão inclui:

```text
- separação entre API de Lançamentos e publicador de Outbox
- separação entre consumidor do Consolidado e API de consulta
- possibilidade de escalar APIs e workers de forma independente
- possibilidade de reiniciar ou recuperar workers sem interromper APIs
- isolamento operacional entre registro, publicação, consumo e consulta
- clareza de responsabilidade por unidade implantável
- execução local em containers para avaliação do desafio
```

---

## 4. O que fica fora desta decisão

Esta decisão não define:

```text
- tecnologia final de implementação das APIs e workers
- plataforma final de runtime em cloud ou ambiente corporativo
- quantidade final de réplicas por unidade
- estratégia final de autoscaling
- configuração final de CPU, memória e limites
- estratégia final de deploy progressivo
- service mesh, gateway ou ingress definitivo
- malha final de segurança entre serviços
```

Esses pontos serão detalhados na arquitetura da solução, na documentação operacional, na documentação de segurança e em ADRs complementares.

---

## 5. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar como base da solução |
|---|---|---|
| Unidade única com API e processamento juntos | Uma única aplicação executaria registro, publicação, consumo e consulta. | Simplifica empacotamento, mas mistura responsabilidades, dificulta escala independente e reduz clareza operacional. |
| Duas unidades implantáveis por fronteira | Uma unidade para Lançamentos e uma unidade para Consolidado, cada uma contendo API e processamento. | Mantém fronteiras de negócio, mas acopla APIs e workers dentro do mesmo runtime, dificultando recuperação e escala independente. |
| Três unidades implantáveis | API de Lançamentos, worker de consolidação e API de Consolidado, com publicação da Outbox dentro da API de Lançamentos. | Reduz uma unidade, mas reaproxima publicação assíncrona do ciclo operacional da API de escrita. |
| Quatro unidades implantáveis | APIs e workers separados para Lançamentos e Consolidado. | Alternativa adotada. Mantém clareza de responsabilidade, isolamento operacional, escala independente e recuperação mais controlada. |
| Muitos microsserviços granulares | Dividir cada responsabilidade menor em serviços próprios. | Aumenta complexidade de comunicação, deploy, observabilidade e operação sem necessidade para o escopo inicial. |

---

## 6. Consequências

Consequências positivas:

```text
- protege melhor o caminho crítico de registro financeiro
- permite escalar consulta separadamente de processamento
- permite escalar workers conforme backlog e volume de eventos
- facilita reinício e recuperação de processamento assíncrono
- melhora clareza operacional de logs, métricas e responsabilidades
- facilita isolamento de falhas entre API, publicação, consumo e consulta
- prepara a solução para execução local e evolução para plataforma corporativa
```

Consequências e tradeoffs:

```text
- aumenta o número de unidades a empacotar e executar
- exige configuração por componente
- exige observabilidade por unidade implantável
- exige coordenação de deploy entre componentes relacionados
- exige disciplina de versionamento de contratos entre produtor e consumidor
- aumenta a atenção necessária a health checks, readiness e dependências de runtime
```

---

## 7. Relação com requisitos, ABBs e SBBs

Esta decisão sustenta principalmente:

```text
- RNF-001: Lançamentos não deve ficar indisponível caso Consolidado falhe
- RNF-002: Consolidado deve suportar 50 RPS em pico
- RNF-003: Consolidado deve limitar falhas ou perdas de requisições a no máximo 5% no pico
- ASR-001: Lançamentos deve continuar disponível mesmo se Consolidado falhar
- ASR-002: Consolidado deve suportar 50 RPS em pico
- ASR-005: Consolidado pode ficar temporariamente defasado, desde que observável e recuperável
- ASR-010: O fluxo deve ser observável
- ASR-011: Falhas de publicação, consumo ou consolidação devem ser recuperáveis
- ABB-001: Fronteira de Lançamentos
- ABB-006: Publicação Recuperável
- ABB-008: Fronteira de Consolidado
- ABB-012: API de Consulta do Consolidado
- ABB-013: Observabilidade do Fluxo
- ABB-014: Recuperação Operacional
- SBB-001: Ledger.Api
- SBB-006: Ledger.OutboxPublisher
- SBB-008: Consolidation.Worker
- SBB-012: Consolidation.Api
- SBB-018: Containers and Local Runtime
```

---

## 8. Relação com documentos

Esta decisão sustenta:

```text
- docs/architecture/04-blocos-de-solucao.md
- docs/architecture/05-arquitetura-da-solucao.md
- docs/architecture/06-diagramas.md
- docs/operations/arquitetura-operacional.md
```

---

## 9. Status

Decisão aceita para o escopo inicial da solução.
