---
adr_id: ADR-0014
titulo: Promoção, deployment e rollback por workload
status: Aceita
categoria: Entrega
altitude_decisoria: Estrutural
data: 2026-07-28
responsavel: Arquitetura de Soluções
---

# ADR-0014 — Promoção, deployment e rollback por workload

## 1. Contexto

Os quatro workloads de negócio têm perfis de tráfego diferentes: dois expõem HTTP atrás de um ALB, um consome uma fila sem endpoint HTTP, e um publica eventos sem receber tráfego externo. Uma única estratégia de deployment aplicada indistintamente a todos não é mecanicamente válida para o Worker, que não tem ALB, nem apropriada ao Publisher, que não precisa de canary de tráfego HTTP.

## 2. Pergunta arquitetural

Como a mesma release imutável é promovida e implantada de acordo com o comportamento de cada workload?

## 3. Decisão

Development, Staging e Production são os únicos ambientes de deployment reais, mapeados a GitHub Environments com os mesmos nomes — nunca ambientes simulados. A mesma imagem, identificada pelo digest qualificado (ADR-0013), é promovida sem rebuild entre os três ambientes.

`Ledger.Api` e `Consolidation.Api` usam a estratégia nativa `CANARY` do ECS: desloca uma fração do tráfego para a nova revisão via o ALB, observa por um tempo de espera, completa para 100% e só então encerra a revisão anterior, com rollback automático via alarme (5xx, latência do ALB). `Consolidation.Worker`, sem ALB, usa capacity canary: dois serviços ECS independentes (primary e canary) consumindo a mesma fila sob o mesmo modelo de idempotência — a fração de mensagens processada pelo canário é aproximada e dirigida pela capacidade relativa entre os dois serviços, nunca uma porcentagem exata de tráfego. `Ledger.OutboxPublisher` usa rolling controlado, sem canary artificial de HTTP: capacidade mínima de 100% durante o rollout, circuit breaker de deployment e um tempo de parada que permite concluir ou liberar com segurança o trabalho já reivindicado antes de encerrar.

A versão antiga e a nova coexistem durante a janela de observação de cada estratégia — por isso os contratos de evento e de API precisam permanecer compatíveis durante essa coexistência (ADR-0005). Alarmes de deployment propagam falha automaticamente para as três estratégias. Rollback nunca reconstrói a imagem: sempre promove uma release imutável anterior já qualificada. Nenhuma dessas estratégias é chamada de "rolling" quando na verdade é canário, nem de "canário" quando é apenas rolling simples.

## 4. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar |
|---|---|---|
| Uma única estratégia de rolling deployment para todos os workloads | Aplicar rolling simples a APIs, Worker e Publisher indistintamente. | Renomear rolling simples como "canário" contraria a semântica real de traffic-shift esperada para as APIs; não oferece o mesmo controle de risco durante promoção. |
| CodeDeploy como mecanismo de canary | Usar o CodeDeploy para orquestrar o traffic-shift do ECS. | O ECS já oferece estratégias de deployment nativas (`ROLLING`/`LINEAR`/`CANARY`/`BLUE_GREEN`), sem exigir um serviço adicional para o caso de uso atual. |
| Reconstruir a imagem a cada promoção ou rollback | Buildar novamente o artefato para cada ambiente ou durante o rollback. | Quebra a garantia de build once e de identidade de release imutável (ADR-0013). |
| CANARY nativo do ECS para APIs, capacity canary de dois serviços para o Worker, rolling controlado para o Publisher | Estratégia escolhida conforme o comportamento real de cada workload. | Alternativa adotada. Cada estratégia é mecanicamente válida para o tipo de tráfego do respectivo workload. |

## 5. Trade-offs

Manter três estratégias de deployment distintas exige mais Terraform e mais documentação do que uma única estratégia uniforme, em troca de um mecanismo de rollout realmente adequado ao comportamento de cada workload.

## 6. Consequências

Migrações de banco seguem uma disciplina EXPAND-primeiro (ADR-0015) para que a versão antiga, ainda em execução durante a janela de canário ou rolling, continue operando normalmente contra o schema já alterado, sem erro.

## 7. Guardrails

- Nenhuma promoção ou rollback reconstrói a imagem — sempre reutiliza um digest já qualificado.
- Nenhuma estratégia de rolling simples é apresentada como canário.
- Alarmes de deployment cobrem os três workloads com estratégia de traffic-shift ou capacidade.

## 8. Risco arquitetural evitado

Uma implementação futura não deve aplicar uma única estratégia de deployment mecanicamente inválida a todos os workloads, nem reconstruir artefatos durante promoção ou rollback.

## 9. ASRs relacionados

ASR-001 (Ledger disponível mesmo com falha do Consolidation), ASR-002/ASR-003 (50 RPS, ≤5% de falha), ASR-010 (fluxo observável), ASR-011 (falhas recuperáveis).

## 10. ABBs e SBBs relacionados

ABB-006 (Publicação Recuperável), ABB-014 (Recuperação Operacional); SBB-001/SBB-006/SBB-008/SBB-012 (unidades implantáveis).

## 11. Evidências de implementação

`infra/terraform/modules/{ecs-service-api,ecs-service-worker,ecs-service-publisher,deployment-alarms}`, `.github/workflows/{deploy-development,promote-staging,promote-production,rollback-production}.yml`, `tests/Architecture.Tests/AwsPlatformGovernanceArchitectureTests.cs` (testes de canary/capacity-canary/rolling e do guard de rollback).

## 12. ADRs relacionados

ADR-0005 (contratos HTTP e eventos de integração), ADR-0006 (unidades implantáveis e topologia de runtime), ADR-0011 (plataforma AWS e isolamento de ambientes), ADR-0013 (integridade de release e software supply chain), ADR-0015 (governança de migrations de banco de dados).
