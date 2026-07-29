---
doc_id: ARCH-009
titulo: Escopo, Priorização e Limites
versao: 2.3
status: Atualizado
responsavel: Arquitetura de Soluções
ultima_atualizacao: 2026-07-27
etapa_relacionada: Definition and Decision
---

# Escopo, Priorização e Limites

## 1. Objetivo da entrega

Este documento formaliza, em um único lugar, o escopo, a priorização (MoSCoW), os riscos, as premissas, as dependências e os adiamentos deliberados de **todo o case**, cobrindo identidade OIDC/RS256, borda HTTPS/WAF, menor privilégio PostgreSQL, Secrets Manager/SSM/KMS/IAM locais, plataforma AWS de referência e governança de release, deployment e migração.

O histórico de implementação permanece registrado no histórico Git e nas branches locais de segurança preservadas; este documento reflete o estado atual da arquitetura, não uma linha do tempo de implementação.

## 2. Estado local comprovado versus referência AWS

| Camada | Prova local (execução real) | Referência AWS |
|---|---|---|
| Identidade | Keycloak real, OIDC/RS256, discovery, JWKS, audiences distintas por API, `merchant_id` derivado do token (ADR-0007). | IdP corporativo OIDC/OAuth2 ou Amazon Cognito. |
| Borda | edge-proxy real (nginx + ModSecurity/OWASP CRS), HTTPS, WAF em modo bloqueio, rate limiting (ADR-0008). | API Gateway + AWS WAF + VPC Link V2/ALB interno. |
| Banco de dados | PostgreSQL real, menor privilégio por componente, roles/grants comprovados negativamente (ADR-0009). | RDS PostgreSQL com IAM/security groups e criptografia KMS. |
| Mensageria | SQS via LocalStack real, Outbox durável, consumo idempotente, DLQ configurada (ADR-0004). | SQS gerenciado com IAM por produtor/consumidor. |
| Secrets/config | Secrets Manager/SSM/KMS via LocalStack real, um secret por componente, SSM como fonte autoritativa de issuer/audience, rotação ponta a ponta comprovada por execução real (ADR-0009). | Secrets Manager/SSM reais, com `aws_secretsmanager_secret_rotation`. |
| IAM | Roles/policies provisionadas como estrutura/referência; **enforcement de policy não é comprovável no LocalStack Hobby usado neste projeto** (achado real, ver `docs/security/threat-model.md`). | IAM role por task ECS com enforcement real. |
| IaC | Terraform aplicado localmente contra LocalStack (`environments/localstack-hobby`) — init/validate/plan/apply/destroy reais. Ambientes `aws-reference`/`development`/`staging`/`production` (ADR-0011) validados só com `init`/`validate`/`plan` (credenciais falsas, sem tocar AWS real). | Nenhum `apply` contra AWS real em nenhuma etapa, em nenhum ambiente. |
| Observabilidade | OpenTelemetry real (traces/métricas/logs) + Aspire Dashboard local. | ADOT, CloudWatch, X-Ray. |
| Carga/desempenho | Smoke de 50 RPS/≤5% de falhas elegíveis automatizado em CI (`performance-smoke-gate`), contra o Consolidation.Api real com autenticação Keycloak real (ADR-0012). | Autoscaling ECS + CloudWatch Alarms produtivos (não executado). |
| CI | 5 gates por camada de teste (unitário/contrato/arquitetura, integração, segurança, imagem non-root, desempenho), permissions mínimas, actions fixadas por commit SHA (ADR-0012). | GitHub Actions com OIDC para AWS e publicação ECR implementados e validados localmente (ADR-0013) — deploy ECS materializado e validado estruturalmente (ADR-0014), nunca executado contra AWS real. |
| Supply chain | SBOM (CycloneDX) e scan de vulnerabilidade das 4 imagens reais (Trivy fixado por digest), auditoria NuGet nativa, revisão de dependências em PR, CodeQL e Dependabot (ADR-0013). Vulnerabilidade real encontrada e corrigida (`System.Text.Json`). | Identidade OIDC, 4 repositórios ECR e workflow de publicação build-once implementados e validados localmente (ADR-0013) — publicação real de imagem e atestação de proveniência não executadas. |

## 3. Escopo funcional

In scope:

- registro de lançamentos financeiros de crédito e débito, com idempotência por comerciante e chave;
- consolidação diária assíncrona via Outbox → SQS → Worker → projeção materializada;
- consulta do consolidado diário por comerciante e data;
- isolamento de dados entre comerciantes (`merchant_id` derivado do token, nunca do payload/header/query);
- autenticação OIDC/RS256 e autorização por escopo (`ledger.write`, `consolidation.read`);
- disponibilidade do Ledger independente de falha/indisponibilidade do Consolidado.

Out of scope:

- aprovação, confirmação, liquidação, cancelamento, estorno ou fechamento manual de lançamentos;
- múltiplas moedas e múltiplos fusos por comerciante (BRL/`America/Sao_Paulo` fixos no MVP);
- consulta pública de lançamentos individuais (só o consolidado diário é exposto);
- rebuild/reprocessamento operacional completo da projeção.

## 4. Escopo técnico

In scope (implementado e comprovado por execução real):

- Arquitetura Hexagonal em 4 unidades deployáveis (`Ledger.Api`, `Ledger.OutboxPublisher`, `Consolidation.Worker`, `Consolidation.Api`), cada uma com Domain/Application/Infrastructure próprios;
- persistências independentes por fronteira (PostgreSQL `ledger`/`consolidation`), com roles de menor privilégio distintas por componente;
- Outbox transacional com claim recuperável (`FOR UPDATE SKIP LOCKED`) e retry com backoff exponencial;
- SQS via LocalStack como canal assíncrono, com DLQ configurada por `redrive_policy`;
- Keycloak real como IdP local, HTTPS/WAF na borda via edge-proxy real;
- Secrets Manager/SSM/KMS/IAM via LocalStack, Terraform como IaC de referência (containers lógicos, nunca valor de secret no state);
- rotação de credencial de banco ponta a ponta, comprovada por execução real contra a stack completa (`scripts/security/rotate-database-credential.sh`);
- ambiente Terraform `aws-reference` (ECR, OIDC, IAM de publicação — ADR-0011, ADR-0013), validado com `init`/`validate`/`plan`, nunca aplicado;
- 16 ADRs, testes de domínio/aplicação/integração/arquitetura/segurança/contrato, documentação de rastreabilidade e evidências.

Out of scope (deliberadamente, sem ambiguidade):

- MediatR, CQRS cerimonial, repository genérico, Unit of Work genérico, projeto `Common`, `BaseEntity`, event sourcing;
- múltiplos workers/publishers concorrentes com coordenação distribuída (um único `Ledger.OutboxPublisher`/`Consolidation.Worker` por ambiente local);
- redrive assistido de DLQ (ferramenta de reprocessamento manual/automático);
- qualquer `apply`/`destroy` contra AWS real, em qualquer ambiente Terraform;
- publicação real de imagem no ECR, assunção real de role OIDC, execução hospedada do workflow de publicação, deploy AWS real (fora desta entrega — o Terraform e o workflow já estão implementados e validados localmente, ver ADR-0013).

## 5. Riscos

| Risco | Tratamento |
|---|---|
| Enforcement de IAM não comprovável no LocalStack Hobby usado | Aceito e documentado (`docs/security/threat-model.md`, item 1); controle compensatório real é a disciplina da aplicação (cada componente só solicita, por configuração, o secret/parâmetro que lhe pertence — comprovado por teste). Nunca tratado como enforcement real. |
| Rotação de credencial sem janela de indisponibilidade | Não implementado (exigiria dual-user ou serviço gerenciado de rotação); aceito como trade-off documentado (ADR-0009). Pool de conexões pode manter conexões abertas até reciclagem. |
| KMS local não equivale a AWS KMS gerenciado | Aceito; prova local é de infraestrutura/integração (round-trip real), não de equivalência de segurança criptográfica com HSM/CloudTrail. |
| Backlog com evento legado `EntryCreated.v1` | Consumidor aceita temporariamente o evento legado (ver ADR-0005). |
| API Gateway REST v1 local instável (LocalStack) | Mantido fora do caminho quente; nunca reintroduzido sem evidência de estabilidade. |

## 6. Premissas

- Ledger é a fonte autoritativa dos lançamentos financeiros; `businessDate` é derivada de `occurredAt` em `America/Sao_Paulo`.
- Lançamentos são imutáveis no escopo atual; lançamentos retroativos são aceitos.
- BRL é a única moeda do MVP.
- O ambiente local (Docker Compose + LocalStack) é reproduzível e é o caminho oficial de avaliação deste case — não representa a topologia definitiva de produção.
- SSM/Secrets Manager/KMS/IAM locais (LocalStack Hobby) provam integração e desenho, não paridade de segurança operacional com os serviços gerenciados reais.

## 7. Dependências

- Docker + Docker Compose como caminho oficial de execução, build e teste (container-first, sem exigir .NET SDK/Node/Python instalados no host para o fluxo operacional).
- PostgreSQL, LocalStack (SQS/Secrets Manager/SSM/KMS/IAM) e Keycloak reais, todos containerizados.
- Terraform (`hashicorp/terraform:1.9`, containerizado) para o ambiente `localstack-hobby`.
- JSON Schema e OpenAPI como contratos externos (`contracts/`).

## 8. Adiamentos deliberados (próximo ciclo ou fora de escopo local)

- Redrive assistido de DLQ e rebuild/reprocessamento operacional completo.
- Rotação automática agendada de secrets (hoje manual, porém ponta a ponta comprovada).
- Estratégia de rotação sem janela de indisponibilidade (dual-user).
- `terraform apply`/`destroy` real contra qualquer ambiente (`aws-reference`, `development`, `staging`, `production` existem e estão validados - ADR-0011 - mas nunca foram aplicados).
- Deploy ECS real, rollout de task definition real, canary/capacity-canary/rolling real, rollback produtivo real (ver `deploy/ecs/README.md` e ADR-0014 - Terraform de rede/ECS/RDS/SQS/edge/IAM/observabilidade e as estratégias de deployment por workload já estão materializados e validados localmente; o que falta é especificamente a execução real).
- Publicação real de imagem no ECR (único registry oficial - ADR-0013), assunção real da role de publicação via OIDC, execução hospedada do workflow `publish-images.yml` — o Terraform (4 repositórios ECR, identidade OIDC, IAM de menor privilégio), o workflow (build once → release-qualification → validar → publicar → atestar) e a atestação real de proveniência/SBOM (`actions/attest-build-provenance`/`actions/attest`, associada ao digest ECR real) já estão implementados e validados localmente; o que falta é especificamente a execução hospedada real.
- Reavaliação de parâmetro sensível via SSM `SecureString` (hoje deliberadamente não usado — ver ADR-0009).
- Execução real dos 4 workflows de deploy AWS (`deploy-development.yml`, `promote-staging.yml`, `promote-production.yml`, `rollback-production.yml`) e das contas AWS reais (`development`, `staging`, `production`) que eles apontam — o Terraform e os workflows já estão materializados e validados localmente (ADR-0014); nenhuma conta AWS real foi criada ou usada nesta etapa.

## 9. MoSCoW

### Must have

- Ledger write path (registro de lançamentos, idempotência por comerciante/chave);
- consolidação diária assíncrona (Outbox → SQS → Worker → projeção);
- disponibilidade do Ledger independente de falha do Consolidado (ver prova de isolamento, `tests/Consolidation.IntegrationTests`);
- Outbox transacional e entrega at-least-once com consumo idempotente;
- isolamento de comerciante (`merchant_id` do token, nunca do payload);
- autenticação e autorização (OIDC/RS256, escopos, audiences distintas);
- 50 RPS no Consolidado com no máximo 5% de falhas elegíveis no pico;
- documentação e ADRs;
- reprodutibilidade local (Docker Compose container-first).

### Should have

- paridade LocalStack para SQS, Secrets Manager, SSM e KMS;
- baseline local de TLS e WAF na borda;
- testes de arquitetura (dependências entre camadas, higiene do repositório, governança de secrets);
- evidência de recuperação operacional (retry, DLQ, rotação de credencial ponta a ponta);
- estrutura Terraform de referência (módulos `messaging`, `secrets`, `parameters`, `kms`, `iam`);
- governança de supply chain: SBOM (CycloneDX) e scan de vulnerabilidade das 4 imagens, auditoria NuGet, revisão de dependências em PR, CodeQL, Dependabot (ADR-0013).

### Could have

- automação de deploy AWS estendida;
- SAST/DAST contínuo em ambiente produtivo (CodeQL local/CI já implementado - ver ADR-0013; o que falta é execução contínua produtiva);
- ferramenta automática de redrive de DLQ;
- dashboards mais ricos que o Aspire Dashboard local;
- AWS AppConfig para configuração dinâmica;
- rotação de credencial com zero-downtime (dual-user).

### Won't have nesta entrega

- multi-região ativo-ativo;
- prova de enforcement de IAM produtivo dentro do LocalStack Hobby (estrutural/limitação de ferramenta, não de arquitetura);
- deploy real em AWS;
- prova local completa de criptografia RDS gerenciada;
- dependência do plano de dados do API Gateway local (mantido fora do caminho quente);
- service mesh;
- mTLS entre todos os componentes internos;
- polling dinâmico do SSM (leitura é única, no startup — ver ADR-0009);
- integração com um MCP de OpenTelemetry (não está instalado; não presumir disponibilidade).

## 10. Status

Documento atualizado como fonte única de escopo/priorização/limites da solução. Não substitui a rastreabilidade detalhada (`07-rastreabilidade.md`) nem a matriz de evidências (`docs/operations/evidencias-do-case.md`) — referencia ambas.
