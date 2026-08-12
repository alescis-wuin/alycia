# Security Policy

## Reporting

Do not disclose credentials, API keys, access tokens, private conversation data, or exploit details in public issues.

## Repository rules

- Secrets must remain outside Git.
- Provider credentials will be loaded through dedicated secure configuration boundaries.
- Logs must not contain authorization headers, bearer tokens, or full secret values.
- Dependency vulnerability audits are part of `make audit` and `make verify`.
- AI provider network access is isolated behind application ports and explicit infrastructure adapters.
- The managed llama.cpp server binds to loopback only.
- Hugging Face credentials are inherited from `HF_TOKEN` when supplied by the environment; Alicia does not persist that token or place it in the llama-server command line.
- Global provider configuration stores provider/model/runtime/generation values only; credentials and tokens are intentionally excluded from its schema.
- Provider fallback is disabled: Alicia does not silently route a conversation to a different provider when the saved selection is unavailable.
- Managed provider installation never invokes `sudo` or a distribution package manager.
