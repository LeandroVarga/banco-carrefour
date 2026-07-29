---
adr_id: ADR-0009
titulo: Menor privilégio, secrets e criptografia
status: Aceita
categoria: Segurança
altitude_decisoria: Estrutural
data: 2026-07-28
responsavel: Arquitetura de Soluções
---

# ADR-0009 — Menor privilégio, secrets e criptografia

## 1. Contexto

Cada workload precisa acessar apenas as credenciais, tabelas e recursos criptográficos necessários à sua própria função. Um usuário PostgreSQL único por banco, compartilhado entre migrations e runtime, ou credenciais versionadas no repositório, ampliam o impacto de qualquer componente comprometido.

## 2. Pergunta arquitetural

Como credenciais, material criptográfico, permissões de banco de dados e identidades de workload são isolados entre si?

## 3. Decisão

Cada componente PostgreSQL tem uma role própria, com grants explícitos por tabela: `ledger_migration`/`consolidation_migration` (DDL, ownership), `ledger_api` (leitura/escrita em `Entries`/`InputIdempotency`, insert em Outbox), `ledger_outbox_publisher` (leitura/atualização só da Outbox), `consolidation_worker` (aplicação de evento e projeção, sem acesso ao Ledger), `consolidation_api_readonly` (somente leitura em `DailyBalance`). Nenhuma role de runtime tem privilégio de DDL.

Na AWS de referência, cada workload de negócio tem uma dupla de roles IAM — task role e execution role — nunca uma role ampla compartilhada entre os quatro workloads. A execution role resolve os secrets da definição do container; a task role concede o acesso runtime específico (SQS de envio para o Publisher, SQS de consumo para o Worker, ECR pull-only para todas). As migrations seguem um modelo próprio: duas task roles dedicadas e mutuamente exclusivas (`migration-ledger-task`, `migration-consolidation-task`), cada uma com acesso apenas ao secret da própria fronteira, nunca a ambos, e sem acesso a SQS ou permissão de publicação no ECR — compartilhando apenas uma execution role genérica, que nunca recebe `secret_arns`.

Secrets Manager, SSM Parameter Store e KMS provêm as credenciais, os parâmetros não sensíveis (issuer/audience OIDC) e as chaves de criptografia. Localmente, um bootstrap gera segredos de forma idempotente antes da stack subir, gravados apenas em arquivos não versionados; nenhum valor sensível passa por argumento de recurso Terraform. Nenhuma credencial ou chave de assinatura é versionada no repositório; nenhum valor de secret é impresso em log. Tráfego externo usa TLS (ADR-0008); dados em repouso no PostgreSQL e no Amazon RDS são protegidos por criptografia gerenciada.

O enforcement de política IAM não é comprovável no ambiente de emulação local usado neste projeto — a garantia local equivalente é a disciplina da aplicação: cada componente só solicita, por configuração, o secret ou parâmetro que lhe pertence, comprovado por teste.

## 4. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar |
|---|---|---|
| Usuário único por banco, dono do schema e usado pelo runtime | Uma única role PostgreSQL por banco para tudo. | Amplia o raio de impacto de qualquer API ou worker comprometido, sem separação entre DDL e operação runtime. |
| Uma única role IAM ampla para todos os workloads | Todos os componentes compartilhando a mesma role. | Concede acesso desnecessário entre workloads e dificulta auditoria de quem acessa o quê. |
| Uma única role de migração para ambas as fronteiras | Uma role de migração com acesso aos secrets de Ledger e Consolidation. | Permite que uma migração de uma fronteira acesse credenciais da outra, quebrando o isolamento entre fronteiras (ADR-0002). |
| Credenciais em variáveis de ambiente sem gestão de segredos | Variáveis de ambiente simples, sem Secrets Manager/SSM/KMS. | Facilita versionamento acidental de credenciais e não representa o modelo de produção. |
| Roles distintas por componente, Secrets Manager/SSM/KMS para gestão, migrações isoladas por fronteira | Menor privilégio comprovado por teste em todas as camadas. | Alternativa adotada. Reduz o raio de impacto de qualquer credencial comprometida e prepara a integração real com serviços gerenciados. |

## 5. Trade-offs

Mais roles e mais bootstrap de permissões para gerenciar do que um modelo compartilhado, em troca de um raio de impacto comprovadamente menor por componente.

## 6. Consequências

Testes negativos com conexões PostgreSQL reais comprovam que cada role só acessa exatamente o que deveria. O enforcement de IAM em si permanece uma limitação conhecida do ambiente de emulação local, documentada como risco aceito — nunca apresentada como comprovação de isolamento negativo real.

## 7. Guardrails

- Nenhuma role de runtime executa DDL.
- Nenhuma role de migração acessa o secret da fronteira oposta.
- Nenhuma credencial, signing key ou connection string com senha é versionada no repositório.
- Nenhum valor de secret é impresso em log em nenhum componente.

## 8. Risco arquitetural evitado

Uma implementação futura não deve usar uma identidade de workload compartilhada, um superusuário de banco compartilhado entre migração e runtime, uma única role de migração para as duas fronteiras, ou secrets embutidos em configuração versionada.

## 9. ASRs relacionados

ASR-004 (lançamentos confiáveis), ASR-009 (acesso autenticado e autorizado por comerciante).

## 10. ABBs e SBBs relacionados

ABB-015 (Segurança de Acesso); SBB-019 (Configuration and Secrets).

## 11. Evidências de implementação

`infra/postgres/{ledger,consolidation}/{db-role-bootstrap.sql,db-grants-bootstrap.sql}`, `infra/terraform/modules/{iam,secrets,parameters,kms}`, `infra/terraform/environments/{development,staging,production}/main.tf` (`module "task_roles"`, `module "task_execution_roles"`), `tests/Security.IntegrationTests/Database/DatabasePrivilegesTests.cs`, `tests/Architecture.Tests/AwsPlatformGovernanceArchitectureTests.cs` (`Migration_task_roles_devem_ter_exatamente_1_secret_arn_cada...`), `docs/security/threat-model.md`.

## 12. ADRs relacionados

ADR-0002 (persistência PostgreSQL independente por fronteira), ADR-0007 (identidade, autorização e isolamento por merchant), ADR-0011 (plataforma AWS e isolamento de ambientes), ADR-0015 (governança de migrations de banco de dados).
