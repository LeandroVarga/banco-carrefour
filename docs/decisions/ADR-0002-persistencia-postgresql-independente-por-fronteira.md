---
adr_id: ADR-0002
titulo: Persistência PostgreSQL independente por fronteira
status: Aceita
categoria: Persistência
altitude_decisoria: Fundacional
data: 2026-07-28
responsavel: Arquitetura de Soluções
---

# ADR-0002 — Persistência PostgreSQL independente por fronteira

## 1. Contexto

O Ledger e o Consolidation são fronteiras arquiteturais distintas (ADR-0001). Se compartilharem a mesma persistência, a separação perde força: consultas cruzadas, acoplamento de schema e evolução conjunta reintroduziriam exatamente o acoplamento que a separação de fronteiras busca evitar.

## 2. Pergunta arquitetural

Como a propriedade dos dados e os limites de persistência entre Ledger e Consolidation são implementados?

## 3. Decisão

Ledger e Consolidation têm bancos PostgreSQL independentes. Localmente, cada fronteira roda em um container PostgreSQL próprio; na AWS de referência, cada fronteira tem sua própria instância Amazon RDS for PostgreSQL — nunca uma instância compartilhada.

O banco do Ledger é dono de `FinancialEntry`, `InputIdempotency` e da tabela de Outbox. O banco do Consolidation é dono de `DailyBalance` e `ProcessedEvent`. Nenhuma fronteira acessa diretamente as tabelas internas da outra — a única integração é o canal assíncrono (ADR-0004).

## 4. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar |
|---|---|---|
| Banco único compartilhado | Ledger e Consolidation usam a mesma base física. | Aumenta acoplamento entre fronteiras e cria risco de o Consolidation depender da estrutura interna do Ledger. |
| Mesmo banco físico, schemas separados | Separação apenas lógica dentro da mesma instância. | Reduz isolamento operacional (backup, escala, recuperação) e não reflete a independência real exigida entre fronteiras. |
| Consolidation lendo diretamente as tabelas do Ledger | Consulta calculada a partir da persistência do Ledger. | Reaproxima a leitura consolidada da fonte transacional e aumenta custo de leitura sob pico. |
| Bancos PostgreSQL independentes por fronteira | Cada fronteira controla sua própria persistência, integrando-se por fluxo assíncrono. | Alternativa adotada. Preserva isolamento, evolução independente e reconstrução controlada do Consolidation. |

## 5. Trade-offs

Persistências independentes custam mais operacionalmente do que uma única base — duas instâncias para provisionar, monitorar e fazer backup — e exigem reconciliação assíncrona entre registro e projeção em vez de consistência transacional imediata entre as duas fronteiras.

## 6. Consequências

Backup, restore, escala e migrations de schema são independentes por fronteira (ver ADR-0015 para governança de migrations). RDS Multi-AZ, proteção contra deleção e retenção de backup são configuráveis por ambiente e por instância (ver ADR-0011).

## 7. Guardrails

- Nenhuma consulta cruzada direta entre o banco do Ledger e o banco do Consolidation em nenhum ambiente.
- Cada instância RDS pertence exclusivamente a uma fronteira — nunca compartilhada entre Ledger e Consolidation.
- Roles PostgreSQL de menor privilégio, distintas por componente dentro de cada fronteira (ver ADR-0009).

## 8. Risco arquitetural evitado

Uma implementação futura não deve unificar os dois domínios em um único schema compartilhado, nem usar consultas entre fronteiras como mecanismo de integração no lugar do canal assíncrono.

## 9. ASRs relacionados

ASR-001 (Ledger disponível mesmo com falha do Consolidation), ASR-004 (lançamentos confiáveis), ASR-008 (estrutura de leitura adequada).

## 10. ABBs e SBBs relacionados

ABB-002 (Fonte de Verdade Financeira), ABB-003 (Persistência Transacional de Lançamentos), ABB-011 (Persistência do Consolidado); SBB-002 (Ledger Database), SBB-003 (Entries), SBB-005 (Outbox), SBB-009 (Consolidation Database), SBB-010 (Processed Events), SBB-011 (DailyBalance).

## 11. Evidências de implementação

`src/Ledger/Ledger.Infrastructure/LedgerDbContext.cs`, `src/Consolidation/Consolidation.Infrastructure/ConsolidationDbContext.cs`, `infra/postgres/{ledger,consolidation}/db-role-bootstrap.sql`, `infra/terraform/modules/rds-postgresql`, `infra/terraform/environments/{development,staging,production}/main.tf` (`module "rds_ledger"`, `module "rds_consolidation"`).

## 12. ADRs relacionados

ADR-0001 (fronteiras Ledger e Consolidation), ADR-0009 (menor privilégio, secrets e criptografia), ADR-0011 (plataforma AWS e isolamento de ambientes), ADR-0015 (governança de migrations de banco de dados).
