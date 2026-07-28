---
adr_id: ADR-0005
titulo: Contratos HTTP e eventos de integração
status: Aceita
categoria: Contratos
altitude_decisoria: Estrutural
data: 2026-07-28
responsavel: Arquitetura de Soluções
---

# ADR-0005 — Contratos HTTP e eventos de integração

## 1. Contexto

O Ledger expõe registro e o Consolidation expõe consulta por contratos HTTP. A integração entre as duas fronteiras usa um evento de integração assíncrono (ADR-0004). Sem contratos explícitos e versionados, cada componente poderia interpretar de forma diferente o payload de registro, a chave de idempotência, a semântica de `businessDate` e o formato do evento.

## 2. Pergunta arquitetural

Como os contratos HTTP externos e os eventos de integração são governados e evoluídos de forma segura?

## 3. Decisão

Os contratos HTTP são descritos em OpenAPI (`contracts/openapi.yaml`): `POST /entries` para registro de lançamento e `GET /daily-balances/{businessDate}` para consulta do consolidado. `Idempotency-Key` é obrigatória no registro; autenticação usa Bearer JWT (ADR-0007); `merchantId` nunca é aceito no corpo da requisição — deriva sempre do token.

O evento de integração atual é `FinancialEntryRegistered.v1` (`contracts/events/financial-entry-registered-v1.schema.json`, JSON Schema), publicado pelo Ledger a cada lançamento aceito. O evento carrega `merchantId`, `occurredAt`, `businessDate`, o identificador do lançamento e `registeredAt` — o instante em que o Ledger persistiu o lançamento, distinto de `occurredAt`. Não há dual publish: o Ledger publica exclusivamente `FinancialEntryRegistered.v1` para novos lançamentos.

Mudanças de contrato seguem compatibilidade aditiva sempre que possível: novos campos opcionais, nunca remoção ou mudança de tipo de campo existente sem nova versão explícita do evento ou do endpoint. Durante uma promoção canary, a versão anterior e a nova versão de um workload podem coexistir por um período — o contrato precisa permanecer compatível com ambas simultaneamente (ver ADR-0014).

## 4. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar |
|---|---|---|
| Implementar primeiro, documentar depois | O código definiria implicitamente payloads e eventos. | Aumenta risco de inconsistência e enfraquece a rastreabilidade entre arquitetura, contrato e código. |
| Contratos apenas em Markdown | Descrição em texto livre. | Não favorece validação automatizada nem uso por ferramentas de teste de contrato. |
| AsyncAPI completo desde o início | Especificação própria para todo o fluxo assíncrono. | Adiciona complexidade documental sem necessidade concreta para o escopo atual. |
| OpenAPI para HTTP e JSON Schema para o evento, com versionamento explícito e compatibilidade aditiva | Contratos estruturados, versionáveis e verificáveis por teste. | Alternativa adotada. Equilibra clareza, automação e disciplina de evolução. |

## 5. Trade-offs

Manter contratos explícitos exige disciplina de atualização a cada mudança de payload e testes de contrato dedicados — um custo aceito em troca de rastreabilidade e de proteção contra quebra silenciosa de compatibilidade entre produtor e consumidor.

## 6. Consequências

Testes de contrato validam que o schema real do evento e o OpenAPI publicado permanecem sincronizados com a implementação. Qualquer mudança de payload passa pela revisão explícita do contrato antes de chegar ao código.

## 7. Guardrails

- Nenhuma mudança de payload HTTP ou de evento sem atualização explícita do contrato correspondente.
- `merchantId` nunca é um campo aceito no corpo de `POST /entries` — deriva exclusivamente do token.
- Testes de contrato automatizados validam o schema real do evento contra o JSON Schema publicado.

## 8. Risco arquitetural evitado

Uma implementação futura não deve alterar o payload HTTP ou o evento de integração de forma incompatível sem versionamento explícito e análise de compatibilidade com consumidores existentes.

## 9. ASRs relacionados

RF-001/RF-002 (registrar crédito/débito), RF-005 (consultar consolidado diário), ASR-006 (idempotência de entrada), ASR-007 (eventos duplicados não duplicam efeito), ASR-009 (acesso autenticado e autorizado por comerciante).

## 10. ABBs e SBBs relacionados

ABB-004 (Idempotência de Entrada), ABB-007 (Canal Assíncrono Confiável), ABB-009 (Consumo Idempotente); SBB-013 (API Contracts).

## 11. Evidências de implementação

`contracts/openapi.yaml`, `contracts/events/financial-entry-registered-v1.schema.json`, `contracts/README.md`, `tests/ContractTests/ContractValidationTests.cs`.

## 12. ADRs relacionados

ADR-0000 (semântica financeira e data de negócio), ADR-0004 (integração assíncrona confiável), ADR-0007 (identidade, autorização e isolamento por merchant), ADR-0014 (promoção, deployment e rollback por workload).
