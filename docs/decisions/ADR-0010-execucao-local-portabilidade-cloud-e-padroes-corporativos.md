---
adr_id: ADR-0010
titulo: Execução Local, Portabilidade Cloud e Padrões Corporativos
status: Aceita
data: 2026-07-10
responsavel: Arquitetura de Soluções
decisao_relacionada: Estratégia de execução local e evolução para ambiente corporativo ou cloud
---

# ADR-0010 — Execução Local, Portabilidade Cloud e Padrões Corporativos

## 1. Contexto

O desafio exige uma solução implementável, documentada, testável e executável para avaliação técnica.

A arquitetura também precisa demonstrar segurança, operação, monitoramento, escalabilidade, recuperação e capacidade de evolução.

O enunciado não define um provedor cloud específico, uma plataforma corporativa obrigatória, um serviço final de containers, um serviço final de banco gerenciado ou um serviço final de mensageria gerenciada.

Por isso, a solução precisa separar a materialização local usada no desafio da arquitetura alvo que poderia evoluir para ambiente corporativo ou cloud.

A execução local deve ser simples, reproduzível e suficiente para validar os fluxos principais.

A arquitetura, porém, não deve depender de Docker Compose como topologia definitiva de produção.

---

## 2. Decisão

A solução adotará Docker e Docker Compose como estratégia de execução local para o desafio.

Essa execução local materializa as unidades implantáveis, bancos e broker de referência de forma reproduzível.

Em ambiente corporativo ou cloud, os mesmos papéis arquiteturais poderão ser materializados por serviços gerenciados ou padrões internos de plataforma.

A decisão de portabilidade será baseada em papéis arquiteturais, não em equivalência literal de produto.

Mapeamento inicial:

| Necessidade arquitetural | Execução local | Ambiente corporativo ou cloud |
|---|---|---|
| APIs e workers | Containers Docker | Plataforma de containers ou serviço gerenciado equivalente. |
| Orquestração local | Docker Compose | Orquestrador corporativo, plataforma de containers ou serviço gerenciado. |
| Banco relacional | PostgreSQL em container | PostgreSQL gerenciado ou banco relacional aprovado pela plataforma. |
| Mensageria | RabbitMQ em container | Fila ou broker gerenciado equivalente. |
| Secrets e configuração | Variáveis de ambiente locais | Secret manager corporativo ou cloud. |
| Observabilidade | Logs, métricas e traces locais | Stack corporativa ou cloud de logs, métricas e traces. |
| Autenticação | Representação simplificada para avaliação | Provedor de identidade corporativo. |
| Exposição HTTP | Portas locais e reverse proxy quando necessário | API gateway, ingress ou padrão corporativo equivalente. |

Essa abordagem mantém o desafio executável localmente e preserva espaço para implantação futura em plataforma corporativa ou cloud.

---

## 3. O que a decisão inclui

Esta decisão inclui:

```text
- Docker como mecanismo de empacotamento local
- Docker Compose como orquestração local do desafio
- containers separados para APIs e workers
- containers ou serviços locais para PostgreSQL e RabbitMQ
- parametrização por configuração de ambiente
- separação entre execução local e topologia final de produção
- equivalência por papel arquitetural para cloud ou plataforma corporativa
- preservação de portabilidade entre provedores e padrões internos
```

---

## 4. O que fica fora desta decisão

Esta decisão não define:

```text
- provedor cloud final
- conta, assinatura ou organização cloud final
- serviço final de containers
- serviço final de banco gerenciado
- serviço final de mensageria gerenciada
- ferramenta final de observabilidade
- solução final de autenticação corporativa
- política final de secrets
- pipeline final de CI/CD
- estratégia final de alta disponibilidade
- estratégia final de disaster recovery
- configuração final de API gateway, ingress ou service mesh
```

Esses pontos serão detalhados conforme a arquitetura evoluir para segurança, operação, custos e implantação.

---

## 5. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar como base da solução |
|---|---|---|
| Execução manual local | Executar APIs, workers, banco e broker manualmente na máquina do avaliador. | Aumenta variação de ambiente, dificulta reprodução e torna a avaliação mais frágil. |
| Docker Compose para execução local | Executar as unidades e dependências em containers coordenados localmente. | Alternativa adotada. Simplifica avaliação, reduz variação de ambiente e mantém baixo acoplamento com provedor cloud. |
| Kubernetes local como base do desafio | Usar cluster local para simular produção. | Pode aproximar runtime corporativo, mas aumenta complexidade de instalação e avaliação para o escopo inicial. |
| Cloud específica desde o início | Materializar a solução diretamente em um provedor cloud específico. | Pode ser adequado em contexto real, mas aumenta acoplamento inicial e exige decisões de plataforma não definidas pelo desafio. |
| Serverless como execução principal | Usar funções e serviços gerenciados como runtime principal. | Pode ser adequado em determinados ambientes, mas dificulta equivalência local simples e aumenta dependência de provedor. |

---

## 6. Consequências

Consequências positivas:

```text
- torna a solução executável localmente
- reduz dependência de conta cloud para avaliação
- facilita testes e validação dos fluxos principais
- mantém APIs, workers, bancos e broker como unidades explícitas
- preserva portabilidade para serviços gerenciados equivalentes
- permite evoluir para plataforma corporativa sem alterar responsabilidades arquiteturais
- evita confundir Docker Compose com arquitetura definitiva de produção
```

Consequências e tradeoffs:

```text
- Docker Compose não representa alta disponibilidade real
- a execução local não substitui decisões de produção
- recursos gerenciados de cloud precisam ser definidos posteriormente
- segurança local tende a ser simplificada em relação ao ambiente corporativo
- observabilidade local tende a ser mais limitada que a observabilidade de produção
- custos, escalabilidade e recuperação precisam ser detalhados na documentação operacional
```

---

## 7. Relação com requisitos, ABBs e SBBs

Esta decisão sustenta principalmente:

```text
- ASR-002: Consolidado deve suportar 50 RPS em pico
- ASR-003: Consolidado deve limitar falhas ou perdas de requisições a no máximo 5% no pico
- ASR-009: O acesso aos dados deve respeitar o comerciante autenticado e autorizado
- ASR-010: O fluxo deve ser observável
- ASR-011: Falhas de publicação, consumo ou consolidação devem ser recuperáveis
- ABB-012: API de Consulta do Consolidado
- ABB-013: Observabilidade do Fluxo
- ABB-014: Recuperação Operacional
- ABB-015: Segurança de Acesso
- ABB-016: Controle de Comunicação entre Serviços
- SBB-001: Ledger.Api
- SBB-002: Ledger Database
- SBB-006: Ledger.OutboxPublisher
- SBB-007: Message Broker
- SBB-008: Consolidation.Worker
- SBB-009: Consolidation Database
- SBB-012: Consolidation.Api
- SBB-014: Authentication and Authorization
- SBB-015: Service-to-Service Security
- SBB-016: Observability
- SBB-017: Operational Recovery
- SBB-018: Containers and Local Runtime
- SBB-019: Configuration and Secrets
```

---

## 8. Relação com documentos

Esta decisão sustenta:

```text
- docs/architecture/04-blocos-de-solucao.md
- docs/architecture/05-arquitetura-da-solucao.md
- docs/architecture/06-diagramas.md
- docs/security/arquitetura-de-seguranca.md
- docs/operations/arquitetura-operacional.md
- docs/operations/observabilidade-sli-slo-e-recuperacao.md
- docs/operations/estimativa-de-custos.md
```

---

## 9. Status

Decisão aceita para o escopo inicial da solução.
