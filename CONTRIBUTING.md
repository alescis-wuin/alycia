# Contributing

## Branches

Normal work starts from `develop` and uses one of the approved work-branch forms, for example:

```text
feature/provider-abstraction
fix/composer-focus
refactor/chat-state
```

Direct feature work on `develop`, `testing`, and `main` is prohibited.

## Commits

Every local commit must be GPG-signed and follow the repository commit-message policy enforced by `.githooks/commit-msg`.

## Quality gate

Before publishing a work-branch checkpoint:

```bash
make verify-push
```

The push itself is always performed manually by the repository owner.
