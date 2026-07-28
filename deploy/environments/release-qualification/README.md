# deploy/environments/release-qualification

Documenta a única capacidade de qualificação pré-publicação deste
repositório (ver ADR-0013). Nunca deve ser confundida com um ambiente AWS
real: é Docker Compose efêmero, local ao runner, nunca uma execução
ECS/RDS/IAM/ALB/WAF real.

## O que é `release-qualification`

Uma única execução, dentro do MESMO job que fez o build once (nenhuma
transferência de imagem entre jobs, nenhum rebuild), que sobe
[`deploy/compose/docker-compose.release-qualification.yml`](../../compose/docker-compose.release-qualification.yml)
com as 4 imagens locais já validadas (SBOM, scan, non-root) e roda:

- health checks dos 4 componentes e da infraestrutura de apoio;
- inicialização de banco (migrations + bootstrap de roles/grants);
- autenticação/autorização real via Keycloak (OIDC/RS256);
- isolamento por `merchant_id` e scopes;
- caminho de escrita do Ledger (lançamento -> Outbox transacional ->
  entrega compatível com SQS via LocalStack -> projeção do Consolidation);
- isolamento Consolidation-indisponível vs Ledger-disponível
  ([`scripts/release/verify-ledger-consolidation-isolation.sh`](../../../scripts/release/verify-ledger-consolidation-isolation.sh));
- smoke de desempenho de 50 RPS com falha elegível <= 5%
  ([`tests/Consolidation.LoadTests`](../../../tests/Consolidation.LoadTests));
- teardown sempre executado (`docker compose down -v`), mesmo em falha -
  nunca deixa uma stack de qualificação órfã.

Orquestrado por
[`scripts/ci/run-release-qualification.sh`](../../../scripts/ci/run-release-qualification.sh),
chamado:

- como gate real dentro de `.github/workflows/publish-images.yml`,
  imediatamente antes da publicação real no Amazon ECR (a publicação só
  prossegue se a qualificação passar);
- como entrada standalone disparável manualmente via
  `.github/workflows/release-qualification.yml` (`workflow_dispatch`),
  que builda e qualifica mas **nunca publica** em nenhum registry.

## O que `release-qualification` NUNCA valida

- Comportamento real de ECS/Fargate, RDS (incluindo Multi-AZ), IAM de
  produção, ALB (incluindo canary/weighted routing real), WAF, ou
  qualquer outro serviço AWS - o Terraform desses ambientes reais
  (`development`, `staging`, `production` - ver
  [`deploy/ecs/README.md`](../../ecs/README.md)) está materializado e
  validado estruturalmente, mas nunca aplicado contra uma conta AWS real.
- Publicação em nenhum registry - a publicação real (Amazon ECR, único
  registry oficial) é uma etapa separada e posterior, condicionada ao
  sucesso desta qualificação.

## Por que uma única execução, e não ambientes encadeados

Encadear 3 "ambientes" que fossem, na prática, a mesma stack Compose
local repetida 3 vezes não agregaria evidência real sobre o
comportamento em AWS - apenas repetiria a mesma validação funcional sob
nomes diferentes. Uma única execução de qualificação, honesta sobre o
que prova e o que não prova, é mais clara e não finge ser um ambiente
AWS que não existe.

## Relação com os ambientes AWS reais

Os ambientes reais de deployment (`development`, `staging`, `production`)
são contas/ambientes AWS - ver [`deploy/ecs/README.md`](../../ecs/README.md)
para o modelo de contas e as estratégias de deployment por workload,
materializadas em Terraform e validadas estruturalmente, nunca aplicadas
contra uma conta AWS real.

## Status

Implementado e executado localmente (Docker Compose, sem AWS). Nunca
executado como gate de um job hospedado real do GitHub Actions
(`publish-images.yml` nunca rodou em um runner hospedado real).
