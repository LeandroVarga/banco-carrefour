---
doc_id: ARCH-001
titulo: Contexto de Negócio
versao: 1.0
status: Rascunho
responsavel: Arquitetura de Soluções
ultima_atualizacao: 2026-07-10
etapa_relacionada: Intake and Discovery
---

# Contexto de Negócio

## 1. Objetivo

Este documento consolida o contexto de negócio do desafio, o problema a ser resolvido, as capacidades envolvidas, os requisitos funcionais, a semântica do consolidado diário e os principais pontos que precisam de definição.

---

## 2. Contexto do desafio

O desafio propõe uma solução para que um comerciante controle seu fluxo de caixa diário por meio de lançamentos de débito e crédito.

Além do registro dos lançamentos, o comerciante precisa consultar um relatório com o saldo diário consolidado.

O enunciado estabelece duas responsabilidades principais:

```text
- serviço responsável pelo controle de lançamentos
- serviço responsável pelo consolidado diário
```

Essa separação orienta a arquitetura em duas capacidades principais: uma voltada ao registro financeiro e outra voltada à leitura consolidada.

---

## 3. Problema de negócio

O comerciante precisa registrar movimentações financeiras do dia e consultar uma visão consolidada dessas movimentações.

A solução deve permitir que os lançamentos continuem sendo registrados mesmo quando o consolidado estiver indisponível, atrasado ou em recuperação.

O problema central pode ser resumido como:

```text
Garantir o registro confiável dos lançamentos financeiros e disponibilizar uma visão diária consolidada por comerciante e data.
```

---

## 4. Capacidades de negócio

| ID | Capacidade | Descrição |
|---|---|---|
| CAP-001 | Registrar lançamentos | Permitir o registro de créditos e débitos de um comerciante. |
| CAP-002 | Preservar histórico de lançamentos | Manter os lançamentos registrados como histórico confiável para cálculo, auditoria e reconstrução, sem prometer endpoint público de consulta de lançamentos no MVP. |
| CAP-003 | Consolidar movimentações diárias | Calcular os totais de crédito, débito e saldo diário por comerciante e data. |
| CAP-004 | Consultar consolidado diário | Permitir a consulta do saldo consolidado de um dia. |
| CAP-005 | Disponibilizar relatório diário | Apresentar uma visão consultável dos dados consolidados, com créditos, débitos, saldo, quantidade de lançamentos e última atualização. |
| CAP-006 | Rastrear movimentações | Manter informações suficientes para auditoria, investigação e suporte operacional. |
| CAP-007 | Recuperar visão consolidada | Permitir correção ou reconstrução da visão consolidada quando houver falha, atraso ou inconsistência. |

---

## 5. Requisitos funcionais

| ID | Requisito funcional |
|---|---|
| RF-001 | A solução deve permitir registrar lançamentos de crédito. |
| RF-002 | A solução deve permitir registrar lançamentos de débito. |
| RF-003 | Cada lançamento deve estar associado a um comerciante. |
| RF-004 | Cada lançamento deve possuir valor, tipo, data de ocorrência e informações de rastreabilidade. |
| RF-005 | A solução deve permitir consultar o consolidado diário por comerciante e data. |
| RF-006 | O consolidado diário deve apresentar total de créditos, total de débitos e saldo do dia. |
| RF-007 | O relatório diário deve apresentar os dados consolidados de forma consultável. |
| RF-008 | A solução deve preservar os lançamentos como base confiável para cálculo, auditoria e reconstrução do consolidado. |
| RF-009 | A solução deve tratar tentativas repetidas de registro para evitar duplicidade indevida. |

---

## 6. Semântica do lançamento

Um lançamento representa uma movimentação financeira informada para um comerciante.

No escopo inicial, um lançamento possui as seguintes características:

```text
- pertence a um comerciante
- possui tipo crédito ou débito
- possui valor monetário positivo
- possui data de ocorrência
- é registrado de forma rastreável
- não é alterado após o registro
```

Créditos aumentam o movimento líquido do dia.

Débitos reduzem o movimento líquido do dia.

Correções, cancelamentos, estornos ou ajustes contábeis não fazem parte do escopo inicial. Caso sejam necessários no futuro, devem ser tratados como novas regras de negócio e novas decisões arquiteturais.

---

## 7. Semântica do consolidado diário

O consolidado diário representa a visão agregada dos lançamentos de um comerciante em uma data.

No escopo inicial, o saldo diário consolidado representa o movimento líquido do dia:

```text
saldo diário = total de créditos do dia - total de débitos do dia
```

Portanto, o consolidado diário não representa, neste momento:

```text
- saldo bancário acumulado
- saldo contábil formal
- saldo de liquidação
- fechamento manual de caixa
- posição financeira histórica acumulada
```

A data usada para agrupamento inicial considera o calendário de `America/Sao_Paulo`.

Decisão relacionada: `docs/decisions/ADR-0000-semantica-do-consolidado-diario.md`.

---

## 8. Relatório diário

O relatório diário é a forma de apresentação do consolidado.

No escopo inicial, ele deve disponibilizar:

```text
- comerciante
- data
- total de créditos
- total de débitos
- saldo diário
- quantidade de lançamentos considerados
- data e hora da última atualização
```

O relatório inicial é consultável por API.

Exportações em arquivo, formatos específicos, layouts regulatórios ou fechamento formal não fazem parte do escopo inicial.

---

## 9. Premissas de negócio

| ID | Premissa |
|---|---|
| PRM-001 | O comerciante é o principal usuário da visão de fluxo de caixa diário. |
| PRM-002 | O consolidado diário representa o movimento líquido do dia. |
| PRM-003 | O saldo diário é calculado por comerciante e data. |
| PRM-004 | A moeda inicial considerada é BRL. |
| PRM-005 | Valores monetários devem ser tratados com precisão decimal. |
| PRM-006 | Lançamentos são imutáveis no escopo inicial. |
| PRM-007 | Correções futuras devem ser tratadas por novas regras de negócio. |
| PRM-008 | A data do consolidado segue inicialmente o calendário de America/Sao_Paulo. |
| PRM-009 | O relatório diário inicial é disponibilizado por consulta, não por geração de arquivo. |

---

## 10. Pontos que precisam de definição

Alguns pontos de negócio não foram especificados no desafio e precisam ser definidos conforme a evolução da solução.

| ID | Ponto de definição | Impacto |
|---|---|---|
| PND-001 | A data do consolidado deve considerar ocorrência, criação, liquidação ou data contábil? | Afeta agrupamento dos lançamentos e cálculo do saldo. |
| PND-002 | Existem horários de corte diferentes do dia calendário? | Afeta a regra de fechamento diário. |
| PND-003 | Lançamentos retroativos são permitidos? | Afeta reprocessamento, auditoria e atualização de consolidados anteriores. |
| PND-004 | A visão futura deve continuar como movimento líquido do dia ou evoluir para saldo acumulado? | Afeta modelo de cálculo e apresentação do relatório. |
| PND-005 | Estornos, cancelamentos e ajustes fazem parte do fluxo inicial? | Afeta regras de domínio e rastreabilidade. |
| PND-006 | O relatório precisa de exportação em arquivo? | Afeta requisitos de armazenamento, geração e distribuição. |
| PND-007 | Existe atraso máximo aceitável entre lançamento e consolidado? | Afeta metas de atualização, observabilidade e alertas. |
| PND-008 | Existem regras específicas por tipo de comerciante? | Afeta segmentação de regras e evolução do domínio. |

---

## 11. Fora do escopo inicial

Os seguintes pontos ficam fora do escopo inicial da solução:

```text
- múltiplas moedas
- múltiplos fusos horários
- fechamento manual de caixa
- conciliação bancária
- liquidação financeira
- regras contábeis formais
- saldo acumulado histórico
- exportação de relatório em arquivo
- estornos e cancelamentos
- previsão de fluxo de caixa
- integração com ERP ou sistemas externos
```

Esses temas podem ser tratados como evolução futura, conforme novas necessidades de negócio.

---

## 12. Relação com os próximos documentos

Este documento serve como base para os demais documentos de arquitetura.

A partir dele:

```text
- os requisitos arquiteturais e ASRs são detalhados em 02-requisitos-arquiteturais.md
- os ABBs são definidos em 03-blocos-de-arquitetura.md
- os SBBs são definidos em 04-blocos-de-solucao.md
- a arquitetura da solução é consolidada em 05-arquitetura-da-solucao.md
- as decisões relevantes são registradas em docs/decisions/
```

---

## 13. Status

Documento em rascunho até a consolidação dos demais documentos de arquitetura.
