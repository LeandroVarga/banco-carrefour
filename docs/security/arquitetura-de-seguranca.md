---
doc_id: SEC-001
titulo: Arquitetura de Segurança
versao: 1.0
status: Baseline local
responsavel: Arquitetura de Soluções
ultima_atualizacao: 2026-07-12
etapa_relacionada: Definition and Decision
---

# Arquitetura de Segurança

## 1. Objetivo

Este documento define a arquitetura de segurança da solução de controle de lançamentos e consulta do consolidado diário.

A segurança cobre acesso externo, autorização por comerciante, proteção de APIs, proteção de dados, comunicação entre componentes, secrets, validação de entrada e controles mínimos para execução local e AWS como plataforma de referência do case.

Este documento complementa a arquitetura descrita em `docs/architecture/05-arquitetura-da-solucao.md`.

---

## 2. Princípios de segurança

A solução segue os seguintes princípios:

```text
- autenticar chamadas externas
- autorizar acesso por comerciante
- impedir consulta cruzada entre comerciantes
- validar entradas antes de processar comandos
- proteger dados financeiros e identificadores sensíveis
- restringir acesso entre componentes internos
- usar menor privilégio entre componentes e segregação completa de credenciais em produção
- não armazenar secrets no código
- registrar eventos relevantes para auditoria e investigação
- aplicar rate limit e proteções contra abuso
- diferenciar controles locais de controles corporativos ou cloud
```

---

## 3. Superfícies de exposição

As principais superfícies de exposição são:

| Superfície | Exemplo | Risco principal | Controle esperado |
|---|---|---|---|
| API de Lançamentos | `POST /entries` | Registro indevido, duplicidade, payload inválido ou abuso de API. | Autenticação, autorização, validação, idempotência e rate limit. |
| API de Consolidado | `GET /daily-balances/{businessDate}` | Consulta cruzada entre comerciantes ou exposição de dados financeiros. | Autenticação, autorização por comerciante e validação de escopo. |
| Banco de Lançamentos | Ledger Database | Acesso indevido à fonte de verdade financeira. | Credenciais restritas, rede controlada, criptografia e auditoria. |
| Banco do Consolidado | Consolidation Database | Exposição ou alteração indevida da projeção consolidada. | Credenciais restritas, rede controlada, criptografia e auditoria. |
| Broker | Message Broker | Publicação, consumo ou leitura indevida de eventos. | Credenciais por componente, permissões mínimas e comunicação protegida. |
| Configuração e secrets | Connection strings, credenciais e chaves | Vazamento de credenciais. | Secrets Manager/SSM na referência AWS e variáveis locais controladas no desafio. |
| Observabilidade | Logs, métricas e traces | Vazamento de dados sensíveis em logs. | Sanitização de logs e correlação sem exposição indevida. |

---

## 4. Autenticação

Chamadas externas às APIs devem ser autenticadas.

Na referência AWS, a autenticação deve ser integrada a um provedor de identidade corporativo usando padrão compatível com OAuth2/OIDC e tokens assinados. Amazon Cognito pode ser usado como referência possível quando fizer sentido para o desenho de implantação.

Na execução local do desafio, a autenticação pode ser representada de forma simplificada, desde que os pontos de integração, claims esperadas e decisões de autorização estejam documentados. O baseline local usa JWT HS256 com validação de assinatura, expiração, issuer e audience.

Informações mínimas esperadas no contexto autenticado:

```text
- identidade do solicitante
- comerciante ou escopo de comerciantes permitido
- papéis ou permissões aplicáveis
- identificador de correlação quando disponível
```

A arquitetura não deve depender de confiança cega em identificadores enviados no payload externo.

---

## 5. Autorização por comerciante

A autorização deve garantir que um comerciante acesse apenas seus próprios dados.

Regra principal:

```text
O comerciante usado para registrar ou consultar dados deve ser obtido do contexto autenticado.
```

Fluxos administrativos futuros com comerciante explícito devem exigir permissão específica e validação de escopo.

Para `POST /entries`:

```text
- o comerciante deve ser derivado exclusivamente do token autenticado
- o payload não deve conter merchantId
- qualquer tentativa de informar merchantId no payload deve ser rejeitada pela validação do contrato
```

Para `GET /daily-balances/{businessDate}`:

```text
- a data pode ser informada na rota
- o comerciante deve ser derivado do contexto autenticado
- consultas administrativas com comerciante explícito exigem permissão específica
```

Esse controle atende diretamente ao ASR-009.

---

## 6. Proteção de APIs

As APIs devem aplicar controles mínimos antes de executar regras de negócio.

Controles:

```text
- autenticação obrigatória para endpoints protegidos
- autorização por comerciante
- validação de payload
- validação de tipo de lançamento
- validação de valor monetário
- validação de moeda aceita no escopo inicial
- validação de data de ocorrência
- validação de chave de idempotência
- limites de tamanho de payload
- rate limit por consumidor, comerciante ou credencial
- respostas de erro padronizadas sem vazamento de detalhe interno
```

O `POST /entries` deve proteger principalmente o caminho de escrita financeira.

O `GET /daily-balances/{businessDate}` deve proteger principalmente o acesso a dados consolidados por comerciante.

---

## 7. Idempotência como controle de segurança e confiabilidade

A idempotência de entrada reduz riscos operacionais e também protege contra duplicidade causada por retries, timeouts ou repetição indevida de chamadas.

Controle esperado:

```text
- chave de idempotência obrigatória para registro de lançamento
- chave associada ao comerciante autenticado
- fingerprint ou hash do payload relevante
- resposta consistente para repetição equivalente
- rejeição ou conflito para repetição com payload divergente
```

A idempotência não substitui autenticação e autorização.

Ela complementa a proteção do fluxo financeiro.

---

## 8. Proteção de dados

A solução lida com dados financeiros de lançamentos e saldos consolidados.

Os seguintes cuidados devem ser aplicados:

```text
- não usar float binário para valores monetários
- registrar somente dados necessários ao escopo
- evitar exposição de dados sensíveis em logs
- proteger connection strings e credenciais
- restringir acesso direto aos bancos
- separar credenciais por fronteira no baseline local e por componente em produção
- aplicar criptografia em trânsito conforme ambiente
- aplicar criptografia em repouso com AWS KMS na referência AWS
- manter rastreabilidade de operações relevantes
```

Os dados financeiros de Lançamentos são a fonte de verdade da solução e exigem proteção mais rigorosa contra alteração indevida.

---

## 9. Segurança entre serviços

A comunicação interna deve seguir o princípio de menor privilégio.

Permissões esperadas por componente:

| Componente | Acessos necessários | Restrições |
|---|---|---|
| Ledger.Api | Ledger Database | Não acessa Consolidation Database nem consome diretamente do broker. |
| Ledger.OutboxPublisher | Ledger Database e Message Broker | Não expõe API pública e não acessa Consolidation Database. |
| Consolidation.Worker | Message Broker e Consolidation Database | Não acessa Ledger Database diretamente. |
| Consolidation.Api | Consolidation Database | Não acessa Ledger Database nem publica mensagens. |

Essa separação reduz acoplamento e limita impacto em caso de falha ou credencial comprometida.

No Docker Compose local, o menor privilégio é demonstrado parcialmente: Ledger e Consolidado usam bancos e credenciais PostgreSQL separados por fronteira, e cada componente recebe apenas a connection string da persistência que utiliza. O RabbitMQ local mantém a credencial de desenvolvimento `ledger` / `ledger` compartilhada entre publisher e consumer para preservar simplicidade e reprodutibilidade do case. Em produção, a separação esperada é credencial distinta para produtor, consumidor e operação, com permissões restritas por exchange, fila, routing key e vhost.

---

## 10. Segurança do broker

O broker transporta eventos de lançamentos para o Consolidado.

Controles esperados:

```text
- credenciais específicas para produtor e consumidor
- permissões mínimas para publicar e consumir
- filas, exchanges ou tópicos restritos ao fluxo necessário
- autenticação no acesso ao broker
- comunicação protegida conforme ambiente
- isolamento de mensagens com falha persistente
- monitoramento de filas, erros e backlog
```

O broker não é fonte de verdade financeira.

Eventos devem ser recuperáveis a partir da Outbox e dos lançamentos persistidos.

---

## 11. Secrets e configuração

Secrets não devem ser versionados no repositório.

Itens tratados como secrets:

```text
- connection strings
- senhas de banco
- credenciais do broker
- chaves de assinatura ou validação de tokens
- credenciais de observabilidade
- tokens administrativos
```

No ambiente local do desafio, variáveis de ambiente podem ser usadas para parametrização.

Na referência AWS, os secrets devem ser armazenados no AWS Secrets Manager e/ou no SSM Parameter Store, com criptografia por KMS e acesso por IAM role de componente.

Arquivos de exemplo podem existir, desde que não contenham valores reais.

---

## 12. Logs, auditoria e privacidade

Logs devem apoiar diagnóstico e auditoria sem expor dados sensíveis de forma indevida.

Logs devem conter:

```text
- correlation_id
- request_id quando aplicável
- merchant_id ou identificador pseudonimizado quando necessário
- operação executada
- resultado da operação
- código de erro padronizado
- componente de origem
- timestamp
```

Logs não devem conter:

```text
- secrets
- senhas
- tokens completos
- connection strings
- payloads sensíveis completos
- dados financeiros além do necessário para diagnóstico
```

Eventos relevantes de segurança devem ser rastreáveis para investigação.

---

## 13. Rate limit e proteção contra abuso

As APIs devem prever limitação de uso para reduzir abuso, sobrecarga e impacto em disponibilidade.

Controles esperados:

```text
- rate limit por credencial, cliente ou comerciante
- limites específicos para endpoints de escrita e leitura
- proteção contra payloads excessivos
- timeouts de requisição
- respostas padronizadas para limites excedidos
- métricas de rejeição por rate limit
```

O requisito de 50 RPS se aplica ao Consolidado e deve ser medido junto com a taxa de falhas ou perdas no pico.

No baseline atual, `Ledger.Api` e `Consolidation.Api` implementam rate limiting básico local/in-memory nos endpoints de negócio:

```text
- POST /entries
- GET /daily-balances/{businessDate}
```

Os endpoints `GET /health/live` e `GET /health/ready` não aplicam rate limit.

Quando o limite é excedido, a resposta é `HTTP 429 Too Many Requests` no padrão `ErrorResponse`, preservando o `correlationId` quando informado por `X-Correlation-Id` válido.

O particionamento usa `merchant_id` quando a requisição está autenticada e fallback por IP/anonymous para requisições sem contexto autenticado.

Esse controle local não substitui rate limiting distribuído/produtivo em API Gateway, WAF, ingress, service mesh ou mecanismo equivalente.

---

## 14. Segurança local e segurança AWS de referência

A execução local do desafio materializa os fluxos principais de forma reproduzível.

Ela não substitui a segurança completa de produção.

| Aspecto | Execução local | AWS como referência do case |
|---|---|---|
| Autenticação | JWT local com assinatura, expiração, issuer, audience e `merchant_id`. | IdP corporativo via OIDC/OAuth2, com Cognito como referência possível. |
| Secrets | Variáveis de ambiente locais e exemplos sem segredo real. | Secrets Manager e/ou SSM Parameter Store com KMS. |
| Banco de dados | Credenciais locais controladas. | RDS PostgreSQL com IAM/security groups, criptografia KMS, backup e controle de rede. |
| Mensageria | Credencial RabbitMQ local compartilhada `ledger` / `ledger` para preservar simplicidade e reprodutibilidade. | SQS com IAM por produtor/consumidor, DLQ, KMS quando aplicável e política de acesso mínima. |
| Comunicação | Rede local de containers. | VPC/subnets, security groups, TLS, mTLS onde aplicável, API Gateway ou ALB com WAF. |
| Observabilidade | Logs e métricas locais. | ADOT, CloudWatch Logs/Metrics/Alarms e X-Ray. |

Essa separação evita confundir a execução local com a topologia definitiva de produção.

---

## 15. Ameaças e controles principais

| Ameaça | Impacto | Controle arquitetural |
|---|---|---|
| Comerciante acessa dados de outro comerciante | Exposição de dados financeiros. | Autorização por comerciante e validação do contexto autenticado. |
| Requisição repetida cria lançamento duplicado | Distorção financeira. | Idempotência de entrada por comerciante e chave. |
| Evento duplicado altera saldo mais de uma vez | Distorção do consolidado. | Processamento idempotente e registro de eventos processados. |
| Payload inválido ou malformado | Erro de processamento ou inconsistência. | Validação de entrada e respostas padronizadas. |
| Credencial de componente comprometida | Acesso indevido a recurso interno. | Menor privilégio; no baseline local, credenciais de banco separadas por fronteira; em produção, credenciais segregadas por componente e recurso. |
| Secret versionado no repositório | Vazamento de credenciais. | Uso de variáveis locais e secret manager em produção. |
| Logs expõem dados sensíveis | Vazamento operacional. | Sanitização de logs e restrição de payloads sensíveis. |
| Abuso de API de consulta | Indisponibilidade ou degradação. | Rate limit, métricas e escalabilidade da API de Consolidado. |
| Broker indisponível ou inacessível | Atraso na consolidação. | Outbox durável, retry e monitoramento. |
| Acesso direto indevido aos bancos | Exposição ou alteração de dados. | Rede restrita, credenciais por componente e auditoria. |

---

## 16. Relação com ASRs, ABBs e SBBs

| Item | Relação com segurança |
|---|---|
| ASR-009 | Garante acesso aos dados conforme comerciante autenticado e autorizado. |
| ASR-010 | Exige observabilidade do fluxo para diagnóstico e investigação. |
| ABB-015 | Define segurança de acesso. |
| ABB-016 | Define controle de comunicação entre serviços. |
| SBB-014 | Materializa autenticação e autorização. |
| SBB-015 | Materializa segurança entre serviços. |
| SBB-019 | Materializa configuração e secrets. |

---

## 17. Relação com ADRs

| ADR | Relação com segurança |
|---|---|
| ADR-0001 | Separa fronteiras, reduzindo acoplamento entre responsabilidades. |
| ADR-0005 | Define persistências independentes, evitando compartilhamento indevido de dados. |
| ADR-0007 | Define broker como canal de comunicação assíncrona, exigindo controle de acesso ao canal. |
| ADR-0008 | Define unidades implantáveis separadas, permitindo menor privilégio por componente. |
| ADR-0009 | Define stack de referência que deve aplicar padrões de segurança por camada. |
| ADR-0010 | Separa execução local, AWS de referência e produção real. |
| ADR-0011 | Consolida as decisões de autenticação, autorização, proteção de APIs, dados, secrets e comunicação entre serviços. |

A decisão específica de segurança está registrada em `docs/decisions/ADR-0011-decisoes-de-seguranca.md`.

---

## 18. Critérios de aceitação de segurança

| ID | Critério |
|---|---|
| SEC-CA-001 | APIs externas exigem autenticação. |
| SEC-CA-002 | Consultas e registros respeitam o comerciante autorizado. |
| SEC-CA-003 | Payloads inválidos são rejeitados antes do processamento. |
| SEC-CA-004 | Requisições repetidas não criam duplicidade indevida. |
| SEC-CA-005 | Eventos duplicados não duplicam efeito financeiro no Consolidado. |
| SEC-CA-006 | Secrets não são versionados no repositório. |
| SEC-CA-007 | Baseline local demonstra separação de credenciais por fronteira de banco e documenta segregação completa de credenciais por componente como requisito produtivo. |
| SEC-CA-008 | Logs não expõem secrets, tokens completos ou payloads sensíveis completos. |
| SEC-CA-009 | Acesso direto aos bancos e broker é restrito aos componentes necessários. |
| SEC-CA-010 | Execução local e produção possuem premissas de segurança diferenciadas. |

---

## 19. Itens para detalhamento futuro

Os seguintes pontos dependem de decisão de plataforma ou ambiente:

```text
- provedor de identidade corporativo real
- estratégia final de emissão e validação de tokens
- API Gateway, ALB ou WAF final
- mTLS ou service mesh
- parâmetros finais de Secrets Manager/SSM e KMS
- política de rotação de credenciais
- criptografia em repouso conforme serviço gerenciado
- auditoria centralizada
- política final de retenção de logs
- hardening de containers e imagens
```

---

## 20. Status

Documento atualizado como baseline local de segurança e referência AWS do case. Hardening produtivo com IdP/OIDC real, TLS/mTLS, Secrets Manager/SSM, KMS, IAM, WAF, credenciais completas por componente e validações de segurança permanece pendente.
