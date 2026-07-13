# Arquitetura

Esta pasta contém a documentação principal da arquitetura da solução.

## Documentos

| Documento | Conteúdo |
|---|---|
| `00-jornada-arquitetural.md` | Jornada usada para transformar o desafio em solução arquitetural. |
| `01-contexto-de-negocio.md` | Contexto, problema, capacidades, requisitos funcionais e semântica de negócio. |
| `02-requisitos-arquiteturais.md` | Requisitos não funcionais, ASRs, cenários de qualidade e critérios de aceitação. |
| `03-blocos-de-arquitetura.md` | ABBs necessários para atender aos requisitos arquiteturais. |
| `04-blocos-de-solucao.md` | SBBs que materializam os ABBs e decisões arquiteturais. |
| `05-arquitetura-da-solucao.md` | Arquitetura alvo, fluxos, consistência, falhas, escala, segurança e observabilidade. |
| `06-diagramas.md` | Diagramas C4, componentes, fluxos, visão operacional local e implantação AWS de referência. |
| `07-rastreabilidade.md` | Relação entre requisitos, ASRs, ABBs, ADRs, SBBs, testes e evidências de implementação. |
| `08-implementation-readiness.md` | Decisões e critérios necessários para iniciar a implementação sem decisões implícitas. |

## Leitura recomendada

Para revisão arquitetural completa:

```text
01-contexto-de-negocio.md
-> 02-requisitos-arquiteturais.md
-> 03-blocos-de-arquitetura.md
-> 04-blocos-de-solucao.md
-> docs/decisions/registro-de-decisoes.md
-> 05-arquitetura-da-solucao.md
-> 06-diagramas.md
-> 07-rastreabilidade.md
-> 08-implementation-readiness.md
```

Para avaliação rápida:

```text
05-arquitetura-da-solucao.md
-> 06-diagramas.md
-> 07-rastreabilidade.md
-> 08-implementation-readiness.md
-> docs/decisions/registro-de-decisoes.md
```

## Status

Documentação arquitetural atualizada para o baseline local implementado e para AWS como plataforma de referência do case. Pendências produtivas e evoluções permanecem registradas nos documentos de prontidão, operação e evidências.
