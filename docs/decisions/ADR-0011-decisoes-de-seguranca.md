---
adr_id: ADR-0011
titulo: Decisões de Segurança
status: Aceita
data: 2026-07-11
responsavel: Arquitetura de Soluções
decisao_relacionada: Autenticação, autorização, proteção de APIs, dados, secrets e comunicação entre serviços
---

# ADR-0011 — Decisões de Segurança

## 1. Contexto

A solução lida com lançamentos financeiros, saldos consolidados e dados associados a comerciantes.

O desafio exige que a arquitetura trate segurança de forma explícita, incluindo autenticação, autorização, proteção de APIs, proteção de dados sensíveis, criptografia, controle de acesso entre serviços e proteção contra ataques comuns.

A arquitetura separa Lançamentos e Consolidado em fronteiras distintas, usa persistências independentes, broker assíncrono, APIs externas, workers internos, execução local reproduzível e AWS como plataforma de referência do case.

Esses elementos exigem uma estratégia de segurança que proteja o acesso externo, restrinja permissões internas, evite consulta cruzada entre comerciantes e proteja credenciais e dados financeiros.

---

## 2. Decisão

A solução adotará uma arquitetura de segurança baseada em autenticação obrigatória, autorização por comerciante, menor privilégio entre componentes, proteção de APIs, proteção de secrets, validação de entrada, rate limit e separação entre controles locais e controles da referência AWS.

As chamadas externas às APIs devem ser autenticadas.

Na execução local do desafio, a autenticação é representada por JWT HS256 com validação de assinatura, expiração, issuer e audience. Esse baseline local mantém o formato de integração testável sem exigir um provedor externo.

O acesso aos dados deve ser autorizado por comerciante.

A identificação do comerciante deve ser obtida do contexto autenticado ou validada contra ele.

Componentes internos devem acessar apenas os recursos necessários. Na execução local do desafio, a separação é materializada parcialmente: bancos separados e connection strings por fronteira; RabbitMQ usa credencial compartilhada de desenvolvimento para preservar simplicidade e reprodutibilidade. Em produção, a segregação por produtor, consumidor e operação é mandatória.

Na AWS de referência, segurança deve usar IdP corporativo via OIDC/OAuth2, Amazon Cognito quando aplicável, IAM roles por componente, Secrets Manager e/ou SSM Parameter Store, KMS, security groups, VPC/subnets, WAF, TLS/mTLS onde aplicável, auditoria e menor privilégio.

Secrets não devem ser versionados no repositório.

Logs, métricas e traces devem apoiar diagnóstico sem expor secrets, tokens completos ou payloads sensíveis completos.

O detalhamento da decisão está documentado em `docs/security/arquitetura-de-seguranca.md`.

---

## 3. O que a decisão inclui

Esta decisão inclui:

```text
- autenticação de chamadas externas
- autorização por comerciante
- proteção contra consulta cruzada entre comerciantes
- validação de payloads antes do processamento
- idempotência de entrada como proteção contra duplicidade indevida
- controle de eventos duplicados no Consolidado
- rate limit e proteção contra abuso
- menor privilégio entre componentes, com segregação completa de credenciais como alvo produtivo
- menor privilégio entre APIs, workers, bancos e broker
- proteção de secrets e configuração sensível
- sanitização de logs
- distinção entre segurança local e segurança AWS de referência
- IdP corporativo via OIDC/OAuth2, com Cognito como referência possível
- IAM roles por componente
- Secrets Manager/SSM, KMS, security groups, VPC/subnets e WAF
```

---

## 4. O que fica fora desta decisão

Esta decisão não define:

```text
- provedor de identidade corporativo real
- desenho final de API Gateway, ALB, ingress ou WAF
- estratégia final de mTLS ou service mesh
- parâmetros finais de Secrets Manager/SSM e KMS
- política final de rotação de credenciais
- política final de retenção de logs
- configuração final de criptografia em repouso por serviço gerenciado
- ferramenta final de auditoria centralizada
- hardening final de containers e imagens
```

Esses pontos dependem do ambiente real adotado e devem ser detalhados na etapa de implantação e operação.

---

## 5. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar como base da solução |
|---|---|---|
| Sem autenticação na avaliação local | APIs seriam expostas sem representação de identidade. | Simplifica a execução local, mas não demonstra os controles obrigatórios do desafio. |
| Autenticação sem autorização por comerciante | Usuário autenticado poderia acessar dados se informasse identificadores válidos. | Não protege contra consulta cruzada entre comerciantes. |
| Confiar no merchant_id informado no payload | API aceitaria o comerciante enviado pelo cliente sem validação contra o contexto autenticado. | Permite registro ou consulta indevida em nome de outro comerciante. |
| Credencial única irrestrita para todos os recursos em produção | APIs, workers e operação compartilhariam o mesmo acesso amplo a bancos e broker. | Aumenta impacto de credencial comprometida e viola menor privilégio. O baseline local usa simplificações controladas, não uma credencial produtiva irrestrita. |
| Segurança baseada apenas em rede interna | Componentes internos seriam considerados confiáveis por estarem na mesma rede. | Não reduz adequadamente riscos de movimento lateral, erro de configuração ou credencial comprometida. |
| Segurança por camadas e menor privilégio | Autenticação, autorização, validação, secrets, permissões por componente e proteção de logs. | Alternativa adotada. Atende ao desafio e preserva evolução para a referência AWS ou padrão corporativo equivalente. |

---

## 6. Consequências

Consequências positivas:

```text
- protege dados financeiros por comerciante
- reduz risco de consulta cruzada
- reduz risco de registro indevido em nome de outro comerciante
- limita impacto de credenciais comprometidas
- evita versionamento de secrets reais
- melhora auditabilidade e investigação
- atende aos requisitos obrigatórios de segurança do desafio
- prepara a solução para integração com padrões corporativos e com a referência AWS do case
```

Consequências e tradeoffs:

```text
- exige propagação correta do contexto autenticado
- exige validação consistente de escopo por comerciante
- exige configuração separada de credenciais por componente em produção
- exige cuidado para não expor dados sensíveis em logs
- exige tratamento diferenciado entre execução local e produção
- deixa decisões finais dependentes da plataforma real de produção
- mantém pendentes para produção a integração real com IdP/OIDC, TLS/mTLS, rotação de chaves, WAF, IAM e gestão de secrets
```

---

## 7. Relação com requisitos, ABBs e SBBs

Esta decisão sustenta principalmente:

```text
- ASR-009: O acesso aos dados deve respeitar o comerciante autenticado e autorizado
- ASR-010: O fluxo deve ser observável para diagnóstico e investigação
- ABB-015: Segurança de Acesso
- ABB-016: Controle de Comunicação entre Serviços
- SBB-014: Authentication and Authorization
- SBB-015: Service-to-Service Security
- SBB-019: Configuration and Secrets
```

---

## 8. Relação com documentos

Esta decisão sustenta:

```text
- docs/security/arquitetura-de-seguranca.md
- docs/architecture/04-blocos-de-solucao.md
- docs/architecture/05-arquitetura-da-solucao.md
- docs/architecture/07-rastreabilidade.md
```

---

## 9. Status

Decisão aceita para o escopo inicial da solução.
