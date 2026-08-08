SHELL := /usr/bin/bash
.SHELLFLAGS := -Eeuo pipefail -c
.DEFAULT_GOAL := help
.ONESHELL:

ROOT := $(abspath $(dir $(lastword $(MAKEFILE_LIST))))
SOLUTION := $(ROOT)/Alicia.slnx
DESKTOP_PROJECT := $(ROOT)/src/Alicia.Desktop/Alicia.Desktop.csproj
ARCHITECTURE_TEST_PROJECT := $(ROOT)/tests/Alicia.Architecture.Tests/Alicia.Architecture.Tests.csproj
COMMON_SCRIPT := $(ROOT)/scripts/lib/common.sh
HOOK_INSTALLER := $(ROOT)/scripts/hooks/install.sh
CHECKS_DIRECTORY := $(ROOT)/scripts/checks
PATCH_RUNNER := $(ROOT)/scripts/patch/apply-package.sh
PATCH_TOOL := $(ROOT)/scripts/patch/patch_tool.py
DOTNET := $(ROOT)/scripts/toolchain/dotnet.sh
TOOLCHAIN_BOOTSTRAP := $(ROOT)/scripts/toolchain/bootstrap-dotnet.sh
TOOLCHAIN_RESOLVER := $(ROOT)/scripts/toolchain/resolve-dotnet.sh
TOOLCHAIN_VERIFY := $(ROOT)/scripts/toolchain/verify-dotnet.sh
TOOLCHAIN_TESTS := $(ROOT)/scripts/toolchain/tests/run.sh
TOOLCHAIN_INTEGRATION_TESTS := $(ROOT)/scripts/toolchain/tests/integration.sh

CONFIGURATION ?= Debug
BASE_REF ?= origin/develop
PATCH ?=
PATCH_DOWNLOADS_DIR ?= $(HOME)/Téléchargements
PATCH_DIR ?=
PATCH_OUTPUT ?=
TOOLCHAIN_ARCHIVE ?=
TOOLCHAIN_SHA256 ?=
TOOLCHAIN_OFFLINE_ONLY ?= 0
TOOLCHAIN_FORCE ?= 0

.PHONY: help doctor toolchain-bootstrap toolchain-check toolchain-info toolchain-clean toolchain-self-test  hooks-install hooks-check clean restore build rebuild run test architecture dependency-graph status git-check  branch-check worktree-clean staged syntax format format-check lint audit signatures linear-history  bootstrap-verify verify-fast verify verify-push patch patch-validate patch-pack patch-self-test  worktree-clean-self-test

help: ## Show the available commands
	@printf "\nAvailable commands:\n\n"
	@awk 'BEGIN { FS = ":.*## " } /^[a-zA-Z0-9_.-]+:.*## / { printf "  %-22s %s\\n", $$1, $$2 }' $(MAKEFILE_LIST)
	@printf "\n"

doctor: ## Audit the local development environment
	@source "$(COMMON_SCRIPT)"
	section "Development environment"
	for command_name in bash git make python3 gpg; do require_command "$$command_name"; done
	require_file "$(DOTNET)"
	require_file "$(SOLUTION)"
	"$(TOOLCHAIN_VERIFY)"
	info "Repository: $$(git rev-parse --show-toplevel)"
	info "Branch: $$(git branch --show-current)"
	info ".NET SDK: $$($(DOTNET) --version)"
	info ".NET source: $$($(TOOLCHAIN_RESOLVER) --source)"
	info "Commit signing: $$(git config --get commit.gpgsign || printf 'not configured')"
	info "Signing key: $$(git config --get user.signingkey || printf 'not configured')"
	info "Hooks path: $$(git config --get core.hooksPath || printf 'not configured')"
	success "Development environment audit completed."

toolchain-bootstrap: ## Install the pinned SDK into .dotnet when required
	@arguments=()
	if [[ -n "$(TOOLCHAIN_ARCHIVE)" ]]; then arguments+=(--archive "$(TOOLCHAIN_ARCHIVE)"); fi
	if [[ -n "$(TOOLCHAIN_SHA256)" ]]; then arguments+=(--sha256 "$(TOOLCHAIN_SHA256)"); fi
	if [[ "$(TOOLCHAIN_OFFLINE_ONLY)" == '1' ]]; then arguments+=(--offline-only); fi
	if [[ "$(TOOLCHAIN_FORCE)" == '1' ]]; then arguments+=(--force); fi
	"$(TOOLCHAIN_BOOTSTRAP)" "$${arguments[@]}"

toolchain-check: ## Verify the SDK selected for this repository
	@"$(TOOLCHAIN_VERIFY)"

toolchain-info: ## Display SDK resolution and complete host information
	@"$(TOOLCHAIN_RESOLVER)" --json
	@"$(DOTNET)" --info

toolchain-clean: ## Remove only the repository-local SDK installation
	@rm -rf -- "$(ROOT)/.dotnet" "$(ROOT)/.dotnet.bootstrap.lock"

toolchain-self-test: ## Test SDK resolution, bootstrap, and project integration
	@"$(TOOLCHAIN_TESTS)"
	@"$(TOOLCHAIN_INTEGRATION_TESTS)"

hooks-install: ## Install repository-managed Git hooks
	@"$(HOOK_INSTALLER)" install

hooks-check: ## Verify repository-managed Git hooks
	@"$(HOOK_INSTALLER)" check

clean: toolchain-check ## Remove .NET build outputs
	@"$(DOTNET)" clean "$(SOLUTION)" --configuration "$(CONFIGURATION)"

restore: toolchain-check ## Restore NuGet dependencies
	@"$(DOTNET)" restore "$(SOLUTION)"

build: restore ## Build the complete solution
	@"$(DOTNET)" build "$(SOLUTION)" --configuration "$(CONFIGURATION)" --no-restore

rebuild: clean build ## Clean and rebuild the complete solution

run: build ## Build and run the desktop Avalonia host
	@"$(DOTNET)" run --project "$(DESKTOP_PROJECT)" --configuration "$(CONFIGURATION)" --no-build

test: build ## Run all automated tests
	@"$(DOTNET)" test --solution "$(SOLUTION)" --configuration "$(CONFIGURATION)" --no-build --no-restore

architecture: build ## Run architecture boundary tests
	@"$(DOTNET)" test --project "$(ARCHITECTURE_TEST_PROJECT)" --configuration "$(CONFIGURATION)" --no-build --no-restore

dependency-graph: ## Validate the internal project dependency graph
	@python3 "$(CHECKS_DIRECTORY)/dependency-graph.py"

status: ## Display concise Git status
	@git status --short

git-check: ## Inspect staged changes and repository status
	@git diff --cached --check
	@git --no-pager diff --cached --stat
	@git status --short

branch-check: ## Enforce work branch naming and protection rules
	@"$(CHECKS_DIRECTORY)/branch-policy.sh"

worktree-clean: ## Reject pending staged, unstaged, or untracked changes
	@"$(CHECKS_DIRECTORY)/worktree-clean.sh"

staged: ## Validate staged file safety
	@"$(CHECKS_DIRECTORY)/staged-files.sh"

syntax: ## Validate Bash, Python, JSON, XML, and Makefile syntax
	@"$(CHECKS_DIRECTORY)/syntax.sh"

format: restore ## Apply .NET formatting and analyzer fixes
	@"$(DOTNET)" format "$(SOLUTION)" --no-restore

format-check: restore ## Verify .NET formatting without modifying files
	@"$(DOTNET)" format "$(SOLUTION)" --verify-no-changes --no-restore

lint: ## Run ShellCheck on repository shell files
	@"$(CHECKS_DIRECTORY)/lint.sh"

audit: ## Audit direct and transitive NuGet vulnerabilities
	@"$(CHECKS_DIRECTORY)/vulnerabilities.sh"

signatures: ## Verify signatures of branch commits
	@BASE_REF="$(BASE_REF)" "$(CHECKS_DIRECTORY)/signatures.sh"

linear-history: ## Reject merge commits introduced by the work branch
	@BASE_REF="$(BASE_REF)" "$(CHECKS_DIRECTORY)/linear-history.sh"

patch: ## Safely apply, validate, test, stage, and commit a patch package
	@test -n "$(PATCH)" || { echo 'PATCH is required.' >&2; exit 2; }
	@runtime_runner="$(PATCH_RUNNER).runtime.$$$$.sh"
	trap 'rm -f -- "$$runtime_runner"' EXIT
	cp -- "$(PATCH_RUNNER)" "$$runtime_runner"
	chmod 700 "$$runtime_runner"
	PATCH="$(PATCH)" PATCH_DOWNLOADS_DIR="$(PATCH_DOWNLOADS_DIR)" "$$runtime_runner"

patch-validate: ## Validate an unpacked patch package directory
	@test -n "$(PATCH_DIR)" || { echo 'PATCH_DIR is required.' >&2; exit 2; }
	@python3 "$(PATCH_TOOL)" validate-dir --package-dir "$(PATCH_DIR)"

patch-pack: ## Generate checksums and create a patch ZIP archive
	@test -n "$(PATCH_DIR)" || { echo 'PATCH_DIR is required.' >&2; exit 2; }
	@if [[ -n "$(PATCH_OUTPUT)" ]]; then 		python3 "$(PATCH_TOOL)" pack --package-dir "$(PATCH_DIR)" --output "$(PATCH_OUTPUT)"; 	else 		python3 "$(PATCH_TOOL)" pack --package-dir "$(PATCH_DIR)"; 	fi

patch-self-test: ## Run patch workflow unit tests
	@PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s "$(ROOT)/scripts/patch/tests" -p 'test_*.py' -v

worktree-clean-self-test: ## Test clean and dirty worktree detection
	@"$(CHECKS_DIRECTORY)/tests/worktree-clean-test.sh"

bootstrap-verify: ## Validate the repository before its initial signed commit
	@$(MAKE) --no-print-directory toolchain-check restore build test architecture dependency-graph syntax lint format-check audit toolchain-self-test worktree-clean-self-test

verify-fast: ## Run fast checks suitable before a commit
	@$(MAKE) --no-print-directory branch-check staged syntax toolchain-check dependency-graph format-check

verify: ## Run the complete local quality gate
	@$(MAKE) --no-print-directory branch-check toolchain-check clean restore build test architecture dependency-graph syntax lint format-check audit toolchain-self-test worktree-clean-self-test signatures linear-history

verify-push: ## Require a clean repository before the complete quality gate
	@$(MAKE) --no-print-directory worktree-clean
	@$(MAKE) --no-print-directory verify
