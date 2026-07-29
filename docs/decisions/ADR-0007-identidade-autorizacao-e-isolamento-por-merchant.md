---
adr_id: ADR-0007
titulo: Identidade, autorização e isolamento por merchant
status: Aceita
categoria: Segurança
altitude_decisoria: Estrutural
data: 2026-07-28
responsavel: Arquitetura de Soluções
---

# ADR-0007 — Identidade, autorização e isolamento por merchant

## 1. Contexto

O Ledger e o Consolidation lidam com dados financeiros por comerciante. Toda chamada externa precisa ser autenticada, autorizada por operação e restrita ao comerciante correto — sem isso, um comerciante poderia acessar ou registrar dados em nome de outro.

## 2. Pergunta arquitetural

Como um chamador é autenticado e restrito ao comerciante e à operação corretos?

## 3. Decisão

A identidade usa OAuth2/OpenID Connect. Localmente, o Identity Provider é o Keycloak, com um realm dedicado (`banco-carrefour`); na AWS de referência, o papel equivalente é cumprido por um IdP corporativo ou gerenciado via OIDC/OAuth2. Tokens de acesso são assinados com RS256, validados por discovery document e JWKS — nunca por chave simétrica compartilhada.

A autorização exige, por operação: token válido (assinatura, issuer, audience, tempo de vida), audience compatível com o recurso (`ledger-api` ou `consolidation-api`), a claim `merchant_id` presente e dentro do limite de tamanho validado, e o scope específico da operação (`ledger.write` para `POST /entries`, `consolidation.read` para `GET /daily-balances/{businessDate}`). O mapper de audience vive dentro de cada client scope de negócio, de modo que a audience concedida depende exatamente do scope solicitado no pedido de token.

`merchant_id` deriva sempre da claim do token — nunca de query string, corpo da requisição ou header controlado pelo cliente (ver ADR-0000). Ausência de `merchant_id` ou de scope correto resulta em 403; token inválido resulta em 401; audience incompatível é rejeitada na validação do middleware de autenticação, antes de qualquer avaliação de política de autorização. Um comerciante nunca acessa dados de outro, mesmo com token válido e scope correto para um recurso diferente.

## 4. Alternativas consideradas

| Alternativa | Descrição | Motivo para não adotar |
|---|---|---|
| JWT HS256 com chave simétrica compartilhada | Assinatura e validação com a mesma chave hardcoded nas APIs. | Rejeitada como modelo definitivo. Não tem Identity Provider real, não tem descoberta OIDC nem JWKS, e a mesma chave de assinatura compartilhada entre APIs aumenta o impacto de um vazamento. |
| Confiar no `merchant_id` informado no payload | A API aceitaria o comerciante enviado pelo cliente. | Permite registro ou consulta indevida em nome de outro comerciante. |
| Autenticação sem autorização por comerciante | Usuário autenticado poderia acessar qualquer comerciante informando o identificador. | Não protege contra consulta cruzada entre comerciantes. |
| Keycloak/OIDC com RS256, JWKS, scopes por operação e `merchant_id` exclusivamente do token | IdP real localmente, papel equivalente na referência AWS, autorização por operação e por comerciante. | Alternativa adotada. Elimina a chave simétrica compartilhada e introduz autorização granular e testável. |

## 5. Trade-offs

Um IdP real adiciona dependência operacional (Keycloak e seu banco de dados localmente) e exige bootstrap de realm, clients e secrets, em troca de eliminar a chave simétrica compartilhada e permitir autorização granular por escopo e por comerciante.

## 6. Consequências

Testes de identidade usam tokens RS256 reais emitidos pelo IdP, nunca geração manual de token simétrico. A prova de isolamento entre comerciantes usa clientes de teste genuinamente distintos, nunca o mesmo client com `merchant_id` alterado manualmente.

## 7. Guardrails

- HS256 e chave simétrica compartilhada nunca aparecem como caminho produtivo — apenas como alternativa rejeitada nesta ADR.
- `merchant_id` nunca é aceito de payload, query string ou header controlado pelo cliente.
- Testes de arquitetura e de segurança impedem geração manual de token HS256 fora de `tests/`.
- A prova de isolamento entre comerciantes usa identidades reais e distintas, nunca simulação por parâmetro.

## 8. Risco arquitetural evitado

Uma implementação futura não deve adotar HS256 com chave compartilhada como modelo definitivo, confiar em um identificador de comerciante fornecido pelo chamador, nem permitir acesso cruzado entre comerciantes.

## 9. ASRs relacionados

ASR-009 (acesso autenticado e autorizado por comerciante), ASR-010 (fluxo observável para diagnóstico).

## 10. ABBs e SBBs relacionados

ABB-015 (Segurança de Acesso), ABB-016 (Controle de Comunicação entre Serviços); SBB-014 (Authentication and Authorization).

## 11. Evidências de implementação

`infra/keycloak/realm/banco-carrefour-realm.json`, `src/Ledger/Ledger.Api/Authentication/LedgerAuthentication.cs`, `src/Consolidation/Consolidation.Api/Authentication/ConsolidationAuthentication.cs`, `tests/Security.IntegrationTests/Identity/{TokenValidationTests,ScopeAuthorizationTests,MerchantIsolationTests}.cs`.

## 12. ADRs relacionados

ADR-0000 (semântica financeira e data de negócio), ADR-0005 (contratos HTTP e eventos de integração), ADR-0008 (proteção de borda e conectividade privada), ADR-0009 (menor privilégio, secrets e criptografia).
