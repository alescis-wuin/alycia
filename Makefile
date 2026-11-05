.PHONY: build up status down

build:
	dotnet build Alycia.slnx

up:
	docker compose up --build -d

status:
	docker compose ps

down:
	docker compose down
