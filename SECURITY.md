# Security Policy

## Reporting

Do not disclose credentials, API keys, access tokens, private conversation data, or exploit details in public issues.

## Repository rules

- Secrets must remain outside Git.
- Provider credentials will be loaded through dedicated secure configuration boundaries.
- Logs must not contain authorization headers, bearer tokens, or full secret values.
- Dependency vulnerability audits are part of `make audit` and `make verify`.
- AI provider network access will be introduced behind application ports and explicit infrastructure adapters.
