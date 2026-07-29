# Governança do repositório

Este documento define regras permanentes de higiene para os artefatos deste
repositório. As regras valem para todas as branches, ciclos de evolução,
commits e Pull Requests futuros — não são específicas de um ciclo de trabalho.

## Princípios

- Somente artefatos oficiais da solução (código-fonte, testes, documentação,
  infraestrutura como código, configuração de CI/CD) são versionados neste
  repositório.
- Configurações pessoais de IDEs, assistentes de codificação e demais
  ferramentas de desenvolvimento permanecem locais a cada máquina ou sessão e
  nunca são versionadas.
- Instruções, memórias, caches, sessões e planos específicos de uma
  ferramenta não fazem parte do produto e não têm lugar neste repositório.
- Segredos, certificados privados, tokens, arquivos de estado de
  infraestrutura (por exemplo `*.tfstate*`), planos de execução (por exemplo
  `*.tfplan`) e demais saídas geradas localmente nunca são versionados.
- Todo conjunto de arquivos em staging deve ser revisado (`git status`,
  `git diff --cached`) antes de qualquer commit.
- Exceções a estas regras exigem decisão explícita e registrada do projeto —
  nunca são assumidas por conveniência individual.

## Aplicação

- O `.gitignore` na raiz do repositório mantém os padrões correspondentes a
  estas regras.
- `tests/Architecture.Tests/RepositoryHygieneTests.cs` valida de forma
  automatizada, a partir de `git ls-files`, que nenhum arquivo efetivamente
  rastreado pelo Git corresponda aos padrões proibidos.
- Esta regra é permanente e independe do ciclo de evolução em execução no
  momento.
