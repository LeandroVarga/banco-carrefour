---
adr_id: ADR-0013
titulo: Integridade de release e software supply chain
status: Aceita
categoria: Entrega
altitude_decisoria: Estrutural
data: 2026-07-28
responsavel: Arquitetura de Soluções
---

# ADR-0013 — Integridade de release e software supply chain

## 1. Contexto

Uma release precisa ter identidade única, rastreável e imutável, e a cadeia de suprimentos das imagens precisa ser auditável antes de qualquer publicação — sem essas garantias, não é possível afirmar com confiança que a imagem publicada corresponde exatamente ao código escaneado e testado.

## 2. Pergunta arquitetural

Como uma release imutável, qualificada e rastreável é construída e promovida com segurança?

## 3. Decisão

O Amazon ECR é o único registry oficial da solução. As imagens são construídas uma única vez (build once) a partir de uma árvore de trabalho limpa — nunca reconstruídas por ambiente. A tag canônica é imutável, `sha-<SHA completa de 40 caracteres>`, nunca `latest` nem outra tag mutável; a identidade de release inclui o digest da imagem publicada.

O manifesto de release (`schemas/release-manifest.schema.json`, versão 4.0.0) separa estruturalmente `components` — exatamente os quatro workloads de negócio — de `operationalArtifacts` — o `migration-runner`, nunca misturado à primeira coleção. Antes de qualquer publicação, cada imagem passa por: verificação non-root, geração de SBOM (CycloneDX), varredura de vulnerabilidade com política de exceção documentada (achados sem correção disponível na imagem base são reportados, nunca bloqueantes; achados corrigíveis do próprio código ou dependências bloqueiam a release), inventário de licenças, CodeQL e Dependabot. Uma execução única de qualificação de release (Compose efêmero, mesmo job do build once) valida isolamento entre Ledger e Consolidation e o smoke de 50 RPS antes de qualquer publicação.

A publicação real usa identidade federada via GitHub OIDC, nunca credencial estática. Atestações de proveniência e de SBOM são geradas e associadas ao digest real publicado. O estado de publicação e de atestação é sempre honesto: pendente até que a execução hospedada realmente ocorra — nunca fabricado como concluído. O disparo do workflow de publicação é exclusivamente manual (`workflow_dispatch`) e restrito ao ref `main` — a trust policy OIDC da role de publicação só autoriza esse ref, e o preflight (`check-release-prerequisites.sh`) rejeita qualquer outro antes de qualquer trabalho caro. Nenhuma publicação real no ECR e nenhuma atestação hospedada foram realizadas neste repositório. Uma execução hospedada real ocorreu — quando o workflow ainda disparava automaticamente em cada push para `main` — e falhou de forma limpa e segura no preflight, antes de qualquer chamada à AWS, por ausência das variáveis de repositório obrigatórias (`AWS_REGION`, `ECR_PUBLISHER_ROLE_ARN`, `ECR_REGISTRY`); essa execução confirmou o comportamento pretendido do preflight e motivou a correção do gatilho para disparo manual, evitando que todo push para `main` produza uma execução vermelha enquanto o ambiente real de publicação AWS permanecer intencionalmente não configurado. O rollback de uma release sempre reutiliza uma imagem já construída e qualificada — nunca reconstrói ou publica uma nova imagem durante o rollback (ver ADR-0014).

## 4. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar |
|---|---|---|
| GitHub Container Registry (GHCR) como alvo de produção | Publicação real no GHCR com ambientes de promoção simulados por nome. | Rejeitada. Criou um caminho de release paralelo e nunca convergente ao alvo real (Amazon ECR/AWS); os "ambientes" de promoção eram a mesma stack Compose local repetida sob nomes diferentes, nunca uma aproximação real de ECS/RDS/IAM/ALB/WAF. GHCR não é nem foi o alvo de produção deste repositório. |
| Reconstrução de imagem por ambiente | Cada ambiente builda sua própria imagem a partir do mesmo código-fonte. | Quebra a garantia de que a imagem qualificada é exatamente a que é implantada; introduz risco de divergência entre builds. |
| Tag mutável (`latest`) como identidade de deployment | Apontar sempre para a imagem mais recente sob uma tag fixa. | Impede rastrear exatamente qual commit está implantado e torna rollback ambíguo. |
| Amazon ECR único registry, build once, tag imutável por SHA completa, qualificação pré-publicação obrigatória | Identidade rastreável e imutável, qualificada antes de qualquer publicação. | Alternativa adotada. Garante que a imagem publicada é exatamente a imagem escaneada e testada. |

## 5. Trade-offs

Build once e tag imutável exigem disciplina de versionamento e de idempotência de publicação (nunca sobrescrever uma tag existente), em troca da garantia de que a imagem publicada é exatamente a que foi qualificada.

## 6. Consequências

O manifesto de release nunca declara publicação total quando apenas parte dos componentes foi publicada; scripts de publicação consultam o estado remoto antes de qualquer push e nunca sobrepõem uma tag imutável já existente com conteúdo diferente.

## 7. Guardrails

- Nenhuma imagem é reconstruída após a qualificação — o mesmo `imageId` local segue do build até a publicação.
- Nenhuma tag mutável é usada como identidade de deployment.
- Nenhuma evidência de publicação ou atestação é fabricada sem execução hospedada real correspondente.
- GHCR e ambientes de promoção simulados nunca são tratados como caminho oficial.
- O workflow de publicação nunca dispara automaticamente em push — somente `workflow_dispatch` manual, a partir de `main`.

## 8. Risco arquitetural evitado

Uma implementação futura não deve reconstruir, retaguear ou substituir a imagem de aplicação após a qualificação, nem alegar evidência de publicação que não existe.

## 9. ASRs relacionados

ASR-004 (lançamentos confiáveis, por extensão de confiabilidade da entrega), ASR-010 (fluxo observável), ASR-011 (falhas recuperáveis).

## 10. ABBs e SBBs relacionados

ABB-013 (Observabilidade do Fluxo); SBB-018 (Containers and Local Runtime).

## 11. Evidências de implementação

`infra/terraform/modules/ecr`, `infra/terraform/environments/aws-reference`, `.github/workflows/publish-images.yml`, `scripts/ci/{build-images-for-supply-chain,generate-sboms,scan-images,generate-license-inventory,generate-release-manifest,validate-release-manifest,publish-validated-images,record-release-attestations}.sh`, `schemas/release-manifest.schema.json`, `docs/security/dependencias-e-supply-chain.md`, `tests/Architecture.Tests/{ReleaseManifestGovernanceArchitectureTests,ReleaseInfrastructureGovernanceArchitectureTests,AttestationGovernanceArchitectureTests}.cs`.

## 12. ADRs relacionados

ADR-0006 (unidades implantáveis e topologia de runtime), ADR-0009 (menor privilégio, secrets e criptografia), ADR-0011 (plataforma AWS e isolamento de ambientes), ADR-0014 (promoção, deployment e rollback por workload).
