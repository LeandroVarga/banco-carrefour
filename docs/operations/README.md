# Operações

Esta pasta contém a documentação operacional da solução.

Documentos:

- `arquitetura-operacional.md`
- `observabilidade-sli-slo-e-recuperacao.md`
- `estimativa-de-custos.md`
- `teste-de-carga-consolidado.md`

A documentação operacional cobre implantação, execução local, health checks, escalabilidade, recuperação, retries, isolamento de mensagens, reprocessamento, reconstrução, observabilidade, SLIs, SLOs, custos e limites entre ambiente local e produção.

## Estado operacional atual

O estado atual disponibiliza validação container-first para Ledger e Consolidado, com PostgreSQL do Ledger, PostgreSQL do Consolidado, RabbitMQ, build, testes e CI.

Comandos locais existentes:

```powershell
docker compose up -d ledger-postgres consolidation-postgres rabbitmq
docker compose run --rm dotnet-sdk dotnet build
docker compose run --rm dotnet-sdk dotnet test
```

O teste de carga do Consolidado é executado separadamente e não faz parte do `dotnet test` padrão:

```powershell
docker compose run --rm dotnet-sdk dotnet run --project tests/Consolidation.LoadTests
```

O teste de carga local/container-first foi executado contra a `Consolidation.Api` em `http://host.docker.internal:8081` e atendeu aos critérios na janela sustentada: 50.01 req/s, 0% falhas, p95 4.50 ms e p99 5.68 ms.

O workflow de CI está em:

```text
.github/workflows/ci.yml
```

`Consolidation.Worker`, `Consolidation.Api`, `DailyBalance` e `GET /daily-balances/{businessDate}` já foram implementados no incremento do Consolidado, com testes de integração para processador, consumer e API.

Ainda não há execução end-to-end completa via Compose com serviços de aplicação definidos. Também permanecem pendentes health/readiness/liveness, observabilidade completa, DLQ ou política operacional equivalente completa, reconstrução/reprocessamento operacional completo, hardening produtivo de autenticação/autorização, deploy produtivo/IaC e validação de capacidade em ambiente produtivo ou equivalente.
