---
doc_id: SEC-002
titulo: Threat Model — Riscos Residuais Aceitos
versao: 1.1
status: Baseline local
responsavel: Arquitetura de Soluções
ultima_atualizacao: 2026-07-26
etapa_relacionada: Identidade, borda, menor privilégio e secrets (ADR-0007, ADR-0008, ADR-0009)
---

# Threat Model — Riscos Residuais Aceitos

Este documento registra riscos residuais **conhecidos, confirmados por execução real e deliberadamente aceitos** neste baseline local — não uma análise de ameaças genérica (essa já está coberta por [arquitetura-de-seguranca.md](arquitetura-de-seguranca.md), seção 15). Cada item aqui exige: evidência de como foi confirmado, por que foi aceito, e qual o controle compensatório real.

## 1. IAM: enforcement de policy não comprovável no LocalStack Hobby

**Confirmado por execução real** (capability spike empírico):

```text
iam create-user --user-name probe-no-policy-user        → 200 (nenhuma policy anexada)
iam create-access-key --user-name probe-no-policy-user   → 200
secretsmanager get-secret-value --secret-id ledger-api-db-password → 200, retorna o valor
```

Um usuário IAM sem nenhuma policy anexada conseguiu ler um secret. O LocalStack Community/Hobby usado neste projeto aceita chamadas de Secrets Manager/SSM/KMS **independentemente** de qualquer policy IAM anexada.

**Impacto**: nenhum teste automatizado deste repositório pode (nem afirma) provar isolamento negativo entre secrets por IAM localmente. O `infra/terraform/modules/iam` provisiona roles e policies inline de menor privilégio como **estrutura local + referência AWS** (prova de que o Terraform sabe montar a estrutura correta), não como controle de acesso comprovadamente efetivo neste ambiente.

**Controle compensatório real** (o único válido localmente): disciplina da aplicação — cada componente (`Ledger.Api`, `Ledger.OutboxPublisher`, `Consolidation.Api`, `Consolidation.Worker`) só solicita, por configuração (`SecretsManager:SecretName`), o secret que lhe pertence (ver `DatabaseCredentialsResolver`, coberto por `LocalStackSecurityTests.Caso_3_a_9...`). Isso não impede um componente comprometido de tentar ler o secret de outro — apenas garante que, em operação normal, cada um só pede o seu.

**Na referência AWS**: IAM real (fora do LocalStack) comprova enforcement de policy — este risco é específico do ambiente de demonstração local, não da arquitetura de referência.

## 2. Credenciais de bootstrap com escopo amplo transitório

Os serviços `ledger-db-role-bootstrap`/`consolidation-db-role-bootstrap` (docker-compose.yml) conectam-se transitoriamente como o usuário administrativo legado (`ledger`/`consolidation`, `PGUSER`/`PGPASSWORD` fixos no compose) para criar as roles de menor privilégio e transferir ownership (ver ADR-0009). Esse usuário administrativo **nunca** é usado pelos componentes de runtime (confirmado: nenhuma `ConnectionStrings` de `Ledger.Api`/`Ledger.OutboxPublisher`/`Consolidation.Api`/`Consolidation.Worker` referencia `Username=ledger`/`Username=consolidation` — ver `docker-compose.yml`). Risco aceito: é um padrão de bootstrap local, não uma credencial de runtime; a senha (`ledger`/`consolidation`) é trivial e conhecida por design, adequada apenas ao ambiente local efêmero.

## 3. Rotação de secrets é manual, não automática

`secret-value-bootstrap-impl.sh` é idempotente e reexecutável (comprovado por `LocalStackSecurityTests`), mas não há rotação automática agendada — a rotação depende de `ALTER ROLE` no PostgreSQL seguido de `secret-value-bootstrap-impl.sh` (grava o novo valor) e de reiniciar/redeployar apenas o componente afetado. Aceito como adequado ao escopo de demonstração local; a referência AWS pode adotar `aws_secretsmanager_secret_rotation` com Lambda de rotação, fora do escopo deste ciclo.

**Comprovado por execução real ponta a ponta** (ADR-0009): rotação de `ledger_api` (componente de escrita) e `consolidation_worker` (processamento) contra a stack completa real (Keycloak, Postgres, LocalStack, edge-proxy) — senha antiga confirmada rejeitada (via conexão de rede real entre containers, não pelo `pg_hba.conf` de loopback do próprio container Postgres, que usa `trust` e aceitaria qualquer senha), componente reiniciado carrega a credencial nova via Secrets Manager, fluxo de negócio real repetido com sucesso, nenhuma senha aparece nos logs do componente.

**Risco residual aceito — sem janela de zero-downtime**: PostgreSQL não suporta duas senhas válidas simultaneamente para a mesma role. Entre o `ALTER ROLE` e o restart do componente, novas conexões do processo antigo podem falhar (conexões já abertas no pool Npgsql podem continuar funcionando até serem recicladas). Uma estratégia sem nenhuma janela de indisponibilidade exigiria um mecanismo de dual-user ou um serviço gerenciado de rotação — não implementado, por não haver decisão arquitetural dedicada para essa complexidade adicional (ver ADR-0009, seção de consequências).

## 4. KMS local não comprova equivalência integral com AWS KMS

O round-trip `create → encrypt → decrypt` foi comprovado real e funcional no LocalStack (`LocalStackSecurityTests.Caso_11_KMS_encrypt_decrypt_funciona`), mas o LocalStack Community/Hobby não replica garantias de HSM, auditoria via CloudTrail, nem todos os algoritmos/keyspecs do KMS real da AWS. A prova aqui é de infraestrutura e integração, não de equivalência de segurança criptográfica com o serviço gerenciado real.

## 5. API Gateway fora do caminho quente (não é uma mitigação, é escopo)

A invocação real de tráfego via LocalStack API Gateway REST v1 não é estável nesta build (ver ADR-0008) — por isso nunca esteve no caminho quente do Compose. Isso não é tratado como mitigação de um risco; é uma decisão de escopo deliberada.

## 6. Ameaças da plataforma AWS multi-conta (ADR-0011)

Riscos específicos do alvo AWS real materializado em Terraform - nenhum foi observado em execução real (nenhum recurso AWS foi provisionado), analisados por design:

- **Workflow do GitHub comprometido**: mitigado por permissões mínimas por job (nunca no nível do workflow), actions de terceiros pinadas por SHA de commit (`WorkflowGovernanceArchitectureTests`), e trust policy OIDC restrita por `repo`/`ref`/`environment` (nunca um wildcard) - um workflow comprometido em um repositório diferente, ou rodando fora do `environment` protegido, não consegue assumir nenhuma role de deploy.
- **Substituição de artefato / divergência de digest**: mitigado pela cadeia build-once (um commit → uma imagem → um digest ECR real, nunca reconstruído) e pela verificação cruzada de digest (`docker push` vs `aws ecr describe-images`, ver ADR-0013) - um artefato substituído produziria um digest diferente do registrado no manifesto de release, detectado antes da promoção.
- **Confiança cross-account indevida**: a resource policy do ECR (seção "cross-account pull", ADR-0011) concede apenas `GetDownloadUrlForLayer`/`BatchGetImage`/`BatchCheckLayerAvailability` às contas de workload explicitamente listadas em `workload_account_ids` - nunca push, nunca gerenciamento, nunca um curinga de conta.
- **Divulgação de secret**: secrets nunca aparecem em variáveis de ambiente literais na task definition - sempre via bloco `secrets` (resolvido pela execution role, nunca pela task role), nunca logados (`data_trace_enabled = false` na API Gateway).
- **Exposição pública indevida**: RDS e ECS tasks nunca têm IP público (`publicly_accessible = false`, `assign_public_ip = false`); o ALB é sempre interno (`internal = true`); o único endpoint público é a API Gateway REST, atrás de WAF.
- **Movimento lateral**: security groups em camadas (ALB só aceita do security group do VPC Link V2 - nunca um CIDR amplo da VPC; tasks ECS só aceitam do ALB; RDS só aceita das tasks ECS) - nenhum salto direto de um recurso para outro fora dessa cadeia.
- **Envenenamento de fila (SQS)**: preservado o modelo at-least-once + consumo idempotente (ADR-0004) e DLQ com `max_receive_count` - uma mensagem malformada/maliciosa esgota as tentativas e vai para a DLQ, nunca bloqueia a fila principal indefinidamente.
- **Escalonamento de privilégio no banco**: preservado o modelo de menor privilégio por role (ADR-0009) - o usuário mestre do RDS nunca é usado em runtime pela aplicação.
- **Abuso do mecanismo de rollback**: `rollback-production.yml` exige dois run-ids distintos (`previous_promote_run_id` ≠ `current_failed_run_id`, testado por `AwsPlatformGovernanceArchitectureTests`) e roda sob o mesmo GitHub Environment protegido (`production`, aprovação humana obrigatória) - nunca reverte para a própria release atual, nunca contorna a aprovação.

## Referências

- [ADR-0007 — Identidade, autorização e isolamento por merchant](../decisions/ADR-0007-identidade-autorizacao-e-isolamento-por-merchant.md)
- [ADR-0008 — Proteção de borda e conectividade privada](../decisions/ADR-0008-protecao-de-borda-e-conectividade-privada.md)
- [ADR-0009 — Menor privilégio, secrets e criptografia](../decisions/ADR-0009-menor-privilegio-secrets-e-criptografia.md)
- [ADR-0011 — Plataforma AWS e isolamento de ambientes](../decisions/ADR-0011-plataforma-aws-e-isolamento-de-ambientes.md)
