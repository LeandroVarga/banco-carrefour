---
doc_id: DEC-REG
titulo: Registro de Decisões Arquiteturais
versao: 1.0
status: Rascunho
responsavel: Arquitetura de Soluções
ultima_atualizacao: 2026-07-11
etapa_relacionada: Definition and Decision
---

# Registro de Decisões Arquiteturais

## 1. Objetivo

Este documento consolida as decisões arquiteturais relevantes da solução.

As decisões detalhadas são registradas em ADRs, Architecture Decision Records, dentro de `docs/decisions/`.

---

## 2. Decisões registradas

| ADR | Título | Status | Documento |
|---|---|---|---|
| ADR-0000 | Semântica do consolidado diário | Aceita | `ADR-0000-semantica-do-consolidado-diario.md` |
| ADR-0001 | Fronteiras entre Lançamentos e Consolidado | Aceita | `ADR-0001-fronteiras-entre-lancamentos-e-consolidado.md` |
| ADR-0002 | Outbox e publicação confiável | Aceita | `ADR-0002-outbox-e-publicacao-confiavel.md` |
| ADR-0003 | Consumo at-least-once e idempotente | Aceita | `ADR-0003-consumo-at-least-once-e-idempotente.md` |
| ADR-0004 | Projeção materializada do Consolidado | Aceita | `ADR-0004-projecao-materializada-do-consolidado.md` |
| ADR-0005 | Persistências independentes por fronteira | Aceita | `ADR-0005-persistencias-independentes-por-fronteira.md` |
| ADR-0006 | Persistência relacional e PostgreSQL | Aceita | `ADR-0006-persistencia-relacional-e-postgresql.md` |
| ADR-0007 | Canal assíncrono, broker e RabbitMQ local | Aceita | `ADR-0007-canal-assincrono-broker-e-rabbitmq-local.md` |
| ADR-0008 | Unidades implantáveis e topologia de runtime | Aceita | `ADR-0008-unidades-implantaveis-e-topologia-de-runtime.md` |
| ADR-0009 | Stack tecnológica da solução | Aceita | `ADR-0009-stack-tecnologica-da-solucao.md` |
| ADR-0010 | Execução local, portabilidade cloud e padrões corporativos | Aceita | `ADR-0010-execucao-local-portabilidade-cloud-e-padroes-corporativos.md` |
| ADR-0011 | Decisões de segurança | Aceita | `ADR-0011-decisoes-de-seguranca.md` |
| ADR-0012 | Observabilidade e prontidão operacional | Aceita | `ADR-0012-observabilidade-e-prontidao-operacional.md` |

---

## 3. Status

Documento em rascunho até a consolidação final da documentação, implementação e testes.
