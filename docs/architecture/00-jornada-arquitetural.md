---
doc_id: ARCH-000
titulo: Jornada Arquitetural
versao: 1.0
status: Rascunho
responsavel: Arquitetura de SoluÃ§Ãµes
ultima_atualizacao: 2026-07-10
etapa_relacionada: Jornada completa
---

# Jornada Arquitetural

## 1. Objetivo

Este documento apresenta a jornada arquitetural utilizada para transformar o enunciado do desafio em uma soluÃ§Ã£o definida, rastreÃ¡vel e implementÃ¡vel.

A anÃ¡lise parte do contexto de negÃ³cio e avanÃ§a atÃ© os requisitos arquiteturais, ASRs, ABBs, decisÃµes arquiteturais, SBBs, arquitetura alvo, implementaÃ§Ã£o, testes e aspectos operacionais.

A implementaÃ§Ã£o transforma as principais decisÃµes arquiteturais em uma soluÃ§Ã£o executÃ¡vel.

---

## 2. Etapas da jornada

A jornada utilizada neste case segue uma versÃ£o reduzida e prÃ¡tica do trabalho de arquitetura de soluÃ§Ãµes:

1. Intake and Qualification
2. Discovery
3. Definition and Decision
4. Implementation and Governance
5. Validation, Operation and Evolution

---

## 3. Intake and Qualification

A etapa de Intake qualificou a demanda inicial, identificou os primeiros direcionadores arquiteturais e formulou a pergunta que orienta a anÃ¡lise.

O desafio propÃµe uma soluÃ§Ã£o para que um comerciante controle seu fluxo de caixa diÃ¡rio por meio de lanÃ§amentos de dÃ©bito e crÃ©dito.

AlÃ©m do registro dos lanÃ§amentos, o comerciante precisa consultar um relatÃ³rio com o saldo diÃ¡rio consolidado.

O enunciado estabelece duas responsabilidades principais:

```text
- controle de lanÃ§amentos
- consolidado diÃ¡rio
```

A leitura inicial da demanda identificou os seguintes direcionadores:

```text
- a soluÃ§Ã£o possui natureza financeira
- o registro de lanÃ§amentos Ã© a operaÃ§Ã£o mais crÃ­tica
- o consolidado Ã© uma visÃ£o derivada
- a falha do consolidado nÃ£o deve indisponibilizar lanÃ§amentos
- a entrega exige documentaÃ§Ã£o, ADRs, cÃ³digo e testes
```

A pergunta arquitetural orientadora foi definida como:

```text
Como registrar lanÃ§amentos financeiros de forma confiÃ¡vel e permitir a consulta de um consolidado diÃ¡rio por comerciante e data, com desempenho e disponibilidade, sem que falhas no consolidado afetem o registro dos lanÃ§amentos?
```

Essa pergunta direciona o desenho para:

```text
- proteÃ§Ã£o do caminho crÃ­tico de lanÃ§amentos
- separaÃ§Ã£o entre fonte de verdade financeira e visÃ£o consolidada derivada
- comunicaÃ§Ã£o assÃ­ncrona entre capacidades
- consistÃªncia eventual controlada
- prevenÃ§Ã£o de duplicidade
- recuperaÃ§Ã£o operacional
- observabilidade do fluxo fim a fim
- monitoramento de latÃªncia, erros, backlog, lag, DLQ e saÃºde dos workers
```

---

## 4. Discovery

A etapa de Discovery aprofundou o entendimento do problema, das regras de negÃ³cio, das premissas, dos riscos e dos pontos que exigem definiÃ§Ã£o arquitetural.

Principais entendimentos:

```text
- LanÃ§amentos Ã© a fonte de verdade financeira
- Consolidado Ã© uma visÃ£o derivada e reconstruÃ­vel
- o consolidado diÃ¡rio representa o movimento lÃ­quido do dia
- a data inicial do consolidado usa o calendÃ¡rio de America/Sao_Paulo
- a comunicaÃ§Ã£o entre LanÃ§amentos e Consolidado pode ser assÃ­ncrona
- os pontos de plataforma serÃ£o definidos por premissas e decisÃµes documentadas
```

Os pontos de negÃ³cio e plataforma ainda nÃ£o especificados sÃ£o tratados por meio de premissas explÃ­citas e decisÃµes documentadas.

---

## 5. Definition and Decision

A etapa de Definition and Decision transforma o entendimento do Discovery em decisÃµes arquiteturais, blocos de arquitetura e blocos de soluÃ§Ã£o.

A sequÃªncia adotada foi:

```text
1. definir a semÃ¢ntica do consolidado diÃ¡rio
2. separar responsabilidades entre LanÃ§amentos e Consolidado
3. identificar ASRs
4. definir ABBs
5. registrar ADRs
6. escolher SBBs
7. desenhar a arquitetura alvo
8. preparar implementaÃ§Ã£o, testes, seguranÃ§a e operaÃ§Ã£o
```

Essa etapa conecta o problema de negÃ³cio Ã s escolhas tÃ©cnicas e operacionais da soluÃ§Ã£o.

---

## 6. ASRs, ABBs, ADRs e SBBs

A documentaÃ§Ã£o utiliza quatro elementos centrais para manter o raciocÃ­nio rastreÃ¡vel:

| Elemento | Papel na arquitetura |
|---|---|
| ASR | Requisito arquiteturalmente significativo. Direciona decisÃµes relevantes. |
| ABB | Bloco de arquitetura. Define o que a soluÃ§Ã£o precisa ter, sem amarrar tecnologia. |
| ADR | Registro de decisÃ£o arquitetural. Documenta contexto, decisÃ£o, alternativas e tradeoffs. |
| SBB | Bloco de soluÃ§Ã£o. Materializa um ABB usando tecnologia, produto, serviÃ§o ou componente. |

Exemplos aplicados ao case:

| Tipo | Exemplos |
|---|---|
| ASRs | LanÃ§amentos nÃ£o depender do Consolidado; suportar 50 RPS no Consolidado; evitar perda silenciosa de lanÃ§amentos. |
| ABBs | fonte de verdade financeira; Outbox durÃ¡vel; canal assÃ­ncrono; consumo idempotente; projeÃ§Ã£o materializada. |
| ADRs | separaÃ§Ã£o entre LanÃ§amentos e Consolidado; uso de Outbox; consumo at-least-once; persistÃªncias independentes. |
| SBBs | .NET, PostgreSQL, RabbitMQ ou fila gerenciada equivalente, containers e serviÃ§os cloud de referÃªncia. |

---

## 7. Papel da implementaÃ§Ã£o

A implementaÃ§Ã£o transforma as principais decisÃµes arquiteturais em uma soluÃ§Ã£o executÃ¡vel.

Ela deve demonstrar:

```text
- separaÃ§Ã£o entre LanÃ§amentos e Consolidado
- registro de lanÃ§amentos sem dependÃªncia sÃ­ncrona do Consolidado
- persistÃªncia confiÃ¡vel de lanÃ§amentos
- Outbox para publicaÃ§Ã£o recuperÃ¡vel
- comunicaÃ§Ã£o assÃ­ncrona
- consumo idempotente
- projeÃ§Ã£o materializada do consolidado diÃ¡rio
- consulta de consolidado e relatÃ³rio diÃ¡rio
- testes automatizados
```

O foco da implementaÃ§Ã£o estÃ¡ nos fluxos e garantias centrais do desafio, mantendo espaÃ§o para evoluÃ§Ãµes futuras de seguranÃ§a, operaÃ§Ã£o, escala e plataforma.

---

## 8. Rastreabilidade

A documentaÃ§Ã£o mantÃ©m rastreabilidade entre:

```text
Requisito
-> ASR
-> ABB
-> ADR
-> SBB
-> Teste
```

Essa rastreabilidade facilita revisÃ£o, auditoria e avaliaÃ§Ã£o da soluÃ§Ã£o.

---

## 9. Status

Documento em rascunho para revisÃ£o antes da consolidaÃ§Ã£o do diretÃ³rio `docs/architecture`.
