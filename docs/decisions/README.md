# Decisões Arquiteturais

Esta pasta contém as 16 ADRs ativas da solução, numeradas sequencialmente de ADR-0000 a ADR-0015. Cada ADR documenta uma decisão arquitetural coerente, com contexto, alternativas, trade-offs, consequências, guardrails e evidência de implementação.

O índice detalhado de governança — status, categoria, altitude decisória, ASRs, ABBs/SBBs, evidência e dependências entre decisões — está em [registro-de-decisoes.md](registro-de-decisoes.md).

## Decisões, em ordem de leitura recomendada

| ADR | Tema |
|---|---|
| [ADR-0000](ADR-0000-semantica-financeira-e-data-de-negocio.md) | Semântica financeira e data de negócio |
| [ADR-0001](ADR-0001-fronteiras-ledger-e-consolidation.md) | Fronteiras Ledger e Consolidation |
| [ADR-0002](ADR-0002-persistencia-postgresql-independente-por-fronteira.md) | Persistência PostgreSQL independente por fronteira |
| [ADR-0003](ADR-0003-arquitetura-hexagonal-e-direcao-das-dependencias.md) | Arquitetura hexagonal e direção das dependências |
| [ADR-0004](ADR-0004-integracao-assincrona-confiavel.md) | Integração assíncrona confiável |
| [ADR-0005](ADR-0005-contratos-http-e-eventos-de-integracao.md) | Contratos HTTP e eventos de integração |
| [ADR-0006](ADR-0006-unidades-implantaveis-e-topologia-de-runtime.md) | Unidades implantáveis e topologia de runtime |
| [ADR-0007](ADR-0007-identidade-autorizacao-e-isolamento-por-merchant.md) | Identidade, autorização e isolamento por merchant |
| [ADR-0008](ADR-0008-protecao-de-borda-e-conectividade-privada.md) | Proteção de borda e conectividade privada |
| [ADR-0009](ADR-0009-menor-privilegio-secrets-e-criptografia.md) | Menor privilégio, secrets e criptografia |
| [ADR-0010](ADR-0010-execucao-local-e-paridade-comportamental.md) | Execução local e paridade comportamental |
| [ADR-0011](ADR-0011-plataforma-aws-e-isolamento-de-ambientes.md) | Plataforma AWS e isolamento de ambientes |
| [ADR-0012](ADR-0012-observabilidade-e-objetivos-operacionais.md) | Observabilidade e objetivos operacionais |
| [ADR-0013](ADR-0013-integridade-de-release-e-software-supply-chain.md) | Integridade de release e software supply chain |
| [ADR-0014](ADR-0014-promocao-deployment-e-rollback-por-workload.md) | Promoção, deployment e rollback por workload |
| [ADR-0015](ADR-0015-governanca-de-migrations-de-banco-de-dados.md) | Governança de migrations de banco de dados |

As ADRs devem ser lidas em conjunto com a arquitetura, a segurança, a operação e os contratos documentados nas demais pastas de `docs/`. A jornada de implementação que originou estas decisões permanece preservada no histórico Git e nas branches locais de segurança do repositório — este diretório descreve apenas o estado final.
