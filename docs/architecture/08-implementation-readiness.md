---
doc_id: ARCH-008
titulo: Prontidão para Implementação
versao: 1.0
status: Rascunho
responsavel: Arquitetura de Soluções
ultima_atualizacao: 2026-07-11
etapa_relacionada: Definition and Decision
---

# Prontidão para Implementação

## 1. Objetivo

Este documento fecha decisões necessárias para transformar a arquitetura documental em implementação objetiva.

Ele complementa os documentos de arquitetura, segurança, operação, observabilidade e ADRs já criados.

O objetivo é reduzir decisões implícitas durante a implementação.

---

## 2. Escopo deste documento

Este documento define:

```text
- semântica operacional de businessDate
- cutoff inicial
- tratamento de lançamentos retroativos
- contratos esperados antes da implementação
- invariantes transacionais
- regras de idempotência
- concorrência no Ledger e no Consolidado
- estratégia inicial de rebuild do DailyBalance
- perfil de validação para 50 RPS e 5%
- autenticação local testável
- pontos ainda fora do escopo da implementação mínima
```

---

## 3. Business date

Para a implementação inicial, `businessDate` será derivado de `occurredAt`.

Regra:

```text
businessDate = data local de occurredAt convertida para America/Sao_Paulo
```

Exemplo:

```text
occurredAt: 2026-07-11T23:30:00Z
fuso de negócio: America/Sao_Paulo
businessDate: 2026-07-11
```

`createdAt` não define a data do consolidado.

`createdAt` representa quando o lançamento foi registrado no sistema.

`occurredAt` representa quando o evento financeiro ocorreu para fins de agrupamento diário.

---

## 4. Cutoff inicial

O cutoff inicial segue o dia calendário no fuso America/Sao_Paulo.

Janela de um businessDate:

```text
início inclusivo: 00:00:00.000 America/Sao_Paulo
fim exclusivo: próximo dia às 00:00:00.000 America/Sao_Paulo
```

Exemplo:

```text
businessDate 2026-07-11
início: 2026-07-11T00:00:00 America/Sao_Paulo
fim:    2026-07-12T00:00:00 America/Sao_Paulo
```

Não haverá horário de fechamento manual na implementação inicial.

Fechamento contábil, calendário bancário, feriados, múltiplos fusos e múltiplas moedas permanecem fora do escopo inicial.

---

## 5. Lançamentos retroativos

Lançamentos retroativos serão aceitos na implementação inicial.

Um lançamento retroativo é um lançamento registrado hoje com `occurredAt` pertencente a um businessDate anterior.

Regra:

```text
lançamentos retroativos atualizam o DailyBalance correspondente ao businessDate derivado de occurredAt
```

Consequências:

```text
- o Consolidado de dias anteriores pode mudar
- a API de Consolidado deve retornar lastUpdatedAt
- a documentação e os testes devem tratar o Consolidado como visão derivada
- consultas devem considerar que o saldo diário é eventualmente consistente
```

Não serão implementados estorno, cancelamento ou fechamento imutável de dia financeiro neste incremento.

---

## 6. Capacidade de consulta de lançamentos

A implementação mínima não terá endpoint público para listar lançamentos.

Os lançamentos serão preservados no Ledger Database como fonte de verdade para auditoria, idempotência, publicação, recuperação e rebuild.

Portanto, a capacidade inicial deve ser entendida como:

```text
preservar histórico de lançamentos registrados
```

e não como:

```text
fornecer consulta pública de lançamentos por API
```

Consulta pública de lançamentos pode ser tratada em evolução futura, caso o escopo seja ampliado.

---

## 7. Contratos HTTP mínimos

A implementação inicial deve expor dois contratos HTTP principais:

```text
POST /entries
GET /daily-balances/{businessDate}
```

`POST /entries` registra um lançamento financeiro.

`GET /daily-balances/{businessDate}` consulta o consolidado diário do comerciante autenticado.

Os contratos detalhados devem ser materializados em:

```text
contracts/openapi.yaml
```

---

## 8. Contrato assíncrono mínimo

O evento inicial de integração será:

```text
EntryCreated.v1
```

O contrato detalhado deve ser materializado em:

```text
contracts/events/entry-created-v1.schema.json
```

Campos mínimos esperados:

```text
- eventId
- eventType
- eventVersion
- occurredAt
- createdAt
- correlationId
- entryId
- merchantId
- businessDate
- type
- amount
- currency
```

---

## 9. Invariantes transacionais do Ledger

O registro de lançamento deve ocorrer em transação local no Ledger Database.

A mesma transação deve persistir:

```text
- registro de idempotência de entrada, quando aplicável
- lançamento financeiro
- registro de Outbox pendente
```

Invariante:

```text
não pode existir lançamento financeiro confirmado sem registro de Outbox correspondente
```

Outra invariante:

```text
não pode existir resposta de sucesso ao cliente antes do commit da transação local do Ledger
```

---

## 10. Idempotência de entrada

`POST /entries` deve exigir uma chave de idempotência.

Escopo da chave:

```text
merchantId + idempotencyKey
```

Regras:

```text
- mesma chave e mesmo fingerprint retornam resposta equivalente
- mesma chave e payload divergente retornam conflito
- chave de idempotência deve ser associada ao comerciante autenticado
- chave de idempotência não deve ser global entre comerciantes
```

Fingerprint deve considerar os campos de negócio relevantes:

```text
- merchantId
- type
- amount
- currency
- occurredAt
- description quando existir no contrato
```

Resposta para repetição equivalente:

```text
retornar o mesmo entryId ou uma resposta equivalente ao registro original
```

Resposta para divergência:

```text
HTTP 409 Conflict
```

---

## 11. Invariantes transacionais do Consolidado

O processamento de um evento no Consolidado deve ocorrer em transação local no Consolidation Database.

A mesma transação deve:

```text
- verificar se eventId já foi processado
- atualizar ou criar DailyBalance
- registrar eventId em ProcessedEvents
```

Invariante:

```text
o mesmo eventId não pode produzir efeito financeiro mais de uma vez
```

Outra invariante:

```text
a atualização do DailyBalance e o registro em ProcessedEvents devem ser atômicos
```

---

## 12. Chaves únicas e concorrência

Constraints mínimas esperadas:

| Estrutura | Chave única | Objetivo |
|---|---|---|
| InputIdempotency | merchantId + idempotencyKey | Evitar duplicidade por retry de entrada. |
| Entries | entryId | Identificar lançamento financeiro. |
| Outbox | outboxId | Identificar mensagem pendente. |
| Outbox | eventId | Evitar publicação duplicada do mesmo evento lógico. |
| DailyBalance | merchantId + businessDate | Garantir uma projeção por comerciante e data. |
| ProcessedEvents | eventId | Impedir reaplicação do mesmo evento. |

Concorrência no Ledger:

```text
- requisições simultâneas com mesma chave de idempotência devem resultar em apenas um lançamento
- concorrência deve ser controlada por constraint única e transação
- conflitos devem ser tratados de forma determinística
```

Concorrência no Consolidado:

```text
- eventos do mesmo comerciante e businessDate podem chegar em paralelo
- atualização de DailyBalance deve ser protegida por transação e estratégia de upsert ou lock apropriado
- múltiplas entregas do mesmo eventId devem ser descartadas por ProcessedEvents
```

Concorrência no Outbox Publisher:

```text
- múltiplos publishers só podem processar o mesmo registro de Outbox se houver mecanismo de claim/lock
- na implementação inicial, pode haver apenas uma instância do publisher
- se houver paralelismo, o registro precisa ter status, tentativa, lockedAt ou mecanismo equivalente
```

---

## 13. Rebuild do DailyBalance

DailyBalance é uma projeção derivada e reconstruível.

A implementação inicial deve tratar rebuild como operação controlada, não como fluxo comum de usuário.

Para preservar o isolamento entre fronteiras:

```text
Consolidation.Worker não deve acessar diretamente o Ledger Database
```

Estratégia inicial de rebuild:

```text
1. operação administrativa define merchantId e período
2. um componente da fronteira de Lançamentos relê Entries do Ledger Database
3. eventos EntryCreated.v1 são republicados ou disponibilizados para rebuild controlado
4. o Consolidado reconstrói DailyBalance para o escopo definido
5. o processo registra início, fim, escopo, resultado e eventuais divergências
```

Na implementação mínima, o rebuild pode ser documentado e testado de forma restrita.

Antes de produção, o mecanismo deve definir:

```text
- componente executor
- autorização administrativa
- coordenação com eventos novos
- uso de staging ou troca atômica da projeção
- validação de totais
- estratégia de retomada em caso de falha
```

---

## 14. Perfil de validação para 50 RPS

O requisito de 50 RPS será validado no caminho de consulta do Consolidado.

Cenário inicial de teste:

```text
- endpoint: GET /daily-balances/{businessDate}
- duração mínima do pico: 60 segundos
- carga sustentada: 50 RPS
- rampa inicial: até 30 segundos
- dataset previamente carregado
- consultas distribuídas entre múltiplos comerciantes e datas
- ambiente de execução declarado no resultado do teste
```

Critérios de sucesso:

```text
- taxa de falhas ou perdas de requisições elegíveis <= 5%
- p95 de latência <= 500 ms
- p99 de latência <= 1000 ms
- sem perda de dados financeiros
- sem indisponibilizar POST /entries
```

Definição de requisição elegível:

```text
requisição autenticada, autorizada, bem formada e direcionada a um businessDate válido
```

Não entram no denominador de falha do requisito de 5%:

```text
- 400 Bad Request por payload inválido
- 401 Unauthorized
- 403 Forbidden
- 404 para recurso sem dados, quando esse comportamento for previsto em contrato
```

Entram como falha:

```text
- 5xx
- timeout
- conexão encerrada sem resposta
- resposta acima do timeout definido para o teste
```

---

## 15. Autenticação local testável

A execução local não deve confiar em `merchantId` arbitrário enviado pelo cliente.

Para a implementação inicial, a autenticação local deve usar tokens de teste assinados localmente ou mecanismo equivalente de autenticação de desenvolvimento.

Claims mínimas esperadas:

```text
- sub
- merchant_id ou merchant_scope
- role
```

Regras:

```text
- POST /entries deve validar se o comerciante do lançamento é compatível com o token
- GET /daily-balances/{businessDate} deve derivar o comerciante do token
- acesso administrativo, se existir, deve exigir role específica
```

A execução local pode simplificar o provedor de identidade, mas não pode remover a autorização por comerciante.

---

## 16. Fora do escopo da implementação mínima

Ficam fora da implementação mínima:

```text
- consulta pública de lançamentos
- estorno e cancelamento
- fechamento manual de dia financeiro
- múltiplas moedas
- múltiplos fusos por comerciante
- calendário bancário
- conciliação contábil
- liquidação financeira
- disaster recovery completo
- alta disponibilidade real
- API administrativa completa de rebuild
```

---

## 17. Critérios de prontidão para iniciar código

Antes de iniciar a implementação funcional, devem existir:

```text
- contrato OpenAPI inicial
- contrato JSON Schema do evento EntryCreated.v1
- decisão documentada de businessDate e cutoff
- regras de idempotência de entrada
- invariantes transacionais do Ledger
- invariantes transacionais do Consolidado
- estratégia inicial de concorrência
- perfil de teste de 50 RPS
- estratégia de autenticação local testável
```

---

## 18. Relação com documentos

Este documento complementa:

```text
- docs/architecture/01-contexto-de-negocio.md
- docs/architecture/02-requisitos-arquiteturais.md
- docs/architecture/05-arquitetura-da-solucao.md
- docs/operations/observabilidade-sli-slo-e-recuperacao.md
- docs/security/arquitetura-de-seguranca.md
- docs/decisions/
```

---

## 19. Status

Documento em rascunho até a criação dos contratos e atualização dos ADRs relacionados.
