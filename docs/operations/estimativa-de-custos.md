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
| Exposição HTTP | API Gateway ou ALB com AWS WAF | Requisições, LCU, regras WAF, certificados, tráfego e logs de acesso. |
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
- RabbitMQ local
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
