# Documentação

Esta pasta organiza a documentação do desafio técnico Banco Carrefour. O README da raiz é o roteiro principal de avaliação; este arquivo funciona como mapa dos documentos detalhados.

## Estrutura

| Pasta | Conteúdo |
|---|---|
| [architecture/](architecture/README.md) | Contexto, requisitos, ABBs, SBBs, arquitetura da solução, diagramas, rastreabilidade e prontidão. |
| [security/](security/README.md) | Controles de autenticação, autorização, proteção de dados, APIs, secrets e comunicação segura. |
| [decisions/](decisions/README.md) | ADRs e registro consolidado de decisões arquiteturais. |
| [operations/](operations/README.md) | Runbooks, evidências, observabilidade, recuperação, custos e referência operacional AWS. |
| [../infra/](../infra/README.md) | Referência documental de IaC para AWS. |

## Como navegar

| Objetivo | Documento recomendado |
|---|---|
| Avaliar atendimento do case | [operations/evidencias-do-case.md](operations/evidencias-do-case.md) |
| Entender arquitetura | [architecture/05-arquitetura-da-solucao.md](architecture/05-arquitetura-da-solucao.md) |
| Ver diagramas | [architecture/06-diagramas.md](architecture/06-diagramas.md) |
| Ver requisitos e ASRs | [architecture/02-requisitos-arquiteturais.md](architecture/02-requisitos-arquiteturais.md) |
| Ver rastreabilidade arquitetural | [architecture/07-rastreabilidade.md](architecture/07-rastreabilidade.md) |
| Ver rastreabilidade de implementação | [traceability.md](traceability.md) |
| Executar localmente | [operations/runbook-demonstracao-local.md](operations/runbook-demonstracao-local.md) |
| Ver teste de carga | [operations/teste-de-carga-consolidado.md](operations/teste-de-carga-consolidado.md) |
| Ver segurança | [security/arquitetura-de-seguranca.md](security/arquitetura-de-seguranca.md) |
| Ver decisões | [decisions/registro-de-decisoes.md](decisions/registro-de-decisoes.md) |
| Ver operação | [operations/arquitetura-operacional.md](operations/arquitetura-operacional.md) |
| Ver observabilidade e recuperação | [operations/observabilidade-sli-slo-e-recuperacao.md](operations/observabilidade-sli-slo-e-recuperacao.md) |
| Ver referência AWS | [operations/runbook-implantacao-aws.md](operations/runbook-implantacao-aws.md) e [../infra/README.md](../infra/README.md) |

## Linha de raciocínio

```text
contexto de negócio
-> requisitos e ASRs
-> blocos de arquitetura
-> decisões
-> blocos de solução
-> arquitetura, segurança e operação
-> implementação, testes e evidências
```

## Status resumido

| Frente | Status |
|---|---|
| Arquitetura | Documentada em [architecture/](architecture/README.md). |
| Segurança | Documentada em [security/](security/README.md). |
| ADRs | ADR-0000 a ADR-0015 registrados em [decisions/registro-de-decisoes.md](decisions/registro-de-decisoes.md). |
| Implementação local | Baseline container-first com APIs, workers, PostgreSQL, RabbitMQ e Aspire Dashboard. |
| Testes | Contratos, integração e teste de carga documentados. |
| Operação | Runbook local, observabilidade, recuperação e evidências em [operations/](operations/README.md). |
| AWS/IaC | Referência documental, sem deploy AWS ou execução de Terraform em ambiente AWS. |
