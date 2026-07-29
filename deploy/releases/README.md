# deploy/releases

Este diretório **não armazena manifestos de release commitados**. A fonte
de verdade de uma release imutável é sempre externa ao conteúdo deste
diretório:

- o **workflow artifact** `release-manifest` produzido por
  `.github/workflows/publish-images.yml` (retenção de 90 dias); ou
- um **GitHub Release** cujo asset é o mesmo manifesto, para preservação
  além dos 90 dias de retenção de artifact (passo manual, ainda não
  executado - nenhuma publicação real no Amazon ECR ocorreu).

Commitar um manifesto de release aqui seria indistinguível de "fabricar"
uma release oficial sem uma publicação real correspondente - exatamente o
que este repositório se compromete a nunca fazer (ver
`schemas/examples/README.md` para o exemplo não-produtivo, que vive em
`schemas/examples/`, não aqui).

## Como uma release é referenciada por um ambiente

O Amazon ECR é o único registry oficial (ADR-0013). Os workflows de
deployment AWS reais (`deploy-development.yml`, `promote-staging.yml`,
`promote-production.yml`) já estão implementados e validados
estruturalmente (ver `deploy/ecs/README.md`), mas nunca foram executados
contra uma conta AWS real - dependem de configuração externa de GitHub
Environment e de credenciais AWS que este repositório não provisiona.
Cada workflow recebe a release a promover como **input explícito** (run-id
do workflow de publicação, ou tag do GitHub Release) no momento do
disparo - nunca lida implicitamente de um arquivo estático neste
diretório.

## Rollback

`rollback-production.yml` já está implementado e validado
estruturalmente (ver `deploy/ecs/README.md`), mas nunca foi executado
contra uma conta AWS real. Ele exige a **release imutável anterior** já
preservada como artifact/Release - o mesmo mecanismo acima, nunca
reconstruída. O contrato de elegibilidade (release anterior precisa ser
real, nunca fabricada; nunca reconstrói; consome os mesmos digests já
publicados; bootstrap - a primeira release não tem predecessora - falha
de forma explícita) é validado estruturalmente por teste de arquitetura
e reflete o comportamento exigido do workflow real (ADR-0014).
