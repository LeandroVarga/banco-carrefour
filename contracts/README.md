# Contratos

Esta pasta contém os contratos externos e assíncronos da solução.

Documentos:

- `openapi.yaml` — contrato HTTP inicial das APIs de Lançamentos e Consolidado.
- `events/entry-created-v1.schema.json` — contrato JSON Schema do evento `EntryCreated.v1`.

Os contratos materializam decisões descritas em `docs/architecture/08-implementation-readiness.md`.

## Escopo inicial

Contratos HTTP:

```text
POST /entries
GET /daily-balances/{businessDate}
```

Contrato assíncrono:

```text
EntryCreated.v1
```

## Observações

Os contratos ainda estão em rascunho até a implementação e os testes automatizados confirmarem payloads, códigos de erro e exemplos.
