# Roadmap → Cashflow Ledger Platform (Rumo ao Kubernetes na AWS)
**Data:** 2025-09-29  
**Contexto atual:** passamos de *monolito* → *monolito modular (DDD)* → *microsserviços* (Docker Compose). Próximo passo: **Kubernetes (EKS)** na AWS, mantendo performance, resiliência e observabilidade. Este roadmap também cobre **documentação** do desafio e **estimativas de custo**.

---

## 1) Objetivos do desafio (resumo alinhado)
- Entregar uma solução com **domínios e capacidades de negócio** bem definidos (Lançamentos e Consolidado Diário).
- Desenhar **Arquitetura Alvo** e (quando fizer sentido) **Arquitetura de Transição**.
- Garantir **escalabilidade**, **resiliência**, **segurança** e **observabilidade**.
- Registrar **decisões (ADRs)**, **diagramas (C4)** e **fluxos**.
- **Requisitos não-funcionais do desafio:** Consolidado diário até **50 rps com perda ≤ 5%** mesmo em picos; o serviço de lançamentos **não cai** quando o consolidado estiver indisponível (assincronia com mensageria + DLQ).

---

## 2) Linha do tempo (fases e marcos)
> Proposta enxuta (8–10 semanas) — pode ser acelerada com equipe dedicada.

### Fase 0 — Baseline & Preparação (semana 0)
- Confirmar estado do código, testes e compose. Congelar versão base para migração.
- Reunir **NFRs/SLOs**: latência alvos, erro tolerado, RPO/RTO, política de retenção de DLQ.
- Completar **ADRs** e **C4 (Níveis 1–3)**. 📎 *Saída:* `ADR-unificado.md`, `*.drawio` (context/container/component).

### Fase 1 — Cloud Readiness (semana 1)
- 12-factor: configs em **AWS SSM Parameter Store**/**Secrets Manager**; *secrets* não em git.
- Health/metrics/probes: `/actuator/health`, `/actuator/prometheus`; readiness/liveness.
- Restrições de recursos (CPU/Mem), idempotência no **outbox** + *exactly-once* por **chave de negócio** no consumidor.
- Pipeline CI: build, imagens no **ECR**, *scan* (Trivy/Grype), *SBOM*.

### Fase 2 — Primeira subida em K8s dev (semanas 2–3)
- **EKS (dev)** + add-ons: Cluster Autoscaler, VPC CNI, Metrics Server, ExternalDNS (Route 53), AWS Load Balancer Controller.
- **Ingress** via ALB; **namespaces** `dev`/`observability`.
- Deploy via **Helm** (um chart por serviço): api-gateway, ledger, consolidator, balance-query.
- **RabbitMQ** *self-managed* (StatefulSet) ou **Amazon MQ (RabbitMQ)** — ver custos e operação.
- **PostgreSQL** via **RDS** (dev Single-AZ).
- Observabilidade: **Prometheus + Grafana** (self-managed) ou **AMP/AMG**.

### Fase 3 — Testes de carga e resiliência (semanas 3–4)
- Cargas com picos ≥ **50 rps** no consolidado; validar **≤ 5%** perdas (overflow → DLQ, reprocesso).
- Caos controlado: desligar consolidator, confirmar que ledger continua publicando (publish-only) e posterior *catch-up*.
- HPA por **CPU/latência fila**; **PodDisruptionBudget** e **Pod Priority** para mensageria e banco.

### Fase 4 — Segurança e Hardening (semanas 4–5)
- **IAM Roles for Service Accounts (IRSA)**; segregar permissões (ECR, SSM, Secrets Manager, CloudWatch).
- TLS com **ACM** no ALB; **NetworkPolicies** no cluster; **WAF** opcional no ALB.
- Backups: **RDS snapshots** automáticos + retenção; **RabbitMQ** *policy* para DLQ/TTL.

### Fase 5 — Produção (starter) e *cutover* (semanas 6–8)
- **EKS (prod)** com **2+ AZs**, **NAT Gateways**, **RDS Multi-AZ**.
- *Blue/green* ou *canary* no ALB; *feature flags* para ativar o novo caminho.
- Runbook de **reprocesso** por DLQ e *playbooks* de incidente.

### Fase 6 — Otimização contínua (pós go-live)
- *Right-sizing* dos *resources* e *requests/limits*; **Spot** para *workers* não críticos (jobs).
- Alertas de **custo** (AWS Budgets), *dashboards* de saturação/erros/latência.

---

## 3) Arquitetura de Transição
- **Hoje:** microsserviços com Docker Compose (RabbitMQ/Postgres locais).
- **Transição dev:** EKS + RDS (Single-AZ) + RabbitMQ (self-managed) + ALB em **subnets públicas** (barato, sem NAT).
- **Alvo prod:** EKS multi-AZ + RDS Multi-AZ + ALB + 2× NAT + logs/metrics gerenciados.   Mensageria: **Amazon MQ (RabbitMQ)** *ou* RabbitMQ auto-gerido — ver seção de custos.

---

## 4) Padrões e decisões (resumo)
- **Mensageria:** Ledger **publish‑only** (Outbox); Consolidator **dono da topologia AMQP** (quorum + DLX + DLQ TTL + overflow=reject-publish).
- **Escalabilidade:** HPA; filas quorum; consultas balanceadas; *stateless* nos *pods*.
- **Resiliência:** DLQ, reprocesso, PDB, múltiplas AZs, *readiness gates*, *graceful shutdown*.
- **Segurança:** IRSA, Secrets Manager, TLS no edge, políticas mínimas, audit logs.
- **Observabilidade:** Prometheus/Grafana, CloudWatch, *correlação* por `requestId`.

---

## 5) Entregáveis por fase
- F0: `ADR-unificado.md`, `*.drawio`, plano de testes/NFRs.
- F1: Charts **Helm**, *values* por ambiente, *secrets* no Secrets Manager/SSM.
- F2: Manifestos aplicados, *dashboards* prontos, *smoke tests*.
- F3: Relatório de carga com métricas/SLOs e *bottlenecks*.
- F4: Checklist de segurança e relatórios de *scan*.
- F5: Plano de *cutover*, *rollback* e pós‑go‑live.

---

## 6) Estimativa de custos (AWS, us-east-1)
> Valores aproximados (on-demand), **dev** e **prod starter**. Detalhe em `aws-costs.csv`.

- **DEV (baseline, subnets públicas, sem NAT):** **$203.94 USD/mês**    *(EKS $73.00 + nós $91.10 + ALB $16.43 + LCU $5.84 + RDS $12.41 + storage & observabilidade etc.)*  Recomenda-se **+15%** de buffer → ~**234.53 USD/mês**.

- **PROD (starter HA, subnets privadas, 2 NAT):** **522.13 USD/mês**    *(EKS 73.00 + nós 280.32 + ALB 16.43 + 2×LCU 11.68 + RDS 49.64 + 2×NAT 65.70 + storage & logs)*  Com **+15%** de buffer → ~**600.45 USD/mês**.

**Alternativas e impactos**  
- **Amazon MQ (RabbitMQ gerenciado):** reduz operação, **custa mais** que auto-gerido (aprox. dezenas a centenas USD/mês).  
- **AMP/AMG (Prometheus/Grafana gerenciados):** menos manutenção; custo por *time series/ingest* pode superar self-managed em baixo volume.  
- **Spot/Graviton:** pode reduzir 30–60% em *workers* com tolerância a preempção.

---

## 7) Riscos & Mitigações
- **NAT Gateway** encarece ambientes privados → *Mitigar dev com subnets públicas*; em prod, considerar NAT por AZ mínima viável.
- **Backpressure** no consolidado em picos → HPA por *lag* de fila; *batching* e *tuning* de *prefetch*.
- **Drift** na topologia AMQP → consolidator único declarando *Declarables*; *chaos tests* na inicialização.
- **Custo de logs** → amostrar logs, retenção curta em dev, filtros de *ingest*.
- **Segredos** → nunca em `ConfigMap`; usar **Secrets Manager** e **IRSA**.

---

## 8) Métricas de sucesso (SLOs)
- **Consolidado**: P95 ≤ 150ms (leitura) sob 50 rps; **perda ≤ 5%** com recuperação ≤ 5 min.
- **Ledger**: publicar em < 50ms P95; 0% de perda (garantida por Outbox + DLQ).
- **Disponibilidade**: API ≥ 99.5% (starter).

---

## 9) Apêndice — Mapeamento do desafio → entregáveis
- **Mapeamento de domínios e capacidades**: C4 + *bounded contexts* (Ledger/Report).  
- **Requisitos funcionais e NFRs**: seção 1 e 8; ADRs.  
- **Arquitetura Alvo**: seções 2–4; diagramas C4.  
- **Justificativas técnicas**: seção 4 e ADRs.  
- **Testes**: *harness* com evidências `out/<run>`; testes de carga (F3).  
- **README**: instruções docker/K8s e CI.  
- **Hospedagem GitHub**: repositório público com workflows e artefatos.  
- **Arquitetura de Transição**: seção 3.  
- **Estimativa de custos**: seção 6 + `aws-costs.csv`.  
- **Monitoramento e segurança**: seções 4 e 5.

---

> **Próximos passos imediatos**: (1) Aprovar custos/ambiente-alvo, (2) gerar charts Helm & *values* por ambiente, (3) provisionar EKS dev + RDS + ALB (público), (4) *smoke test*, (5) carga 50 rps + relatório.
