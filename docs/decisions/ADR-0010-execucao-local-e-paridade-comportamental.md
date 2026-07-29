---
adr_id: ADR-0010
titulo: Execução local e paridade comportamental
status: Aceita
categoria: Runtime e topologia
altitude_decisoria: Estrutural
data: 2026-07-28
responsavel: Arquitetura de Soluções
---

# ADR-0010 — Execução local e paridade comportamental

## 1. Contexto

O caso precisa ser avaliável sem exigir uma conta AWS, e ao mesmo tempo precisa demonstrar um comportamento próximo o suficiente da referência AWS para que a avaliação local seja significativa. Sem uma definição clara do que a execução local representa, ela poderia ser confundida com uma topologia de produção.

## 2. Pergunta arquitetural

O que o ambiente local representa, e onde a paridade com a AWS deliberadamente se limita?

## 3. Decisão

Docker Compose orquestra três cenários locais: desenvolvimento (`docker-compose.yml`), testes de integração (containers efêmeros via Testcontainers, sem exigir `docker compose up` prévio) e qualificação de release efêmera (`docker-compose.release-qualification.yml`, ADR-0013). Nenhum desses cenários representa um ambiente AWS persistente — Compose nunca simula Development, Staging ou Production como ambientes duradouros, e não existe promoção baseada em Compose entre esses nomes.

O ambiente local materializa: LocalStack com filas compatíveis com SQS Standard e DLQ (ADR-0004), Keycloak como Identity Provider OIDC local (ADR-0007), dois containers PostgreSQL independentes (Ledger e Consolidation, ADR-0002) e `edge-proxy` com TLS (ADR-0008). A paridade buscada é comportamental — mesmo ack, retry, DLQ e redrive de mensageria; mesmo modelo de autenticação e autorização — nunca paridade literal de produto ou de infraestrutura gerenciada. O ambiente local nunca introduz um broker alternativo com semântica operacional própria de ack e retry: SQS-compatível via LocalStack garante o mesmo comportamento observado na referência AWS.

Execução via containers evita exigir todo SDK ou ferramenta instalada diretamente no host do avaliador. Limitações de evidência do LocalStack — como a ausência de enforcement real de política IAM — são documentadas explicitamente e nunca apresentadas como prova de comportamento equivalente ao serviço gerenciado real.

## 4. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar |
|---|---|---|
| Execução manual local, sem containers | Rodar APIs, workers, banco e broker manualmente na máquina do avaliador. | Aumenta variação de ambiente e fragiliza a reprodução. |
| Compose simulando Development/Staging/Production como ambientes persistentes | Nomear stacks Compose locais com os nomes dos ambientes AWS reais e promover entre elas. | Cria confusão entre o que foi de fato executado em AWS e o que é apenas uma demonstração local, sem nenhuma aproximação real de ECS/RDS/IAM/ALB/WAF. |
| Um broker local com semântica operacional distinta do Amazon SQS | Produto alternativo com semântica própria de ack e retry, divergente do alvo AWS. | Reduz a paridade comportamental entre local e AWS (ver ADR-0004). |
| Docker Compose com paridade comportamental explícita, sem simular ambientes persistentes | LocalStack, Keycloak, dois PostgreSQL e edge-proxy, com limites de evidência documentados. | Alternativa adotada. Torna a avaliação local significativa sem fingir ser uma topologia de produção. |

## 5. Trade-offs

A paridade comportamental exige manter o LocalStack e os demais emuladores alinhados ao comportamento real da AWS, sem garantir equivalência de segurança operacional (HSM, CloudTrail, enforcement de IAM). Isso é aceito como limitação documentada, não como lacuna oculta.

## 6. Consequências

Qualquer capacidade que dependa de comportamento real de conta AWS (enforcement de IAM, criptografia gerenciada completa, alta disponibilidade real) é executada e comprovada apenas na referência AWS (ADR-0011), nunca alegada como comprovada localmente.

## 7. Guardrails

- Docker Compose nunca representa Development, Staging ou Production como ambiente persistente.
- Nenhuma promoção entre ambientes ocorre via Compose.
- Um broker local com semântica operacional distinta do Amazon SQS nunca é reintroduzido no ambiente local (ver ADR-0004).
- Limitações de evidência do LocalStack são sempre documentadas explicitamente quando relevantes.

## 8. Risco arquitetural evitado

Uma implementação futura não deve apresentar Docker Compose como um ambiente de implantação AWS, nem substituir o comportamento do Amazon SQS por um broker alternativo com semântica operacional distinta no ambiente local.

## 9. ASRs relacionados

ASR-002/ASR-003 (50 RPS, ≤5% de falha), ASR-010 (fluxo observável), ASR-011 (falhas recuperáveis).

## 10. ABBs e SBBs relacionados

ABB-013 (Observabilidade do Fluxo), ABB-014 (Recuperação Operacional); SBB-018 (Containers and Local Runtime).

## 11. Evidências de implementação

`docker-compose.yml`, `deploy/compose/docker-compose.release-qualification.yml`, `docs/operations/runbook-demonstracao-local.md`, `docs/security/threat-model.md`, `scripts/ci/run-release-qualification.sh`.

## 12. ADRs relacionados

ADR-0004 (integração assíncrona confiável), ADR-0007 (identidade, autorização e isolamento por merchant), ADR-0008 (proteção de borda e conectividade privada), ADR-0011 (plataforma AWS e isolamento de ambientes), ADR-0013 (integridade de release e software supply chain).
