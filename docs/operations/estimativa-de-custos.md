---
doc_id: OPS-003
titulo: Estimativa de Custos
versao: 1.0
status: Referência
responsavel: Arquitetura de Soluções
ultima_atualizacao: 2026-07-12
etapa_relacionada: Realization and Governance
---

# Estimativa de Custos

## 1. Objetivo

Este documento apresenta a visão de custos da solução de controle de lançamentos e consulta do consolidado diário.

A estimativa não fixa valores comerciais reais de provedor cloud, pois o desafio não informa provedor, região, política corporativa, plano de suporte, descontos, reservas, volume mensal, retenção final ou serviços gerenciados obrigatórios.

O foco deste documento é apresentar os direcionadores de custo, os componentes que geram custo, os tradeoffs arquiteturais e um modelo de estimativa que pode ser preenchido quando o ambiente de execução for definido.

---

## 2. Escopo da estimativa

A estimativa cobre:

```text
- APIs
- workers
- bancos de dados
- broker ou fila
- armazenamento
- tráfego
- observabilidade
- segurança e secrets
- backup e restore
- operação local
- evolução para ambiente corporativo ou cloud
```

Não cobre:

```text
- licenças corporativas já contratadas
- custo de equipe
- custo de suporte premium
- descontos comerciais
- reservas ou savings plans
- custos específicos de uma região cloud
- custos de ferramentas corporativas já existentes
```

---

## 3. Princípios de custo

A arquitetura considera os seguintes princípios:

```text
- preservar confiabilidade do registro financeiro antes de otimizar custo
- evitar custo desnecessário no caminho inicial do desafio
- permitir execução local reproduzível
- separar custos por fronteira
- escalar APIs e workers de forma independente
- usar projeção materializada para reduzir custo de leitura
- manter Consolidado reconstruível para reduzir dependência de armazenamento crítico duplicado
- documentar tradeoffs entre simplicidade, operação, resiliência e custo
```

---

## 4. Componentes que geram custo

| Componente | Tipo de custo | Observações |
|---|---|---|
| Ledger.Api | Computação | Escala conforme volume de registros de lançamentos. |
| Ledger.OutboxPublisher | Computação | Escala conforme volume de eventos pendentes na Outbox. |
| Consolidation.Worker | Computação | Escala conforme backlog, lag e taxa de eventos. |
| Consolidation.Api | Computação | Escala conforme volume de consultas ao consolidado. |
| Ledger Database | Banco de dados | Principal fonte de verdade; maior criticidade operacional. |
| Consolidation Database | Banco de dados | Armazena visão derivada e controle de eventos processados. |
| Message Broker | Mensageria | Transporta eventos de Lançamentos para Consolidado. |
| Observabilidade | Logs, métricas e traces | Custo cresce com volume de logs, retenção e cardinalidade de métricas. |
| Backup e restore | Armazenamento e operação | Mais crítico para Ledger Database. |
| Secrets | Serviço gerenciado ou ferramenta corporativa | Custo depende da plataforma usada. |
| Tráfego de rede | Entrada, saída e comunicação interna | Depende da topologia de implantação. |

---

## 5. Execução local

Na execução local do desafio, os custos diretos de infraestrutura cloud não existem.

A solução local usa:

```text
- containers
- Docker Compose
- PostgreSQL local para Ledger Database
- PostgreSQL local para Consolidation Database
- RabbitMQ local
- logs e métricas locais quando implementados
```

Custos relevantes no ambiente local:

```text
- tempo de desenvolvimento
- uso de CPU, memória e disco da máquina local
- complexidade de manter múltiplos containers
- tempo de execução de testes
```

A execução local valida a arquitetura e reduz dependência de provisionamento cloud para avaliação.

---

## 6. Ambiente corporativo ou cloud

Em ambiente corporativo ou cloud, os mesmos papéis arquiteturais podem ser materializados por serviços gerenciados ou por plataforma interna.

Mapeamento de papéis:

| Papel arquitetural | Possível materialização em produção | Direcionador de custo |
|---|---|---|
| APIs | Containers, app services, serverless ou plataforma interna | CPU, memória, réplicas, requisições. |
| Workers | Containers, jobs, serviços background ou plataforma interna | CPU, memória, réplicas, tempo ativo. |
| Ledger Database | Banco relacional gerenciado ou aprovado | Tamanho, IOPS, HA, backup, retenção. |
| Consolidation Database | Banco relacional gerenciado ou aprovado | Tamanho, leitura, escrita, índices, retenção. |
| Broker | Fila ou broker gerenciado | Mensagens, throughput, retenção, HA. |
| Observabilidade | Plataforma de logs, métricas e traces | Volume, retenção, cardinalidade, alertas. |
| Secrets | Secret manager ou solução corporativa | Quantidade de secrets, chamadas, rotação. |
| Gateway e segurança | API gateway, ingress, WAF, service mesh | Requisições, políticas, tráfego, regras. |

---

## 7. Direcionadores principais de custo

Os principais direcionadores são:

```text
- número de lançamentos registrados por dia
- número de consultas ao Consolidado por dia
- pico de consulta de 50 RPS
- volume de eventos publicados
- tamanho da Outbox
- retenção de lançamentos
- retenção de eventos processados
- retenção de logs, métricas e traces
- política de backup
- necessidade de alta disponibilidade
- número de réplicas mínimas
- estratégia de autoscaling
- volume de reprocessamento
- frequência de rebuild do DailyBalance
```

---

## 8. Modelo de estimativa

A estimativa pode ser calculada a partir das seguintes variáveis:

```text
Custo total mensal =
  custo de computação
+ custo de bancos de dados
+ custo de mensageria
+ custo de armazenamento
+ custo de observabilidade
+ custo de backup
+ custo de tráfego
+ custo de segurança e secrets
+ margem operacional
```

Variáveis recomendadas:

| Variável | Descrição |
|---|---|
| entries_per_day | Quantidade média de lançamentos por dia. |
| entries_peak_rps | Pico de escrita de lançamentos. |
| consolidated_query_rps_peak | Pico de consulta do Consolidado. No desafio: 50 RPS. |
| events_per_entry | Quantidade de eventos gerados por lançamento. |
| retention_entries_days | Retenção dos lançamentos financeiros. |
| retention_events_days | Retenção de eventos processados e Outbox. |
| log_volume_gb_month | Volume mensal estimado de logs. |
| metrics_series_count | Quantidade de séries temporais de métricas. |
| trace_sample_rate | Taxa de amostragem de traces. |
| backup_retention_days | Retenção de backups. |
| min_replicas_api | Réplicas mínimas das APIs. |
| min_replicas_worker | Réplicas mínimas dos workers. |

---

## 9. Estimativa qualitativa por componente

| Componente | Peso relativo de custo | Comentário |
|---|---|---|
| Ledger.Api | Baixo a médio | API simples, mas crítica para escrita financeira. |
| Ledger.OutboxPublisher | Baixo | Worker pode iniciar com baixa capacidade e escalar conforme Outbox. |
| Consolidation.Worker | Baixo a médio | Custo cresce com volume de eventos e necessidade de reduzir lag. |
| Consolidation.Api | Baixo a médio | Pico explícito de 50 RPS exige capacidade mínima de leitura. |
| Ledger Database | Médio a alto | Fonte de verdade financeira; HA, backup e retenção elevam custo. |
| Consolidation Database | Médio | Projeção materializada reduz custo de leitura, mas exige armazenamento próprio. |
| Message Broker | Baixo a médio | Custo depende de throughput, retenção e HA. |
| Observabilidade | Médio a alto | Pode crescer rapidamente com logs, traces e alta cardinalidade. |
| Backup | Médio | Principalmente relevante para Ledger Database. |
| Segurança e secrets | Baixo a médio | Normalmente menor que banco e observabilidade, mas obrigatório. |

---

## 10. Tradeoffs de custo

| Decisão | Ganho | Custo ou impacto |
|---|---|---|
| Separar Lançamentos e Consolidado | Isola falhas e permite evolução independente. | Mais componentes para operar. |
| Usar Outbox | Evita perda silenciosa entre banco e broker. | Mais tabela, worker e monitoramento. |
| Usar broker | Desacopla Lançamentos do Consolidado. | Custo de mensageria e operação. |
| Usar projeção DailyBalance | Reduz custo e latência de consulta. | Exige armazenamento e mecanismo de atualização. |
| Persistências independentes | Reduz acoplamento e melhora isolamento. | Pode aumentar custo de bancos e backups. |
| Observabilidade completa | Melhora diagnóstico, valida RNFs e apoia recuperação. | Logs, métricas e traces podem ter custo relevante. |
| Segurança por menor privilégio | Reduz risco operacional e exposição. | Aumenta configuração e gestão de credenciais. |
| Execução local com Docker Compose | Reduz custo de avaliação e facilita reprodutibilidade. | Não representa HA real de produção. |

---

## 11. Pontos de otimização

Otimizações possíveis sem comprometer a arquitetura:

```text
- ajustar retenção de logs
- reduzir cardinalidade de métricas
- usar sampling de traces
- ajustar réplicas mínimas conforme ambiente
- escalar workers conforme backlog real
- usar índices adequados em DailyBalance
- compactar ou arquivar eventos antigos
- separar logs de auditoria e logs técnicos
- usar serviços gerenciados quando reduzirem carga operacional
- revisar retenção de Outbox e Processed Events
```

Cuidados:

```text
- não reduzir custo sacrificando a fonte de verdade financeira
- não remover Outbox para simplificar publicação
- não usar cache como fonte principal do Consolidado
- não eliminar logs necessários para auditoria e recuperação
- não compartilhar credenciais para reduzir configuração
```

---

## 12. Custos por fase

| Fase | Perfil de custo | Observação |
|---|---|---|
| Avaliação local | Baixo | Execução em Docker Compose, sem custo cloud direto. |
| Homologação | Baixo a médio | Pode usar recursos menores, menor retenção e menor HA. |
| Produção inicial | Médio | Exige segurança, observabilidade, backup, retenção e capacidade mínima. |
| Produção crítica | Médio a alto | Exige HA, DR, maior retenção, monitoramento avançado e operação formal. |

---

## 13. Estimativa inicial para o desafio

Para o escopo do desafio, a estimativa inicial é:

```text
- baixo custo de execução local
- custo de produção dominado por bancos, observabilidade e disponibilidade
- custo de mensageria proporcional ao volume de eventos
- custo de APIs e workers controlável por escala horizontal
- custo de segurança obrigatório e dependente da plataforma
```

Como o requisito de carga explícito é 50 RPS no Consolidado, a primeira validação de custo deve priorizar:

```text
- capacidade da Consolidation.Api
- capacidade do Consolidation Database para leitura por comerciante e data
- custo da observabilidade durante teste de carga
- comportamento do broker e worker sob backlog
```

---

## 14. Cenário de referência para produção mínima

Esta seção apresenta uma estimativa monetária de referência, em ordem de grandeza, para comparar impacto arquitetural. Não é cotação oficial e não deve ser usada como decisão de compra sem recalcular preços no provedor, região e política operacional reais.

Premissas do cenário:

```text
- pequena carga inicial
- uma região
- duas APIs em containers pequenos
- dois workers em containers pequenos
- baseline operacional atual com 1 réplica para Ledger.OutboxPublisher até existir claim/lock transacional; múltiplos workers do Consolidado exigem validação produtiva de carga, backlog e autoscaling
- dois bancos PostgreSQL gerenciados pequenos ou equivalentes
- broker gerenciado pequeno ou RabbitMQ operado em recurso dedicado pequeno
- observabilidade básica
- logs com retenção curta
- backup básico
- sem multi-região
- sem DR avançado
```

| Item | Premissa | Faixa mensal estimada |
|---|---|---:|
| Compute APIs/workers | containers pequenos para 2 APIs e 2 workers, baixa carga inicial | R$ 300-900 |
| Bancos PostgreSQL | 2 instâncias gerenciadas pequenas ou equivalentes, backup básico | R$ 800-2.500 |
| Broker | serviço gerenciado pequeno ou VM/container dedicado para RabbitMQ | R$ 250-1.200 |
| Observabilidade/logs | logs, métricas e traces básicos, baixa retenção e volume moderado | R$ 200-1.000 |
| Backup e armazenamento adicional | backups básicos, volumes pequenos e retenção curta | R$ 150-600 |
| Tráfego/rede | tráfego baixo/moderado em uma região | R$ 100-500 |
| Segurança/secrets | secret manager ou serviço corporativo equivalente em uso inicial | R$ 50-300 |
| Total estimado | cenário mínimo, sem HA avançada, multi-região ou DR | R$ 1.850-7.000 |

Valores reais dependem de cloud, região, tamanho de instâncias, modelo de banco gerenciado, retenção, tráfego, HA, licenças, descontos, reservas, suporte e ferramentas corporativas já contratadas. A estimativa deve ser recalculada antes de qualquer decisão produtiva.

---

## 15. Relação com ADRs

| ADR | Relação com custo |
|---|---|
| ADR-0001 | Separar fronteiras aumenta componentes, mas reduz acoplamento e falha propagada. |
| ADR-0002 | Outbox adiciona persistência e worker, mas reduz risco de perda de evento. |
| ADR-0003 | At-least-once exige idempotência e controle de duplicidade, mas evita dependência de exactly-once. |
| ADR-0004 | Projeção materializada reduz custo de consulta e melhora desempenho. |
| ADR-0005 | Persistências independentes podem aumentar custo, mas melhoram isolamento. |
| ADR-0006 | PostgreSQL oferece boa base relacional e pode ser gerenciado em cloud. |
| ADR-0007 | Broker adiciona custo de mensageria, mas desacopla o fluxo. |
| ADR-0008 | Quatro unidades implantáveis aumentam operação, mas permitem escala independente. |
| ADR-0009 | Stack .NET/PostgreSQL/RabbitMQ/containers favorece execução local e portabilidade. |
| ADR-0010 | Docker Compose reduz custo local e mantém papéis portáveis para cloud. |
| ADR-0011 | Segurança por camadas adiciona configuração, mas reduz risco e atende ao desafio. |
| ADR-0012 | Observabilidade melhora operação e validação dos RNFs, mas pode aumentar custo de logs, métricas e traces. |

---

## 16. Relação com observabilidade e operação

A observabilidade é um dos componentes com maior risco de crescimento de custo.

Pontos de atenção:

```text
- logs excessivos em APIs de alto volume
- traces com sampling muito alto
- métricas com alta cardinalidade por merchant_id
- retenção longa de logs técnicos
- duplicação de sinais em múltiplas plataformas
```

A estratégia deve preservar sinais necessários para diagnóstico, segurança e validação dos RNFs, sem coletar dados além do necessário.

---

## 17. Critérios de aceitação

| ID | Critério |
|---|---|
| COST-CA-001 | Componentes geradores de custo estão identificados. |
| COST-CA-002 | Direcionadores de custo estão documentados. |
| COST-CA-003 | Custos locais e custos de produção estão diferenciados. |
| COST-CA-004 | Tradeoffs de custo estão vinculados às decisões arquiteturais. |
| COST-CA-005 | Observabilidade é tratada como item relevante de custo. |
| COST-CA-006 | A estimativa não depende de preços inventados de provedor. |
| COST-CA-007 | Existe modelo para preencher valores reais quando o ambiente for definido. |
| COST-CA-008 | Existe faixa monetária de referência explícita para produção mínima, sem tratá-la como cotação oficial. |

---

## 18. Status

Documento de referência para ordem de grandeza. A estimativa deve ser recalculada antes de decisão produtiva com provedor, região, serviços gerenciados, retenção, HA, DR e volumes reais.
