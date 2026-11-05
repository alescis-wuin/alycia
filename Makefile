.PHONY: build up status down format lint test check install-hooks

build:
	dotnet build Alycia.slnx

up:
	docker compose up --build -d

status:
	docker compose ps

down:
	docker compose down

format:
	bash scripts/quality.sh format

lint:
	bash scripts/quality.sh lint

install-hooks:
	bash scripts/quality.sh install-hooks

test:
	dotnet test Alycia.slnx

check: lint test

.PHONY: migrate
migrate:
	bash scripts/migrate.sh
