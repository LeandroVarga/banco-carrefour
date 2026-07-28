---
adr_id: ADR-0015
titulo: Governança de migrations de banco de dados
status: Aceita
categoria: Dados e operação
altitude_decisoria: Estrutural
data: 2026-07-28
responsavel: Arquitetura de Soluções
---

# ADR-0015 — Governança de migrations de banco de dados

## 1. Contexto

Mudanças de schema precisam ser aplicadas com segurança durante deployments, canários e execuções concorrentes, sem que instâncias de aplicação corram para migrar no próprio startup e sem risco de duas execuções de migração colidirem sobre a mesma fronteira.

## 2. Pergunta arquitetural

Como mudanças de schema são executadas com segurança através de deployments, canários e múltiplas execuções de task?

## 3. Decisão

`MigrationRunner` é o único artefato responsável por aplicar migrations — nunca a API ou o Worker no próprio startup. É um console app .NET, empacotado como imagem própria, publicado no Amazon ECR como artefato operacional (nunca um quinto workload de negócio, ver ADR-0006), executado como task ECS/Fargate one-off via `aws ecs run-task`, nunca como `aws_ecs_service`.

Ledger e Consolidation têm fronteiras de migração separadas: duas task roles dedicadas e mutuamente exclusivas, cada uma com acesso apenas ao secret da própria fronteira (ADR-0009). A exclusão mútua real durante a execução vem de um advisory lock de sessão do PostgreSQL (`pg_try_advisory_lock`/`pg_advisory_unlock`), com uma chave distinta por fronteira, liberado automaticamente pelo servidor mesmo em desconexão não graciosa. A mesma conexão que adquire o lock executa a migração, com pooling desabilitado nessa conexão — sem isso, o pool devolveria a conexão sem de fato desconectar do servidor, e o lock nunca seria liberado. A aquisição do lock tem timeout configurável, por polling.

Cada migration declara sua fase real via atributo refletido (`[MigrationPhase(...)]`), nunca inferida do nome do arquivo: EXPAND (aditiva — nova coluna nullable ou com default, nova tabela, novo índice — sempre aplicada antes do deployment de aplicação), BACKFILL (preenchimento de dado histórico fora do caminho crítico, implementado como comando dedicado, nunca como migration EF Core), e CONTRACT (remoção de coluna/tabela antiga ou campo `NOT NULL`, aplicada apenas depois que a janela de compatibilidade se fecha, exigindo aprovação explícita e nunca invocada automaticamente por workflow de deploy).

Falha de migração bloqueia o deployment da aplicação correspondente. O registro da task definition por release é feito diretamente pela API do ECS (`aws ecs register-task-definition`), nunca por `terraform apply -target` como caminho rotineiro — a infraestrutura estável da task (family, roles, security group, log group) é aplicada uma vez, com `lifecycle.ignore_changes` na definição de container, garantindo que um apply de rotina nunca reverta a revisão registrada por release. Rollback de aplicação nunca reverte uma migration já aplicada — é estritamente uma troca de digest de imagem; uma falha de CONTRACT exige intervenção manual de DBA, nunca uma resposta automatizada.

O provider real de Entity Framework Core usado neste repositório não introduz lock automático de migração, e `__EFMigrationsHistory` isoladamente nunca é tratado como mutex distribuído — a exclusão mútua real é sempre o advisory lock do PostgreSQL descrito acima.

## 4. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar |
|---|---|---|
| Aplicação migra o próprio schema no startup | Cada instância de API/Worker roda migrations ao iniciar. | Cria risco de múltiplas instâncias competindo para migrar simultaneamente durante um deployment ou canário. |
| Uma única role de migração para Ledger e Consolidation | Task role de migração compartilhada entre as duas fronteiras. | Permite que a migração de uma fronteira acesse a credencial da outra, quebrando o isolamento exigido (ADR-0002/ADR-0009). |
| `__EFMigrationsHistory` ou o lock automático de versões recentes do EF Core como mecanismo de exclusão mútua | Confiar no controle de versão de migrations do próprio EF Core como lock distribuído. | A versão do EF Core usada neste repositório não oferece esse lock automático, e a tabela de histórico isoladamente não é um mutex distribuído. |
| `terraform apply -target` como caminho rotineiro de registro de task definition | Aplicar a mudança de imagem da task de migração via apply direcionado a cada release. | Torna o registro de release dependente de um padrão de apply frágil e não documentado como exceção. |
| MigrationRunner como task ECS one-off, IAM isolado por fronteira, advisory lock do PostgreSQL, fases EXPAND/BACKFILL/CONTRACT | Execução dedicada, exclusão mútua real, disciplina de compatibilidade explícita. | Alternativa adotada. Elimina corrida de migração, isola fronteiras e protege mudanças destrutivas atrás de aprovação explícita. |

## 5. Trade-offs

A disciplina EXPAND/BACKFILL/CONTRACT exige mais etapas e mais disciplina de compatibilidade do que uma migration única e direta, em troca de coexistência segura entre a versão antiga e a nova durante canários e rolling deployments.

## 6. Consequências

Nenhuma migration real deste repositório executa operação destrutiva sem estar classificada como CONTRACT; as três migrations reais existentes são todas EXPAND, verificado automaticamente por teste que reflete o conteúdo real de cada uma contra o atributo declarado.

## 7. Guardrails

- Nenhuma API ou Worker aplica migration no próprio startup.
- Nenhuma task role de migração acessa o secret da fronteira oposta.
- Nenhum workflow de deploy invoca o comando `contract` automaticamente.
- Nenhum dos três workflows de deploy usa `terraform apply -target` como caminho rotineiro.

## 8. Risco arquitetural evitado

Uma implementação futura não deve permitir que instâncias de aplicação migrem o schema no próprio startup, executar CONTRACT automaticamente, ou compartilhar uma identidade de migração entre as duas fronteiras de banco de dados.

## 9. ASRs relacionados

ASR-004 (lançamentos confiáveis), ASR-011 (falhas recuperáveis).

## 10. ABBs e SBBs relacionados

ABB-003 (Persistência Transacional de Lançamentos), ABB-011 (Persistência do Consolidado); SBB-002/SBB-009 (Ledger/Consolidation Database).

## 11. Evidências de implementação

`src/Migrations/MigrationRunner`, `src/BancoCarrefour.Contracts/Migrations/{MigrationPhase,MigrationPhaseAttribute}.cs`, `infra/terraform/modules/ecs-migration-task`, `scripts/ci/{run-migration-task.sh,validate-migration-governance.sh}`, `tests/MigrationRunner.Tests/PostgresMigrationLockTests.cs`, `tests/Architecture.Tests/AwsPlatformGovernanceArchitectureTests.cs` (testes de fase de migration, isolamento de IAM e ausência de `-target`).

## 12. ADRs relacionados

ADR-0002 (persistência PostgreSQL independente por fronteira), ADR-0006 (unidades implantáveis e topologia de runtime), ADR-0009 (menor privilégio, secrets e criptografia), ADR-0014 (promoção, deployment e rollback por workload).
