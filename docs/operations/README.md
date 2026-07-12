# Operações

Esta pasta contém a documentação operacional da solução.

Documentos:

- `arquitetura-operacional.md`
- `observabilidade-sli-slo-e-recuperacao.md`
- `estimativa-de-custos.md`

A documentação operacional cobre implantação, execução local, health checks, escalabilidade, recuperação, retries, isolamento de mensagens, reprocessamento, reconstrução, observabilidade, SLIs, SLOs, custos e limites entre ambiente local e produção.

## Estado operacional atual

O PR #4 disponibiliza validação container-first para o Ledger write path, com PostgreSQL do Ledger, RabbitMQ, build, testes e CI.

Comandos locais existentes:

```powershell
docker compose up -d ledger-postgres rabbitmq
docker compose run --rm dotnet-sdk dotnet build
docker compose run --rm dotnet-sdk dotnet test
```

O workflow de CI está em:

```text
.github/workflows/ci.yml
```

Ainda não há execução end-to-end completa da solução, porque `Consolidation.Worker`, `Consolidation.Api`, `DailyBalance` e `GET /daily-balances/{businessDate}` permanecem pendentes.

Também permanecem pendentes health/readiness/liveness, observabilidade completa, DLQ ou política operacional equivalente, reconstrução operacional completa, hardening produtivo de autenticação/autorização, deploy produtivo/IaC e teste de carga do Consolidado para 50 RPS.
