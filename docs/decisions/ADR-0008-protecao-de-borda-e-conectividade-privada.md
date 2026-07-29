---
adr_id: ADR-0008
titulo: Proteção de borda e conectividade privada
status: Aceita
categoria: Segurança
altitude_decisoria: Estrutural
data: 2026-07-28
responsavel: Arquitetura de Soluções
---

# ADR-0008 — Proteção de borda e conectividade privada

## 1. Contexto

Expor `Ledger.Api` e `Consolidation.Api` diretamente, sem TLS, WAF ou limite de borda, deixa a solução vulnerável a ataques comuns e não representa uma topologia de produção defensável.

## 2. Pergunta arquitetural

Como as APIs são protegidas na borda e conectadas de forma privada aos workloads de aplicação?

## 3. Decisão

Localmente, a única entrada externa de negócio é o `edge-proxy` (nginx + ModSecurity com o OWASP Core Rule Set), acessível em `https://localhost:8443`. TLS é obrigatório; a porta HTTP redireciona para HTTPS. `Ledger.Api` é alcançada em `/ledger/` e `Consolidation.Api` em `/consolidation/`, ambas sem porta própria publicada no host — o roteamento é feito por path na mesma origem `localhost`. O Keycloak é alcançado em um virtual host dedicado, `https://keycloak.localhost:8443`, garantindo que o `issuer`/`jwks_uri` do discovery document permaneçam estáveis independentemente de o chamador ser interno ou externo. ModSecurity/CRS bloqueia SQL injection, XSS e path traversal; rate limiting e validação de tamanho de corpo protegem contra abuso na borda.

Na AWS de referência, a cadeia equivalente é: AWS WAF (WebACL associada ao stage) → Amazon API Gateway REST regional (único ponto de entrada público) → VPC Link V2 (`aws_apigatewayv2_vpc_link`, ENIs próprias, security group com egress escopado apenas ao ALB interno) → ALB interno → ECS/Fargate. Não há NLB intermediário: o VPC Link V2 conecta a API Gateway REST diretamente ao ALB interno, sem exigir um Network Load Balancer. O VPC Link clássico (v1) nunca é usado. Todos os recursos da cadeia pertencem à mesma conta de workload — nunca uma referência cross-account dentro dessa cadeia. O security group do ALB só aceita ingress a partir do security group do VPC Link V2, nunca um CIDR amplo da VPC.

## 4. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar |
|---|---|---|
| Expor as APIs diretamente em portas do host | `Ledger.Api`/`Consolidation.Api` publicando porta própria. | Elimina qualquer defesa de borda contra ataques comuns e não representa uma topologia defensável. |
| VPC Link clássico (v1) com NLB intermediário | Conectar a API Gateway REST via VPC Link v1, que exige um Network Load Balancer entre a API Gateway e o ALB. | Rejeitada. A documentação oficial da AWS recomenda não criar novos VPC Links v1, que são tratados como integração legada; o VPC Link V2 conecta diretamente a um ALB sem exigir NLB. |
| API Gateway com invocação de tráfego real localmente | Rotear o tráfego local através de uma emulação de API Gateway. | A invocação real de tráfego via API Gateway REST não é estável na ferramenta de emulação local usada neste projeto — o plano de controle é validado via Terraform, mas o caminho quente do Compose permanece `edge-proxy` → APIs. |
| `edge-proxy` HTTPS com WAF local, e WAF/API Gateway REST/VPC Link V2/ALB interno na referência AWS | Borda real localmente, cadeia equivalente documentada e validada em Terraform na AWS. | Alternativa adotada. Protege contra ataques comuns em ambos os ambientes, sem NLB desnecessário nem VPC Link legado. |

## 5. Trade-offs

O `edge-proxy` é um ponto único de falha local, mitigado por ser o único escopo deste ambiente (sem alta disponibilidade local exigida). Na AWS, a cadeia completa (WAF → API Gateway → VPC Link V2 → ALB) adiciona latência e componentes a operar em troca de defesa em profundidade e roteamento privado.

## 6. Consequências

Toda requisição de negócio, local ou na referência AWS, passa por pelo menos uma camada de proteção de borda antes de alcançar a aplicação. Autenticação e autorização reais permanecem responsabilidade da aplicação (ADR-0007) — a borda nunca atua como autorizador de identidade.

## 7. Guardrails

- `Ledger.Api`/`Consolidation.Api` nunca publicam porta própria no host, local ou na AWS.
- O VPC Link clássico (v1) e qualquer NLB inserido apenas para expor o ALB ao VPC Link são rejeitados — testes de arquitetura impedem sua reintrodução.
- Todos os recursos da cadeia de borda AWS pertencem à mesma conta de workload.

## 8. Risco arquitetural evitado

Uma implementação futura não deve expor as APIs de aplicação diretamente, contornar os controles de borda, ou reintroduzir VPC Link V1 com um NLB intermediário.

## 9. ASRs relacionados

ASR-009 (acesso autenticado e autorizado por comerciante), ASR-010 (fluxo observável).

## 10. ABBs e SBBs relacionados

ABB-015 (Segurança de Acesso), ABB-016 (Controle de Comunicação entre Serviços); SBB-015 (Service-to-Service Security).

## 11. Evidências de implementação

`infra/edge-proxy/default.conf.template`, `docker/api-entrypoint.sh`, `infra/terraform/modules/edge`, `tests/Security.IntegrationTests/Edge/{TlsAndHeadersTests,WafTests,AuthenticatedEdgeFlowTests}.cs`, `tests/Architecture.Tests/AwsPlatformGovernanceArchitectureTests.cs` (guardas de VPC Link V1/NLB).

## 12. ADRs relacionados

ADR-0007 (identidade, autorização e isolamento por merchant), ADR-0010 (execução local e paridade comportamental), ADR-0011 (plataforma AWS e isolamento de ambientes).
