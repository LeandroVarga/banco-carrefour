---
adr_id: ADR-0006
titulo: Unidades implantáveis e topologia de runtime
status: Aceita
categoria: Runtime e topologia
altitude_decisoria: Fundacional
data: 2026-07-28
responsavel: Arquitetura de Soluções
---

# ADR-0006 — Unidades implantáveis e topologia de runtime

## 1. Contexto

O Ledger e o Consolidation têm responsabilidades de API e de processamento assíncrono. Se APIs e processamento assíncrono compartilharem o mesmo processo, a escala e a recuperação de um afetam o outro, e uma falha de processamento pode indisponibilizar a API correspondente.

## 2. Pergunta arquitetural

Quais unidades de runtime independentemente implantáveis existem, e qual responsabilidade cada uma possui?

## 3. Decisão

A solução é composta por exatamente quatro workloads de negócio:

- `Ledger.Api` — recebe e registra lançamentos financeiros.
- `Ledger.OutboxPublisher` — publica eventos pendentes da Outbox.
- `Consolidation.Api` — expõe a consulta do consolidado diário.
- `Consolidation.Worker` — consome eventos e atualiza a projeção `DailyBalance`.

Cada workload escala, é implantado e se recupera de forma independente — uma falha ou reinício de um worker não afeta a API correspondente, e vice-versa. Localmente, os quatro rodam em containers Docker via Docker Compose; na AWS de referência, são empacotados em imagens publicadas no Amazon ECR e executados no Amazon ECS Fargate (ADR-0011).

`MigrationRunner` é um artefato operacional de release, nunca um quinto workload de negócio: é uma task ECS/Fargate one-off (`aws ecs run-task`, nunca um `aws_ecs_service`), publicada como imagem própria no Amazon ECR mas classificada separadamente dos quatro componentes de negócio no manifesto de release (ADR-0013) e ausente das visões C4 de container de negócio (ver ADR-0015 para a governança de migrations que ele materializa).

## 4. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar |
|---|---|---|
| Unidade única com API e processamento juntos | Uma aplicação executando registro, publicação, consumo e consulta. | Mistura responsabilidades, impede escala e recuperação independentes. |
| Duas unidades por fronteira (API+worker no mesmo processo) | Uma unidade para Ledger, uma para Consolidation. | Mantém as fronteiras de negócio, mas acopla API e worker no mesmo runtime. |
| Três unidades, publicação da Outbox dentro da API do Ledger | Publicação acoplada ao ciclo da API de escrita. | Reaproxima a publicação assíncrona do caminho crítico de registro. |
| Quatro unidades implantáveis: APIs e workers separados por fronteira | Ledger.Api, Ledger.OutboxPublisher, Consolidation.Worker, Consolidation.Api independentes. | Alternativa adotada. Isola falhas, permite escala independente e mantém clareza operacional. |
| Muitos microsserviços granulares | Dividir cada responsabilidade menor em serviço próprio. | Aumenta complexidade de deploy e observabilidade sem necessidade para o escopo atual. |

## 5. Trade-offs

Quatro unidades exigem configuração, observabilidade e coordenação de deploy próprias por componente, em vez de uma única unidade mais simples de operar. Em compensação, cada workload pode escalar e se recuperar sem afetar os demais.

## 6. Consequências

Cada workload tem sua própria estratégia de deployment por comportamento (ADR-0014): APIs com canary nativo do ECS, o Worker com capacity canary, o Publisher com rolling controlado. `MigrationRunner` nunca aparece nas visões de container de negócio nem nas listas de capacidade — apenas em visões de deployment ou de operação de release.

## 7. Guardrails

- Exatamente quatro workloads de negócio em qualquer listagem de capacidade ou visão C4 de container.
- `MigrationRunner` nunca é listado como quinto workload de negócio, nunca é um `aws_ecs_service`.
- Testes de arquitetura e de governança de release verificam a separação entre os quatro componentes e o artefato operacional.

## 8. Risco arquitetural evitado

Uma implementação futura não deve fundir responsabilidades de API e processamento assíncrono no mesmo processo, nem classificar `MigrationRunner` como workload de negócio.

## 9. ASRs relacionados

ASR-001 (Ledger disponível mesmo com falha do Consolidation), ASR-002/ASR-003 (50 RPS, ≤5% de falha), ASR-010 (fluxo observável), ASR-011 (falhas recuperáveis).

## 10. ABBs e SBBs relacionados

ABB-001 (Fronteira de Lançamentos), ABB-006 (Publicação Recuperável), ABB-008 (Fronteira de Consolidado), ABB-012 (API de Consulta do Consolidado); SBB-001 (Ledger.Api), SBB-006 (Ledger.OutboxPublisher), SBB-008 (Consolidation.Worker), SBB-012 (Consolidation.Api), SBB-018 (Containers e Runtime Local).

## 11. Evidências de implementação

`src/Ledger/{Ledger.Api,Ledger.OutboxPublisher}`, `src/Consolidation/{Consolidation.Api,Consolidation.Worker}`, `src/Migrations/MigrationRunner`, `docker-compose.yml`, `infra/terraform/modules/{ecs-service-api,ecs-service-worker,ecs-service-publisher,ecs-migration-task}`, `schemas/release-manifest.schema.json` (`components` versus `operationalArtifacts`).

## 12. ADRs relacionados

ADR-0003 (arquitetura hexagonal), ADR-0011 (plataforma AWS e isolamento de ambientes), ADR-0013 (integridade de release e software supply chain), ADR-0015 (governança de migrations de banco de dados).
