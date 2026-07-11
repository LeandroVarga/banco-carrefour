# Documentação

Esta pasta contém a documentação arquitetural do desafio técnico Banco Carrefour.

A estrutura segue a organização exigida pelo desafio:

```text
docs/
  architecture/
  security/
  decisions/
  operations/
```

## Linha de raciocínio

A documentação segue a seguinte cadeia:

```text
contexto de negócio
-> requisitos arquiteturais
-> ASRs
-> ABBs
-> ADRs
-> SBBs
-> arquitetura alvo
-> segurança
-> operação
-> implementação
-> testes
```

## Pastas

| Pasta | Conteúdo |
|---|---|
| `architecture/` | Contexto, requisitos, ASRs, ABBs, SBBs, arquitetura alvo, diagramas e rastreabilidade. |
| `security/` | Autenticação, autorização, proteção de APIs, dados, secrets e comunicação segura. |
| `decisions/` | ADRs e registro consolidado de decisões arquiteturais. |
| `operations/` | Arquitetura operacional, observabilidade, SLIs, SLOs, recuperação e custos. |

## Como navegar pela documentação

| Objetivo | Documentos recomendados |
|---|---|
| Visão geral | `README.md` → `docs/README.md` |
| Jornada arquitetural | `docs/architecture/00-jornada-arquitetural.md` |
| Contexto e requisitos | `docs/architecture/01-contexto-de-negocio.md` → `docs/architecture/02-requisitos-arquiteturais.md` |
| Blocos arquiteturais e solução | `docs/architecture/03-blocos-de-arquitetura.md` → `docs/architecture/04-blocos-de-solucao.md` |
| Arquitetura alvo | `docs/architecture/05-arquitetura-da-solucao.md` |
| Diagramas | `docs/architecture/06-diagramas.md` |
| Rastreabilidade | `docs/architecture/07-rastreabilidade.md` |
| Decisões arquiteturais | `docs/decisions/registro-de-decisoes.md` → ADRs relacionados |
| Segurança | `docs/security/arquitetura-de-seguranca.md` |
| Operação | `docs/operations/arquitetura-operacional.md` |
| Observabilidade e recuperação | `docs/operations/observabilidade-sli-slo-e-recuperacao.md` |
| Custos | `docs/operations/estimativa-de-custos.md` |

## Status

| Frente | Status |
|---|---|
| Arquitetura | Documentada |
| Decisões arquiteturais | ADR-0000 até ADR-0012 criados |
| Segurança | Documentada |
| Operação e observabilidade | Documentadas |
| Estimativa de custos | Documentada |
| Implementação | Pendente |
| Testes | Pendentes |
| Execução local | Pendente |

## Observação

Esta documentação está em rascunho até a consolidação da implementação, testes e evidências de validação.
