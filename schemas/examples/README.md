# Exemplos de manifesto de release (NÃO-PRODUTIVOS)

`release-manifest.example.json` ilustra a forma de
[`schemas/release-manifest.schema.json`](../release-manifest.schema.json)
(versão 4.0.0, alvo Amazon ECR/AWS real - ver ADR-0013) e é validado por
`tests/Architecture.Tests/ReleaseManifestGovernanceArchitectureTests.cs`
para garantir que nunca fica dessincronizado do schema real.

**Este arquivo nunca representa uma release real.** Todo commit, digest,
run-id e URL nele é um valor sintaticamente válido porém deliberadamente
fictício (zeros/incrementais). Um manifesto de release real só é
produzido por `scripts/ci/generate-release-manifest.sh` +
`scripts/ci/publish-validated-images.sh`, dentro de uma execução hospedada
real de `.github/workflows/publish-images.yml` (publicação no Amazon ECR),
nunca escrito à mão neste repositório.
