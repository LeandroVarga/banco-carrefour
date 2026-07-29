---
doc_id: OPS-003
titulo: Estimativa de Custos
versao: 1.0
status: Referência
responsavel: Arquitetura de Soluções
ultima_atualizacao: 2026-07-13
---

# Estimativa de Custos

## 1. Objetivo

Este documento apresenta direcionadores de custo para a solução de controle de lançamentos e consulta do consolidado diário.

A estimativa usa AWS como plataforma de referência do case, conforme ADR-0010. Ela não fixa valores comerciais, porque preço real depende de região, sizing, retenção, tráfego, descontos, reservas, suporte, políticas corporativas e uso efetivo.

Valores devem ser recalculados na calculadora AWS ou em ferramenta corporativa aprovada antes de qualquer decisão produtiva.

---

## 2. Componentes geradores de custo

| Papel arquitetural | AWS como referência | Direcionadores de custo |
|---|---|---|
| APIs HTTP | ECS Fargate | vCPU, memória, quantidade de tasks, autoscaling, tempo em execução e tráfego. |
| Workers | ECS Fargate | vCPU, memória, quantidade de tasks, backlog, tempo de processamento e autoscaling. |
| Imagens | ECR | Armazenamento de imagens, transferência, scan e política de retenção de tags. |
| Ledger Database | RDS for PostgreSQL | Classe de instância, storage, IOPS, Multi-AZ, backup, snapshots e retenção. |
| Consolidation Database | RDS for PostgreSQL | Classe de instância, leitura, escrita, índices, storage, backup e retenção. |
| Mensageria | SQS Standard com DLQ | Requisições, payload, retenção, redrive, DLQ e tráfego. |
| Exposição HTTP | API Gateway com AWS WAF, VPC Link/private integration e ALB interno | Requisições, LCU, regras WAF, certificados, tráfego e logs de acesso. |
| Secrets e parâmetros | Secrets Manager e/ou SSM Parameter Store | Quantidade de secrets/parâmetros, chamadas, rotação e criptografia. |
| Criptografia | KMS | Chaves, chamadas criptográficas, rotação e políticas. |
| Observabilidade | ADOT, CloudWatch e X-Ray | Logs ingeridos, retenção, métricas customizadas, traces, dashboards e alarmes. |
| IaC | Terraform com S3 e DynamoDB | Estado remoto em S3, lock em DynamoDB e trilha de auditoria. |
| CI/CD | GitHub Actions com OIDC para AWS | Minutos de execução, storage de artefatos, frequência de pipelines e chamadas AWS. |

---

## 3. Execução local

Na execução local do desafio, não há custo direto de infraestrutura AWS.

A solução local usa:

```text
- Docker Compose
- containers de APIs e workers
- PostgreSQL local para Ledger
- PostgreSQL local para Consolidation
- Keycloak real (identidade OIDC/RS256)
- edge-proxy real (HTTPS/WAF)
- LocalStack (SQS, Secrets Manager, SSM, KMS, IAM)
- Aspire Dashboard local
```

Custos locais relevantes são recursos da máquina do avaliador, tempo de execução, armazenamento local de imagens/volumes e eventual custo indireto de energia ou infraestrutura de desenvolvimento.

---

## 4. Direcionadores por requisito

| Requisito ou decisão | Impacto de custo |
|---|---|
| Separação entre Lançamentos e Consolidado | Aumenta número de unidades e bancos, mas reduz acoplamento e permite escala independente. |
| Outbox transacional | Adiciona tabela, worker e métricas, mas reduz risco de perda silenciosa. |
| Consumo idempotente | Adiciona armazenamento de eventos processados, mas reduz risco financeiro por duplicidade. |
| Projeção DailyBalance | Adiciona persistência própria, mas reduz custo e latência de consulta. |
| Persistências separadas | Aumenta custo de RDS/backups, mas melhora isolamento e governança. |
| SQS com DLQ | Adiciona custo por requisição e retenção, mas simplifica operação gerenciada de mensagens. |
| Observabilidade | Pode se tornar custo relevante por ingestão, retenção e cardinalidade. |
| WAF e controles de segurança | Adicionam custo operacional, mas reduzem risco de abuso e exposição. |
| Terraform e CI/CD | Adicionam custo de pipeline e governança, mas reduzem erro manual e melhoram rastreabilidade. |

---

## 5. Variáveis para estimativa real

Antes de calcular valores, defina:

```text
- região AWS
- número de merchants
- volume diário de lançamentos
- volume de consultas ao Consolidado
- retenção de lançamentos, eventos, logs e traces
- quantidade mínima e máxima de tasks ECS
- tamanho inicial dos bancos RDS
- necessidade de Multi-AZ
- política de backup e snapshots
- requisitos de RTO/RPO
- volume de mensagens SQS e percentual de DLQ
- cardinalidade de métricas e traces
- quantidade de alarmes e dashboards
- tráfego HTTP e regras WAF
- estratégia de ambientes: dev, staging, prod
```

---

## 6. Controles para evitar custo desnecessário

Recomendações:

```text
- usar autoscaling com limites explícitos
- definir retenção curta para logs não críticos em ambientes não produtivos
- evitar métricas de alta cardinalidade
- aplicar lifecycle policy no ECR
- revisar snapshots e backups RDS
- configurar alarmes de custo/budget
- revisar DLQ e redrive para evitar acúmulo silencioso
- executar Terraform plan antes de apply
- destruir ambientes efêmeros quando não forem necessários
```

---

## 7. Relação com ADRs

| ADR | Relação com custo |
|---|---|
| ADR-0005 | Persistências independentes aumentam custo de banco, mas melhoram isolamento. |
| ADR-0006 | PostgreSQL mapeia para RDS for PostgreSQL na referência AWS. |
| ADR-0007 | Canal assíncrono mapeia para SQS Standard com DLQ. |
| ADR-0008 | Quatro unidades implantáveis aumentam operação, mas permitem escala independente. |
| ADR-0009 | Stack .NET/PostgreSQL/containers favorece execução local e mapeamento para AWS. |
| ADR-0010 | Define AWS como plataforma de referência do case. |
| ADR-0011 | Segurança por camadas adiciona IAM, KMS, WAF e secrets. |
| ADR-0012 | Observabilidade adiciona CloudWatch, X-Ray, alarmes e retenção. |
| ADR-0015 | CI/CD, ECR e Terraform adicionam governança e custo operacional de entrega. |

---

## 8. Status

Documento atualizado como referência de direcionadores de custo AWS. Não há cotação oficial nem valores fixos neste arquivo.

## 9. Plataforma AWS multi-conta (ADR-0011)

Direcionadores de custo específicos da plataforma materializada em Terraform (`infra/terraform/environments/{development,staging,production}`), qualitativos, sem cotação exata (perfil `banco-carrefour-pricing` não foi usado por não haver uma decisão concreta que dependesse de um número exato - ver seção 1):

- **NAT Gateway**: um único NAT compartilhado (`single_nat_gateway=true`, usado em Development/Staging por padrão) é significativamente mais barato que um por AZ (`false`, usado em Production) - trade-off é disponibilidade de egress vs custo fixo por hora + processamento de dados por NAT adicional.
- **VPC Interface Endpoints** (ECR, Logs, Secrets Manager, SSM): reduzem tráfego via NAT (que tem custo por GB), mas cada endpoint de interface tem custo fixo por hora, por AZ - vale a pena principalmente onde o volume de pull de imagem/chamadas de API é alto (Production), menos óbvio em Development.
- **RDS Multi-AZ**: dobra o custo de computação/armazenamento do RDS (standby síncrono) - obrigatório em Production (`rds_multi_az=true`), opcional em Staging (avaliação de paridade, default `false`), desabilitado em Development.
- **Capacity canary do Consolidation.Worker**: durante uma avaliação de canário, capacidade extra (`canary_desired_count`) roda em paralelo à capacidade normal - custo adicional transitório, proporcional ao tempo de avaliação (nunca permanente - o canário é removido após promoção/rollback).
- **VPC Link V2 + ALB** (topologia de borda, ver ADR-0008): um único load balancer por ambiente (ALB interno) - a topologia de borda (API Gateway REST → VPC Link V2 diretamente ao ALB, sem NLB intermediário) elimina o custo de um segundo load balancer que uma cadeia com VPC Link clássico exigiria. O VPC Link V2 em si tem custo de ENIs/processamento de dados (qualitativo, sem cotação exata nesta etapa - ver AWS Pricing MCP na seção 1), mas nunca uma cobrança por hora de load balancer adicional.
- **WAF**: cobrança por WebACL + por regra + por milhão de requisições avaliadas - custo proporcional a tráfego real, irrelevante em ambientes sem tráfego real (Development/Staging pré-produção).
- **Retenção de logs por ambiente** (`log_retention_days`): 7 dias em Development, 30 em Staging, 90 em Production - retenção maior em Production reflete requisito de auditoria/investigação, não apenas custo.

Nenhum destes valores foi cotado - nenhuma infraestrutura foi provisionada (ver ADR-0011, classificação "IaC-materializado mas não provisionado").
