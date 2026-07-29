---
doc_id: SEC-002
titulo: Dependências e Segurança da Cadeia de Suprimentos
versao: 1.1
status: Atualizado
responsavel: Arquitetura de Soluções
ultima_atualizacao: 2026-07-27
etapa_relacionada: Supply chain, SBOM, governança de dependências e scan de vulnerabilidade (ADR-0013)
---

# Dependências e Segurança da Cadeia de Suprimentos

## 1. Objetivo

Estabelecer uma baseline reproduzível de segurança da cadeia de suprimentos para: dependências NuGet, GitHub Actions, imagens base Docker, as 4 imagens implantáveis reais, código-fonte, SBOMs, evidência de vulnerabilidade, revisão de dependências em PR e análise estática de código. Este bloco prepara artefatos de release para publicação futura, mas **não publica nada** (sem `docker push`, sem OIDC/AWS, sem atestação).

## 2. Ferramentas selecionadas e por quê

| Necessidade | Ferramenta | Por quê |
|---|---|---|
| Vulnerabilidade NuGet (direta/transitiva) | `dotnet list package --vulnerable --include-transitive` (nativo do SDK 8.0.423) | Já resolve o grafo real do projeto; nenhuma ferramenta adicional necessária |
| SBOM das 4 imagens (CycloneDX) | Trivy (`aquasec/trivy`), invocado como container fixado por digest | Cobre SO (Debian) + .NET/NuGet no mesmo scan; formato unico reduz ferramentas sobrepostas |
| Vulnerabilidade das 4 imagens | O mesmo Trivy (subcomando `image`) | Evita introduzir um segundo scanner com responsabilidade sobreposta |
| Revisão de dependências em PR | `actions/dependency-review-action` (oficial GitHub) | Cobre exatamente o delta do PR; nenhuma alternativa nativa equivalente |
| Análise estática de código C# | CodeQL (`github/codeql-action`, oficial GitHub) | Suporte nativo a C#, sem custo de licença, hospedado pelo GitHub |
| Atualização automatizada de dependências | Dependabot (`.github/dependabot.yml`) | Nativo do GitHub, sem Action adicional |
| Licenças | Extraídas do próprio SBOM CycloneDX | Evita uma ferramenta de licença dedicada quando o SBOM já carrega essa informação |

Alternativas consideradas e descartadas: Syft (geração de SBOM separada do scanner de vulnerabilidade — sobreposição de responsabilidade com o Trivy escolhido, sem ganho concreto para este repositório); Grype (mesma cobertura de vulnerabilidade do Trivy, sem motivo para manter os dois); Microsoft SBOM Tool (cobertura mais fraca para pacotes de SO Debian herdados pela imagem base, que o Trivy já cobre nativamente).

### Trivy: decisão de confiança e pinagem

Em 2026-03, a cadeia de suprimentos do próprio Trivy foi brevemente comprometida (GHSA-69fq-xp46-6x23, critical): o binário/imagem `trivy` v0.69.4–v0.69.6 e praticamente todas as tags de `aquasecurity/trivy-action`/`aquasecurity/setup-trivy` foram substituídas por malware via credenciais comprometidas (~3–12h de exposição, já removido). Há também GHSA-9p44-j4g5-cfx5 (moderate, injeção de comando via variável de ambiente não sanitizada em `trivy-action` < 0.34.0).

Decisão: **não usar `aquasecurity/trivy-action`** (a Action) em nenhuma hipótese. O Trivy é invocado exclusivamente como container `docker.io/aquasec/trivy@sha256:cffe3f5161a47a6823fbd23d985795b3ed72a4c806da4c4df16266c02accdd6f` (versão `0.72.0`, publicada 2026-06-30, muito posterior à janela comprometida), fixado por **digest imutável verificado diretamente contra o Docker Hub oficial** — nunca uma tag flutuante, nunca a Action. Isso elimina a superfície de exposição específica do incidente (a Action e os binários/imagens comprometidos), mantendo a ferramenta em si.

## 3. Vulnerabilidade NuGet encontrada e corrigida neste bloco

A auditoria (`scripts/ci/nuget-dependency-audit.sh`) encontrou, antes de qualquer correção, `System.Text.Json 8.0.0` (transitivo, trazido pelos pacotes `AWSSDK.*`) vulnerável em `Ledger.OutboxPublisher`, `Consolidation.Worker` e `Architecture.Tests`:

- **CVE-2024-30105** (GHSA-hh2w-p6rv-4g7w, High) — corrigido em 8.0.4.
- **CVE-2024-43485** (GHSA-8g4q-xg66-9fp4, High) — corrigido em 8.0.5.

Tentativa inicial: fixar `System.Text.Json` centralmente via `Directory.Packages.props` (`PackageVersion` + `CentralPackageTransitivePinningEnabled`) — **causou conflito de resolução real** (`NU1107`, downgrade forçado de `AWSSDK.SecretsManager`/`AWSSDK.SQS` para versões antigas incompatíveis), confirmado por execução real do restore, não por suposição. Revertido.

Correção aplicada: `<PackageReference Include="System.Text.Json" VersionOverride="8.0.6" />` diretamente nos 3 projetos afetados (mecanismo oficial de override pontual do NuGet Central Package Management, confirmado via Context7/documentação oficial). Confirmado por execução real: `dotnet list package --vulnerable` retorna zero achados em todos os 20 projetos do `BancoCarrefour.sln` após a correção.

**Auditoria (verificação de consistência)**: `Ledger.Api` e `Consolidation.Api` (SDK `Microsoft.NET.Sdk.Web`) nunca apareceram na lista de vulneráveis, mesmo antes da correção — porque ambos usam `FrameworkReference` implícito para `Microsoft.AspNetCore.App`, cujo `System.Text.Json` compartilhado (parte do runtime patched da imagem `mcr.microsoft.com/dotnet/aspnet:8.0`) já satisfaz/sobrepõe a versão transitiva pedida pelos pacotes `AWSSDK.*`. Só `Ledger.OutboxPublisher`/`Consolidation.Worker` (SDK `Microsoft.NET.Sdk.Worker`, sem essa `FrameworkReference`) e `Architecture.Tests` precisavam do `VersionOverride` — confirmado reexecutando `dotnet list package --include-transitive` e `--vulnerable` no `BancoCarrefour.sln` completo: a correção não está presente só em projetos de teste enquanto um projeto de runtime permanece vulnerável.

## 4. Política de vulnerabilidade de imagem

Aplicada por `scripts/ci/scan-images.sh`, lida a partir do relatório JSON real do Trivy (nunca de um resumo textual). Veredito por imagem em `artifacts/vulnerability/<componente>.summary.json`:

| Severidade | Correção disponível | Coberto por exceção ativa | Ação |
|---|---|---|---|
| Critical/High | Sim | — (exceção nunca cobre achado com correção) | **Bloqueia** (`verdict: fail`) |
| Critical/High | Não | Sim | Reportado, não bloqueia (`verdict: pass_with_exceptions`) |
| Critical/High | Não | Não | **Bloqueia** (`verdict: fail`) — exige criar uma exceção documentada, nunca ignorar silenciosamente |
| Medium/Low | — | — | Reportado, nunca bloqueia |

Vereditos possíveis (auditoria — antes só existiam `pass`/`fail`, o que permitia um "pass" mesmo com achados severos silenciosamente aceitos por uma exceção; corrigido para tornar essa aceitação visível):

- **`pass`** — nenhum achado Critical/High.
- **`pass_with_exceptions`** — só achados Critical/High sem correção, todos cobertos por exceção ativa e não expirada.
- **`fail`** — achado bloqueante (com correção, sem correção e sem exceção, ou correção que passou a existir para um achado excecionado) ou exceção inválida/expirada/órfã.
- **`scan_error`** — o scanner falhou ao executar (banco de vulnerabilidades indisponível, erro de execução, relatório ausente/vazio) — **nunca** tratado como "scan limpo"; provado por teste isolado real em `scripts/ci/test-scanner-db-unavailable.sh` (cache vazio + rede desligada, nunca o cache real).

Se uma correção passa a existir para um CVE que tem uma exceção ativa, o achado volta a ser bloqueante até a exceção ser revisada/removida — a exceção nunca "esconde" um achado que já poderia ser corrigido.

### Resultado real

As 4 imagens (`ledger-api`, `ledger-outbox-publisher`, `consolidation-api`, `consolidation-worker`) foram escaneadas com sucesso a partir do HEAD limpo mais recente (ver seção 6, "SBOM e proveniência"). Nenhuma tem achado Critical/High **com correção disponível**. Os 22 achados únicos Critical/High sem correção disponível (herdados da imagem base Debian 12: `perl-base`, `util-linux` e derivados, `ncurses`, `zlib1g`, `gzip`, `libacl1`) estão todos cobertos pelas 6 exceções temporárias em `docs/security/excecoes-de-vulnerabilidade.json` (seção 5) — veredito `pass_with_exceptions` nas 4 imagens.

## 5. Registro de exceções

`docs/security/excecoes-de-vulnerabilidade.json` — **6 exceções ativas** (auditoria: a versão original deste documento tinha o arquivo vazio `[]` apesar de já existirem 22 achados únicos Critical/High sem correção, o que fazia o scan reportar "pass" sem nenhuma aceitação de risco visível — corrigido). Cada entrada cobre um pacote (ou família de pacotes do mesmo pacote-fonte Debian) e uma ou mais vulnerabilidades, com todos os campos abaixo obrigatórios (validado estruturalmente por `scan-images.sh`, `validate-supply-chain-artifacts.sh` e `WorkflowGovernanceArchitectureTests`):

```json
{
  "id": "EXC-SC-001",
  "vulnerabilityIds": ["CVE-2026-13221", "..."],
  "packages": ["perl-base"],
  "affectedImages": ["ledger-api", "ledger-outbox-publisher", "consolidation-api", "consolidation-worker"],
  "rationale": "por que o achado existe e por que não foi corrigido",
  "applicability": "análise de exploração real - o código do repositório aciona o caminho vulnerável?",
  "compensatingControls": ["controle 1", "controle 2"],
  "owner": "responsável",
  "created": "AAAA-MM-DD",
  "reviewDate": "AAAA-MM-DD",
  "expires": "AAAA-MM-DD",
  "status": "active",
  "remediationCondition": "quando revisar/remover esta exceção",
  "source": "URL do advisory/tracker"
}
```

As 6 exceções atuais (criadas em 2026-07-27, revisão em 2026-08-26, expiram em 2026-10-25 — nenhuma é permanente):

| ID | Pacote(s) | CVEs | Por que não bloqueia |
|---|---|---|---|
| EXC-SC-001 | `perl-base` | 8 CVEs | Interpretador Perl da imagem base, nunca invocado pela aplicação .NET |
| EXC-SC-002 | `util-linux` e derivados (`mount`, `bsdutils`, `libblkid1` etc.) | CVE-2026-53615 | Exigiria `CAP_SYS_ADMIN`, nunca concedida a estes containers |
| EXC-SC-003 | `ncurses`/`libtinfo6` | CVE-2025-69720 | Terminal interativo, processos .NET rodam headless |
| EXC-SC-004 | `zlib1g` | CVE-2023-45853 | Específico do utilitário `minizip`, não usado pelo `System.IO.Compression` do .NET |
| EXC-SC-005 | `gzip` | CVE-2026-41992 | Binário nunca invocado via shell-out pela aplicação |
| EXC-SC-006 | `libacl1` | CVE-2026-54369 | ACLs POSIX nunca manipuladas pelo código do repositório |

Uma exceção expirada, sem revisão em dia, sem dono, órfã (sem nenhum achado real correspondente) ou cujo achado passou a ter correção disponível **falha** `scripts/ci/scan-images.sh` e `scripts/ci/validate-supply-chain-artifacts.sh` — nunca é tratada como aprovada silenciosamente. Não há allowlist permanente nem uma exceção única cobrindo "todos os achados Debian".

## 6. SBOM e proveniência

Formato: CycloneDX JSON (`artifacts/sbom/<componente>.cyclonedx.json`), um por imagem implantável, gerado pelo mesmo Trivy. Cada SBOM inclui metadados de ferramenta/versão, identidade da imagem, e o inventário real de pacotes (SO Debian + `.deps.json` do .NET) com nome, versão, PURL e licença quando detectável. Rastreabilidade commit → imagem → SBOM: `artifacts/sbom/images.json` (manifesto gerado por `scripts/ci/build-images-for-supply-chain.sh`, finalizado por `scripts/ci/scan-images.sh` com os campos de scanner).

**Proveniência do build**: `scripts/ci/require-clean-source-tree.sh` bloqueia o build (`docker build`) se houver qualquer mudança rastreada não commitada (staged ou não), conflito de merge não resolvido, ou arquivo novo não rastreado e não ignorado — testado com 5 casos negativos e 2 controles positivos em `scripts/ci/test-provenance-guards.sh`. O manifesto usa a **SHA completa de 40 caracteres** (nunca abreviada) como `sourceCommit`, com um campo explícito `sourceTreeClean: true`, e `scripts/ci/validate-supply-chain-artifacts.sh` falha se `sourceCommit` não corresponder exatamente ao `git rev-parse HEAD` no momento da validação.

O manifesto final (por imagem) registra: `sourceCommit` (SHA completa), `sourceTreeClean`, `image`/`imageId`/`dockerfile`/`buildContext`/`targetPlatform`/`buildTimestamp`/`buildCommand`, `sbomPath`/`vulnerabilityReportPath`, e `scannerName`/`scannerVersion`/`scannerImageDigest`/`scannerDatabaseTimestamp` (estes últimos anexados por `scan-images.sh` após o scan, já que a versão do banco de vulnerabilidades só é conhecida depois de escanear).

SBOMs, relatórios de vulnerabilidade e o manifesto são gerados em `artifacts/` (gitignored) e publicados como artifact de workflow — **nunca committados** como estado do repositório.

## 7. Inventário de licenças

Extraído dos SBOMs (`scripts/ci/generate-license-inventory.sh`, sem ferramenta de licença dedicada). Resultado real: 2601 pares pacote/licença, 2408 com licença identificada, 193 `NOASSERTION`. Predominância de GPL-2.0/GPL-3.0/LGPL (pacotes de SO Debian herdados da imagem base) e MIT/BSD (ecossistema .NET/NuGet). **Nenhuma política de bloqueio por licença está ativa** — o inventário real precisa ser revisado por alguém com autoridade de decisão de licenciamento antes de qualquer política ser definida; classificar `NOASSERTION` como seguro por padrão seria incorreto e não foi feito.

**Auditoria (composição do `NOASSERTION`)**: os 193 pares `NOASSERTION` correspondem a 73 pacotes únicos (não 193 pacotes distintos). Amostragem por tipo de PURL mostra que 62 dos 73 são pacotes **NuGet** bem conhecidos e permissivamente licenciados (`Microsoft.EntityFrameworkCore` — MIT, `Npgsql` — PostgreSQL License, `AWSSDK.*`/`OpenTelemetry.*` — Apache-2.0, `Microsoft.Extensions.*`/`Microsoft.IdentityModel.*` — MIT) — o Trivy tem uma limitação conhecida de extração de licença para pacotes NuGet (não lê metadado de licença de `.deps.json`/`.nuspec` com a mesma confiabilidade que usa para pacotes Debian). Os restantes são pacotes de SO (`libcrypt1`, `libgcc-s1`, `libstdc++6`) e arquivos de manifesto (`*.deps.json`, `debian`) que o Trivy lista como "componente" mas não são unidades licenciáveis individuais. **Nenhuma dependência NuGet direta deste repositório foi encontrada com licença genuinamente desconhecida ou não permissiva.** Backlog de governança (não bloqueante): confirmar manualmente a licença dos 62 pacotes NuGet via metadado do NuGet.org antes de qualquer decisão de redistribuição binária — dono: Leandro Varga (mantenedor do repositório), revisão: 2026-08-26.

## 8. Revisão de dependências em pull request

`actions/dependency-review-action@a1d282b36b6f3519aa1f3fc636f609c47dddb294` (v5.0.0, SHA verificado contra o repositório oficial via API do GitHub). `fail-on-severity: high` — reprova o PR só por vulnerabilidade nova introduzida no diff (High ou superior); não reprova por vulnerabilidade já existente antes da mudança. Permissions mínimas (`contents: read`), sem secret de repositório. **Execução real em runner hospedado pendente** — só roda em contexto de `pull_request`, que exige o branch enviado ao remoto.

## 9. CodeQL

`github/codeql-action/init` e `.../analyze` (`f081e69141f6ea0aa740920b0c7012cd81a4fbe6`, v3.37.3 — SHA e existência de `init/action.yml`/`analyze/action.yml` confirmados via API REST do GitHub; o CDN `raw.githubusercontent.com` apresentou um 404 transitório de cache durante a verificação, mas a API de conteúdo — fonte igualmente oficial — confirmou o blob SHA e o tamanho do arquivo em ambos os casos). `build-mode: manual`. `security-events: write` escopado só a este job, nunca no nível do workflow. **Execução real em runner hospedado pendente.**

**Correção de modelo de build (auditoria)**: a versão original reaproveitava o build via `docker compose run dotnet-sdk dotnet build` (container-irmão). A documentação oficial do CodeQL exige que, quando o código é compilado dentro de um container, a análise rode **no mesmo container** — o tracer instrumentado pelo `init` (variáveis de ambiente injetadas no processo do runner) não é herdado por um processo delegado a um container-irmão via `docker compose run`, então a compilação ali nunca seria observada pelo CodeQL (build manual silenciosamente vazio, sem erro visível). Corrigido: o build agora roda nativamente no runner via `actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68` (v6.0.0, SHA verificado contra o repositório oficial), no mesmo processo onde o `init` configurou o tracer. Isso também garante que os 2 métodos com `[GeneratedRegex]` (source generator do C# em `Ledger.Application/RegisterFinancialEntryUseCase.cs` e `Consolidation.Application/ApplyFinancialEntryUseCase.cs`) sejam observados por uma compilação real — a alternativa `build-mode: none` é oficialmente suportada para C#, mas a própria documentação do GitHub alerta que produz resultado menos preciso quando "o repositório normalmente gera código durante o build", que é exatamente este caso.

**CodeQL Action v3 vs. v4**: o repositório oficial `github/codeql-action` lista v3 e v4 como **ambos atualmente suportados** (não há aviso de depreciação da linha v3 até o momento desta auditoria — confirmado via `README.md` oficial do repositório). Decisão: manter `v3.37.3` (já pinada, já validada pelos testes de governança) em vez de migrar para v4 só porque é mais nova — reavaliar quando a linha v3 entrar em depreciação documentada.

## 10. Atestação de artefatos — não implementada em `supply-chain.yml`

Este bloco **não** solicita `id-token: write`, `attestations: write` nem `artifact-metadata: write` em nenhum workflow — confirmado estruturalmente por `WorkflowGovernanceArchitectureTests`. Nenhuma atestação local foi gerada nem classificada como proveniência do GitHub, e isso permanece correto especificamente para `supply-chain.yml`.

**Nota (ADR-0013):** o fluxo abaixo está implementado - não em `supply-chain.yml`, mas em `.github/workflows/publish-images.yml` (o workflow de publicação real no Amazon ECR, único registry oficial, distinto deste conjunto de gates):

```
build da imagem → release-qualification (isolamento + smoke 50 RPS, sem rebuild) → publicação da imagem (Amazon ECR, via OIDC) → geração de SBOM → atestação de proveniência da imagem (actions/attest-build-provenance) → atestação do SBOM (actions/attest com sbom-path) → verificação antes do deploy (gh attestation verify, ação externa)
```

Ver ADR-0013 e `tests/Architecture.Tests/AttestationGovernanceArchitectureTests.cs` para o detalhamento completo (permissões mínimas, associação ao digest ECR remoto real, estados explícitos `not_applicable`/`pending_hosted_execution`/`generated`/`failed`/`verified` no manifesto de release, nunca uma atestação fabricada). A execução hospedada real (e portanto o estado `generated`/`verified` de fato ocorrendo) permanece pendente até o workflow ser disparado em um runner real.

## 11. Verificação de autenticidade do scanner (Trivy)

Pinagem por digest imutável protege contra movimentação de tag, mas não prova por si só que o digest pertence ao release oficial pretendido. Verificação real feita nesta auditoria:

- **Release oficial**: `api.github.com/repos/aquasecurity/trivy/releases/tags/v0.72.0` confirma um release real, não-prerelease, publicado em 2026-06-30, com assets assinados por `cosign sign-blob` (arquivos `.sigstore.json` para cada binário).
- **Digest cross-registry**: o digest pinado (`sha256:cffe3f5161a47a6823fbd23d985795b3ed72a4c806da4c4df16266c02accdd6f`) foi confirmado via API do registry (token + `Docker-Content-Digest`) como a resolução **exata** da tag `0.72.0` tanto em `docker.io/aquasec/trivy` quanto em `ghcr.io/aquasecurity/trivy` — dois registries operados independentemente, ambos sob a conta oficial da Aqua Security.
- **Limitação encontrada**: o pipeline de release do Trivy usa `cosign` para assinar as imagens Docker (`docker_signs` no `goreleaser.yml` oficial, com `id-token: write` para assinatura keyless), mas nenhuma tag de assinatura (`sha256-<digest>.sig`) foi encontrada no registry para este digest específico, em nenhum dos dois registries — apesar de tags de assinatura existirem para outros digests do mesmo repositório (confirmando que o mecanismo funciona, só não foi localizado para esta versão específica). Registrado honestamente como limitação, não ocultado; a correspondência de digest entre dois registries independentes foi considerada evidência suficiente para não bloquear, já que forjar essa correspondência exigiria comprometer as credenciais de publicação de ambos os registries simultaneamente.

## 12. Modelo de execução de testes no CI (Testcontainers)

**Diagnóstico**: `tests/Security.IntegrationTests` cria containers Keycloak/PostgreSQL ad-hoc via Testcontainers e acessa a porta publicada via `127.0.0.1` diretamente (`IdentityFixture.cs`). Quando o processo de teste roda dentro do container-irmão `dotnet-sdk` (padrão usado pelos demais gates de CI), esse `127.0.0.1` é isolado por namespace de rede do próprio container-irmão e nunca alcança o container do Keycloak — reproduzido localmente de forma determinística (101 de 105 testes falhavam por timeout de readiness nesse modo; os mesmos 74 testes passam 100% ao rodar nativamente no host, e o restante dos gates de CI, que usam `TESTCONTAINERS_HOST_OVERRIDE` corretamente, não têm esse problema).

Correção: `security-gate` (`ci.yml`) roda `Security.IntegrationTests` **nativamente no runner**, via `actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68` (v6.0.0), restaurando/buildando/testando como processo direto no runner (com acesso nativo ao daemon Docker do runner para o Testcontainers). Os demais gates (`fast-quality-gate`, `integration-gate`, `image-gate`) continuam via `docker compose run` — não foram alterados porque não apresentaram o defeito.

## 13. O que está implementado localmente, configurado no workflow e pendente de execução hospedada

| Item | Status |
|---|---|
| Auditoria NuGet (`nuget-dependency-audit.sh`) | Implementado e executado com sucesso localmente (0 achados em todos os 20 projetos após a correção) |
| Guarda de árvore limpa antes do build (`require-clean-source-tree.sh`) | Implementado e testado (5 negativos + 2 controles positivos em `test-provenance-guards.sh`) |
| SBOM das 4 imagens, com proveniência (SHA completa + `sourceTreeClean`) | Implementado e executado com sucesso localmente a partir de HEAD limpo |
| Scan de vulnerabilidade das 4 imagens | Implementado e executado com sucesso localmente (`pass_with_exceptions` nas 4 imagens) |
| Política de exceção temporária (6 exceções, expiração/revisão obrigatórias) | Implementado, validado estruturalmente e por teste (exceção expirada/órfã/campo ausente rejeitados) |
| Prova de falha do scanner (banco de vulnerabilidades indisponível) | Implementado e executado com sucesso (`test-scanner-db-unavailable.sh`, isolado, nunca toca o cache real) |
| Inventário de licenças | Implementado e executado com sucesso localmente |
| Validação de evidências (`validate-supply-chain-artifacts.sh`) | Implementado, testado com sucesso, com os caminhos de falha confirmados (SBOM ausente, JSON inválido, associação de imagem errada, SHA abreviada, HEAD divergente, `sourceTreeClean=false`, exceção expirada, `scan_error`) |
| `.github/dependabot.yml` (nuget/github-actions/docker/terraform) | Implementado, validado estruturalmente. Execução real (Dependabot Alerts/PRs) depende do GitHub habilitar o recurso no repositório - **não verificado** |
| `actions/dependency-review-action` | Configurado no workflow. Execução real em runner hospedado **pendente** (só dispara em PR real) |
| CodeQL (build nativo no runner) | Configurado no workflow, validado estruturalmente. Execução real em runner hospedado **pendente** |
| `security-gate` nativo no runner (`actions/setup-dotnet`) | Configurado no workflow, validado localmente (74/74 testes de `Security.IntegrationTests` passam nativamente). Execução real em runner hospedado **pendente** |
| Publicação de imagem, atestação, deploy AWS | Fora do escopo de `supply-chain.yml` - implementados em `publish-images.yml` (ADR-0013), validados estruturalmente. Disparo exclusivamente manual (`workflow_dispatch`, restrito a `main`) - nunca automático em push. Preflight (`check-release-prerequisites.sh`) bloqueia a execução antes do build e de qualquer autenticação AWS quando `AWS_REGION`/`ECR_PUBLISHER_ROLE_ARN`/`ECR_REGISTRY` estão ausentes (comportamento confirmado por uma execução hospedada real, quando o gatilho ainda era automático). Nenhuma imagem publicada, nenhuma atestação gerada, nenhum deploy AWS executado contra conta real. |

## 14. Relação com documentos

- [ADR-0013](../decisions/ADR-0013-integridade-de-release-e-software-supply-chain.md)
- [evidencias-do-case.md](../operations/evidencias-do-case.md)
- `.github/dependabot.yml`, `.github/workflows/supply-chain.yml`, `.github/workflows/ci.yml`
- `scripts/ci/nuget-dependency-audit.sh`, `require-clean-source-tree.sh`, `build-images-for-supply-chain.sh`, `generate-sboms.sh`, `generate-license-inventory.sh`, `scan-images.sh`, `validate-supply-chain-artifacts.sh`, `test-provenance-guards.sh`, `test-scanner-db-unavailable.sh`
