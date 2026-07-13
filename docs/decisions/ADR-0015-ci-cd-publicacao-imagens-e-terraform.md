---
adr_id: ADR-0015
titulo: CI/CD, Publicação de Imagens e Terraform
status: Aceita
data: 2026-07-13
responsavel: Arquitetura de Soluções
decisao_relacionada: Entrega automatizada e infraestrutura como código para a referência AWS
---

# ADR-0015 - CI/CD, Publicação de Imagens e Terraform

## 1. Contexto

A ADR-0010 define AWS como plataforma de referência do case, preservando execução local por Docker Compose.

Para transformar essa referência em entrega operacional, a solução precisa definir como validar código, versionar imagens, publicar no Amazon ECR, provisionar recursos por Terraform e implantar APIs e workers no Amazon ECS Fargate.

Esta ADR registra a decisão de entrega. Ela depende da ADR-0010 e não substitui a escolha da AWS como plataforma de referência.

---

## 2. Decisão

A solução adotará GitHub Actions como mecanismo de CI/CD e Terraform como infraestrutura como código para a implantação AWS de referência.

O desenho de entrega prevê:

```text
- CI com build e testes container-first
- autenticação do GitHub Actions na AWS por OIDC
- versionamento de imagens por commit SHA e tag de release
- publicação das imagens no Amazon ECR
- Terraform plan para revisão de infraestrutura
- Terraform apply protegido por ambiente e aprovação manual quando aplicável
- deploy no Amazon ECS Fargate
- smoke tests após deploy
- rollback por imagem anterior e reversão controlada de infraestrutura
```

---

## 3. O que a decisão inclui

Esta decisão inclui:

```text
- GitHub Actions como orquestrador de CI/CD
- OIDC para AWS, sem access keys fixas no repositório
- ECR como registry de imagens
- Terraform para VPC/rede, API Gateway, WAF, VPC Link/private integration, ALB interno, ECS, ECR, RDS, SQS/DLQ, IAM, Secrets, KMS, observabilidade, alarmes e parâmetros
- S3 backend com lock em DynamoDB para estado Terraform quando houver ambiente AWS real
- separação entre CI local/container-first existente e CD AWS de referência
- proteção de ambientes e aprovações para apply/deploy quando aplicável
```

---

## 4. O que fica fora desta decisão

Esta decisão não afirma que:

```text
- já existe conta AWS configurada
- Terraform foi aplicado em ambiente real
- imagens foram publicadas no ECR
- deploy em ECS foi executado
- smoke tests AWS foram executados
```

Esses itens devem virar evidência somente após execução real em ambiente controlado.

---

## 5. Consequências

Consequências positivas:

```text
- evita credenciais estáticas no repositório
- torna build, teste, publicação e deploy rastreáveis
- separa validação de código de implantação em AWS
- permite revisão de infraestrutura por Terraform plan
- cria caminho claro de rollback por versão de imagem
```

Tradeoffs:

```text
- exige setup de conta AWS, IAM, OIDC, backend remoto e ambientes protegidos
- exige disciplina de versionamento e promoção de imagens
- exige validação real para converter desenho documental em evidência executada
```

---

## 6. Relação com documentos

Esta decisão sustenta:

- [runbook-implantacao-aws.md](../operations/runbook-implantacao-aws.md)
- [arquitetura-operacional.md](../operations/arquitetura-operacional.md)
- [evidencias-do-case.md](../operations/evidencias-do-case.md)
- [infra/README.md](../../infra/README.md)
- `.github/workflows/ci.yml`

---

## 7. Status

Decisão aceita para orientar a entrega AWS de referência. No estado atual, apenas o CI container-first está implementado; publicação de imagens, execução de Terraform em ambiente AWS e deploy AWS permanecem como próximos passos de execução.
