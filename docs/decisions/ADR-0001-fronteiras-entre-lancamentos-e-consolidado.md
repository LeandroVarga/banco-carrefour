---
adr_id: ADR-0001
titulo: Fronteiras entre Lançamentos e Consolidado
status: Aceita
data: 2026-07-10
responsavel: Arquitetura de Soluções
decisao_relacionada: Separação de responsabilidades
---

# ADR-0001 — Fronteiras entre Lançamentos e Consolidado

## 1. Contexto

O desafio define duas responsabilidades principais para a solução:

```text
- serviço responsável pelo controle de lançamentos
- serviço responsável pelo consolidado diário
```

Também define como requisito não funcional que o serviço de controle de lançamentos não deve ficar indisponível caso o serviço de consolidado diário falhe.

Esse requisito exige uma separação clara entre o caminho de registro financeiro e o caminho de leitura consolidada.

No contexto da solução, Lançamentos representa a entrada confiável das movimentações financeiras. Consolidado representa uma visão derivada para consulta do saldo diário.

---

## 2. Decisão

Lançamentos e Consolidado serão tratados como fronteiras arquiteturais distintas.

Lançamentos será responsável por:

```text
- receber registros de créditos e débitos
- validar dados de entrada
- aplicar idempotência de entrada
- persistir lançamentos financeiros
- manter a fonte de verdade financeira
- produzir informação para atualização posterior do Consolidado
```

Consolidado será responsável por:

```text
- receber informações derivadas dos lançamentos
- atualizar a visão consolidada diária
- manter projeção de leitura por comerciante e data
- expor consulta do relatório diário consolidado
- permitir recuperação ou reconstrução da visão consolidada
```

A falha do Consolidado não deve impedir o registro de novos lançamentos.

Esta decisão define fronteiras lógicas de responsabilidade. A topologia física, a quantidade de unidades implantáveis, os bancos, a mensageria e a stack tecnológica serão definidos em ADRs posteriores.

---

## 3. O que a decisão inclui

Esta decisão inclui:

```text
- separação entre escrita financeira e leitura consolidada
- Lançamentos como fonte de verdade financeira
- Consolidado como visão derivada
- isolamento do caminho crítico de registro
- evolução independente das responsabilidades
- base para comunicação assíncrona entre fronteiras
```

---

## 4. O que fica fora desta decisão

Esta decisão não define:

```text
- tecnologia de implementação
- linguagem de programação
- banco de dados
- broker ou fila
- quantidade final de serviços implantáveis
- estratégia de deploy
- plataforma cloud
- modelo final de observabilidade
```

Esses pontos serão definidos em decisões específicas, conforme a arquitetura avançar dos ABBs para os SBBs.

---

## 5. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar como base da solução |
|---|---|---|
| Uma única fronteira para Lançamentos e Consolidado | Registro e consulta consolidada ficariam sob a mesma responsabilidade arquitetural. | Aumenta o acoplamento entre escrita financeira e leitura consolidada, dificultando atender ao requisito de isolamento quando Consolidado falhar. |
| Lançamentos chamando Consolidado de forma síncrona no registro | O registro do lançamento dependeria da atualização imediata do Consolidado. | Torna o registro sensível à indisponibilidade ou lentidão do Consolidado. Contraria o requisito de que Lançamentos continue disponível se Consolidado falhar. |
| Consolidado calculado sob demanda a partir dos lançamentos | A consulta calcularia o saldo diário lendo os lançamentos a cada requisição. | Pode simplificar o modelo inicial, mas aumenta custo de leitura e dificulta suportar pico de consulta com previsibilidade. |
| Fronteiras separadas entre Lançamentos e Consolidado | Registro financeiro e visão consolidada ficam separados por responsabilidade. | Alternativa adotada. Atende melhor ao isolamento, à clareza de responsabilidades e à evolução da solução. |

---

## 6. Consequências

Consequências positivas:

```text
- protege o caminho crítico de registro financeiro
- reduz acoplamento entre escrita e leitura
- permite que Consolidado falhe sem indisponibilizar Lançamentos
- facilita escalar consulta e processamento de forma independente
- permite tratar Consolidado como visão derivada e reconstruível
- melhora clareza de responsabilidades para implementação, testes e operação
```

Consequências e tradeoffs:

```text
- introduz consistência eventual entre Lançamentos e Consolidado
- exige mecanismo confiável para propagar alterações ao Consolidado
- exige idempotência no processamento de eventos ou mensagens
- exige observabilidade para acompanhar atraso, backlog e falhas
- aumenta a complexidade operacional em relação a uma solução totalmente síncrona e simples
```

---

## 7. Relação com requisitos e ABBs

Esta decisão sustenta principalmente:

```text
- RNF-001: Lançamentos não deve ficar indisponível caso Consolidado falhe
- ASR-001: Lançamentos deve continuar disponível mesmo se Consolidado falhar
- ASR-004: Lançamentos registrados devem permanecer confiáveis
- ASR-005: Consolidado pode ficar temporariamente defasado, desde que observável e recuperável
- ABB-001: Fronteira de Lançamentos
- ABB-002: Fonte de Verdade Financeira
- ABB-007: Canal Assíncrono Confiável
- ABB-008: Fronteira de Consolidado
- ABB-010: Projeção Materializada do Consolidado
```

---

## 8. Relação com documentos

Esta decisão sustenta:

```text
- docs/architecture/01-contexto-de-negocio.md
- docs/architecture/02-requisitos-arquiteturais.md
- docs/architecture/03-blocos-de-arquitetura.md
```

---

## 9. Status

Decisão aceita para o escopo inicial da solução.
