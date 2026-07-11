---
doc_id: ARCH-000
titulo: Jornada Arquitetural
versao: 1.0
status: Rascunho
responsavel: Arquitetura de Soluções
ultima_atualizacao: 2026-07-11
etapa_relacionada: Intake and Qualification
---

# Jornada Arquitetural

## 1. Objetivo

Este documento apresenta a jornada arquitetural usada para transformar o desafio Banco Carrefour em uma solução arquitetural definida, rastreável e implementável.

A jornada organiza o raciocínio do arquiteto de soluções desde o entendimento inicial do problema até a definição da arquitetura alvo, decisões, segurança, operação, observabilidade, custos e próximos incrementos de implementação.

---

## 2. Etapas da jornada

A jornada usada neste case segue cinco macroetapas:

```text
1. Intake and Qualification
2. Discovery
3. Definition and Decision
4. Realization and Governance
5. Production Validation and Evolution
```

---

## 3. Intake and Qualification

Nesta etapa, o desafio é recebido, qualificado e transformado em uma pergunta arquitetural investigável.

O desafio propõe uma solução para que comerciantes controlem o fluxo de caixa diário por meio de lançamentos de débito e crédito e consultem um consolidado diário.

Direcionadores iniciais:

```text
- a solução possui natureza financeira
- o registro de lançamentos é a operação mais crítica
- o consolidado é uma visão derivada
- a falha do consolidado não deve indisponibilizar lançamentos
- a entrega exige documentação, decisões arquiteturais, segurança, operação, implementação e testes
```

Pergunta arquitetural principal:

```text
Como registrar lançamentos financeiros de forma confiável e permitir a consulta de um consolidado diário por comerciante e data, com desempenho e disponibilidade, sem que falhas no consolidado afetem o registro dos lançamentos?
```

---

## 4. Discovery

Nesta etapa, o problema é analisado em termos de negócio, requisitos, capacidades, restrições, riscos e requisitos arquiteturalmente significativos.

O Discovery deste case resultou nos seguintes documentos:

```text
- docs/architecture/01-contexto-de-negocio.md
- docs/architecture/02-requisitos-arquiteturais.md
```

Principais conclusões:

```text
- Lançamentos deve ser tratado como fonte de verdade financeira
- Consolidado deve ser tratado como visão derivada e reconstruível
- o caminho de escrita não deve depender de leitura consolidada
- a comunicação entre Lançamentos e Consolidado deve aceitar consistência eventual
- duplicidade, reprocessamento e falhas transitórias precisam ser tratados explicitamente
```

---

## 5. Definition and Decision

Nesta etapa, a arquitetura alvo é definida e as principais decisões são registradas.

Documentos relacionados:

```text
- docs/architecture/03-blocos-de-arquitetura.md
- docs/architecture/04-blocos-de-solucao.md
- docs/architecture/05-arquitetura-da-solucao.md
- docs/architecture/06-diagramas.md
- docs/architecture/07-rastreabilidade.md
- docs/architecture/08-implementation-readiness.md
- docs/decisions/
```

Decisões centrais:

```text
- separar Lançamentos e Consolidado
- proteger o caminho de registro financeiro
- usar Outbox para publicação confiável
- usar comunicação assíncrona
- processar eventos com entrega at-least-once e idempotência
- materializar DailyBalance como projeção de leitura
- manter persistências independentes por fronteira
- documentar segurança, operação, observabilidade e custos
```

---

## 6. Realization and Governance

Nesta etapa, a arquitetura é preparada para implementação.

Para este case, a etapa inclui:

```text
- contratos HTTP
- contrato assíncrono de evento
- invariantes transacionais
- estratégia de idempotência
- estratégia de concorrência
- estratégia de rebuild
- execução local
- testes automatizados
- validação de carga
```

Esses itens são tratados progressivamente a partir do documento `docs/architecture/08-implementation-readiness.md`.

---

## 7. Production Validation and Evolution

Nesta etapa, a solução é validada por evidências técnicas e operacionais.

No contexto do desafio, isso inclui:

```text
- teste de registro de lançamentos
- teste de falha do Consolidado sem indisponibilizar Lançamentos
- teste de idempotência
- teste de consumo duplicado
- teste de consulta do Consolidado com 50 RPS
- medição da taxa de falhas no pico
- validação de logs, métricas e correlação
- validação de reprocessamento e rebuild
```

---

## 8. Status

A jornada arquitetural está documentada e será refinada conforme contratos, implementação, testes e evidências forem adicionados ao repositório.
