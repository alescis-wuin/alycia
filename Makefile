.PHONY: build up status down format lint test check install-hooks

# GNU Make is optional on Windows; every demo target has a PowerShell equivalent.
ifeq ($(OS),Windows_NT)
PYTHON ?= py -3
POWERSHELL ?= powershell.exe
PS_SCRIPT = $(POWERSHELL) -NoLogo -NoProfile -ExecutionPolicy Bypass -File
QUALITY = $(PS_SCRIPT) scripts/quality.ps1 -Mode
MIGRATE = $(PS_SCRIPT) scripts/migrate.ps1
DEMO_API = $(PS_SCRIPT) scripts/demo.ps1 -Action Api
DEMO_CLIENT = $(PS_SCRIPT) scripts/demo.ps1 -Action Client
else
PYTHON ?= python3
QUALITY = bash scripts/quality.sh
MIGRATE = bash scripts/migrate.sh
DEMO_API = bash scripts/demo.sh
DEMO_CLIENT = ALYCIA_API_BASE_URL=http://127.0.0.1:5187 dotnet run --project src/Alycia.Client.Avalonia
POWERSHELL ?= pwsh
PS_SCRIPT = $(POWERSHELL) -NoLogo -NoProfile -ExecutionPolicy Bypass -File
endif

build: bootstrap-packages
	dotnet build Alycia.slnx

up:
	docker compose up --build -d

status:
	docker compose ps

down:
	docker compose down

format:
	$(QUALITY) format

lint:
	$(QUALITY) lint

install-hooks:
	$(QUALITY) install-hooks

test: bootstrap-packages
	dotnet test Alycia.slnx

check: lint test

.PHONY: migrate
migrate:
	$(MIGRATE)

.PHONY: demo-api demo-client
demo-api:
	$(DEMO_API)

demo-client:
	$(DEMO_CLIENT)

.PHONY: demo-windows demo-windows-stop demo-windows-status
demo-windows:
	$(PS_SCRIPT) scripts/demo.ps1 -Action All -InstallPrerequisites

demo-windows-stop:
	$(PS_SCRIPT) scripts/demo.ps1 -Action Stop

demo-windows-status:
	$(PS_SCRIPT) scripts/demo.ps1 -Action Status

.PHONY: bootstrap-packages bootstrap-local-feed build-module test-module check-module check-extraction check-master
bootstrap-packages bootstrap-local-feed:
	$(PYTHON) scripts/modules.py bootstrap-packages

build-module test-module check-module:
	$(PYTHON) scripts/modules.py $@ $(MODULE)

check-extraction: bootstrap-packages
	$(PYTHON) scripts/modules.py check-extraction

check-master:
	$(PYTHON) scripts/modules.py check-master
