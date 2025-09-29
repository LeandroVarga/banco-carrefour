# Checklist de Requisitos — Onde Cada Item é Atendido

- Serviço de lançamentos: `POST /ledger/entries` (201/200 idempotente com Location estável)
  - Cobertura: Cenários A, B, F nos runners (`ops/test-suite.*`); coleção `ops/requests.http`
  - Rota via API Gateway (porta 8080)

- Serviço de saldo diário: `GET /balances/daily?date=YYYY-MM-DD`
  - Cobertura: Cenários A..I (antes/depois e pós-rebuild)
  - Apenas leitura; usado na micro-carga

- Não-funcionais (50 rps, ≤ 5% perda)
  - Scripts: `ops/load.sh` (bash), `ops/load.ps1` (pwsh)
  - Runners: `--load`/`-Load` executam ao final do smoke
  - CI: matriz Linux/Windows em `.github/workflows/test.yml`

- Resiliência/isolamento
  - Ledger aceita POST mesmo se consolidator ficar indisponível (outbox + fila); rebuild replace-only e idempotente
  - Cobertura: runners (rebuild invariance Cenários D, I)

- Observabilidade
  - `/actuator/health` e `/actuator/prometheus` expostos; dashboards provisionados
  - Runners aguardam health e coletam logs em falha

- Segurança
  - API key obrigatória em POST via gateway (`X-API-Key`, default `admin`)
  - Cobertura: Cenário F (403)

- Documentação/decisões
  - ADRs em `docs/adr/*.md` (se aplicável)
  - Como rodar/testar: `README.md` + `docs/TESTES.md`
  - Scripts cruzados: `ops/test-suite.sh` (Linux/macOS), `ops/test-suite.ps1` (Windows)

