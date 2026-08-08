# Project profile

```text
PROJECT_NAME: Alicia
REPOSITORY_FULL_NAME: pending owner decision
REPOSITORY_ROOT_NAME: alicia
PRIMARY_LANGUAGE: C#
FRAMEWORKS: .NET 10; Avalonia 12
PACKAGE_MANAGER: NuGet with Central Package Management
SOLUTION_OR_WORKSPACE_FILE: Alicia.slnx
DOTNET_SDK_REQUIRED: true
DOTNET_SDK_VERSION: 10.0.110
DOTNET_ROOT_DIRECTORY: .dotnet
DOTNET_BOOTSTRAP_COMMAND: make toolchain-bootstrap
DOTNET_TEST_ENVIRONMENT_COMMAND: make test
BASE_DEVELOPMENT_BRANCH: develop
TESTING_BRANCH: testing
PRODUCTION_BRANCH: main
DEFAULT_REMOTE: origin
DEFAULT_BASE_REF: origin/develop
WORK_BRANCH_REMOTE_CHECKPOINT_POLICY: push-after-each-validated-commit
USER_SHELL: Bash-compatible repository tooling
SUPPORTED_OPERATING_SYSTEMS: Windows; Linux; macOS (desktop host)
BUILD_COMMAND: make build
STATIC_ANALYSIS_COMMAND: dotnet build with warnings as errors; make format-check
UNIT_TEST_COMMAND: make test
INTEGRATION_TEST_COMMAND: pending feature requiring external boundary
E2E_TEST_COMMAND: pending interactive workflow implementation
FORMAT_CHECK_COMMAND: make format-check
DEPENDENCY_AUDIT_COMMAND: make audit
SECURITY_AUDIT_COMMAND: make audit
PACKAGE_COMPONENT_SYNTAX_COMMANDS: make syntax; make lint
DEPENDENCY_GRAPH_COMMAND: make dependency-graph
PATCH_REHEARSAL_COMMAND: make patch-validate PATCH_DIR=<package-directory>
RUN_COMMAND: make run
GPG_SIGNING_REQUIRED: true
MERGE_SIGNATURE_MODE: pending owner decision
REQUIRED_GITHUB_CHECKS: CI - Develop; CI - Testing; CI - Main
DEPLOYMENT_POLICY: pending release design
```

## Open owner decisions

- GitHub repository full name and remote URL;
- strict local-signature vs GitHub-merge compatibility mode;
- license;
- release numbering and tag naming;
- coverage threshold;
- future mobile/browser host priority;
- final localization source language and supported locales.
