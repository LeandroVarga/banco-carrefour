SHELL := /bin/sh

.PHONY: up down logs clean test smoke load

up:
	docker compose up -d --build

down:
	docker compose down -v

logs:
	@mkdir -p out
	docker compose logs --no-color > out/compose-logs.txt || true
	@echo "Logs written to out/compose-logs.txt"

clean:
	find . -name target -type d -prune -exec rm -rf {} +
	rm -rf out

test:
	mvn -q -T 1C clean verify

smoke:
	@if command -v pwsh >/dev/null 2>&1; then pwsh -File ops/smoke.ps1; \
	elif command -v powershell >/dev/null 2>&1; then powershell -File ops/smoke.ps1; \
	elif command -v bash >/dev/null 2>&1; then bash ops/smoke.sh; \
	else echo "No shell for smoke test (need pwsh/powershell/bash)"; fi

load:
	@if command -v pwsh >/dev/null 2>&1; then pwsh -File ops/load.ps1; \
	elif command -v powershell >/dev/null 2>&1; then powershell -File ops/load.ps1; \
	elif command -v bash >/dev/null 2>&1; then bash ops/load.sh; \
	else echo "No shell for load script (need pwsh/powershell/bash)"; fi

.PHONY: e2e it
e2e:
	@if command -v pwsh >/dev/null 2>&1; then pwsh -File ops/test-suite.ps1; \
	elif command -v powershell >/dev/null 2>&1; then powershell -File ops/test-suite.ps1; \
	elif command -v bash >/dev/null 2>&1; then bash ops/test-suite.sh; \
	else echo "No shell for test suite (need pwsh/powershell/bash)"; fi

it:
	docker compose --profile tester run --rm tester

.PHONY: secrets.dev
secrets.dev:
	@mkdir -p secrets
	@set -a; [ -f .env ] && . ./.env || true; set +a; \
	for v in API_KEY SPRING_DATASOURCE_USERNAME SPRING_DATASOURCE_PASSWORD SPRING_RABBITMQ_USERNAME SPRING_RABBITMQ_PASSWORD; do \
	  eval val="\$$v"; \
	  if [ -n "$$val" ]; then printf "%s" "$$val" > "secrets/$$v"; echo "wrote secrets/$$v"; else echo "skipped $$v (unset)"; fi; \
	done
