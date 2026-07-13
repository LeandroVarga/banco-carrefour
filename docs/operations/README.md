# Operações

Esta pasta reúne os documentos operacionais da solução. Os comandos detalhados ficam no runbook local; este arquivo é apenas o índice operacional.

## Documentos

| Documento | Conteúdo |
|---|---|
| [runbook-demonstracao-local.md](runbook-demonstracao-local.md) | Passos para subir a solução, gerar token, executar fluxo end-to-end, validar idempotência, DLQ, retry, telemetria e testes. |
| [evidencias-do-case.md](evidencias-do-case.md) | Matriz de atendimento dos requisitos do case contra evidências do repositório. |
| [arquitetura-operacional.md](arquitetura-operacional.md) | Execução local, unidades operacionais, health checks, escala, falhas, retry, DLQ, reprocessamento e referência AWS. |
| [observabilidade-sli-slo-e-recuperacao.md](observabilidade-sli-slo-e-recuperacao.md) | Logs, métricas, traces, SLIs, SLOs, alertas e recuperação. |
| [teste-de-carga-consolidado.md](teste-de-carga-consolidado.md) | Evidência do teste local/container-first de 50 RPS do Consolidado. |
| [estimativa-de-custos.md](estimativa-de-custos.md) | Direcionadores de custo para a referência AWS. |
| [runbook-implantacao-aws.md](runbook-implantacao-aws.md) | Roteiro de implantação AWS de referência, sem afirmar execução realizada. |

## Quando ler cada documento

| Necessidade | Comece por |
|---|---|
| Executar a demonstração local | [runbook-demonstracao-local.md](runbook-demonstracao-local.md) |
| Validar o atendimento do case | [evidencias-do-case.md](evidencias-do-case.md) |
| Conferir a evidência de carga | [teste-de-carga-consolidado.md](teste-de-carga-consolidado.md) |
| Entender health, escala, DLQ e recuperação | [arquitetura-operacional.md](arquitetura-operacional.md) |
| Entender métricas, logs, traces e SLOs | [observabilidade-sli-slo-e-recuperacao.md](observabilidade-sli-slo-e-recuperacao.md) |
| Avaliar custos da referência AWS | [estimativa-de-custos.md](estimativa-de-custos.md) |
| Entender o caminho AWS/IaC | [runbook-implantacao-aws.md](runbook-implantacao-aws.md) |

## Estado operacional

A execução local é container-first via Docker Compose. Ela sobe APIs, workers, PostgreSQL do Ledger, PostgreSQL do Consolidado, RabbitMQ e Aspire Dashboard.

O baseline local cobre health checks das APIs, logs estruturados, OpenTelemetry, DLQ/retry local, testes automatizados e evidência de carga do Consolidado.

AWS, Terraform, publicação de imagens no ECR e CI/CD de deploy estão documentados como referência do case. Eles não representam execução produtiva realizada.
