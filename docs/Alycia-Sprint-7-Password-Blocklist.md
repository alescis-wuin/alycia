# Sprint 7 — Local Owner Password Blocklist

The embedded resource `Resources/blocked-owner-passwords.txt` in the Identity Infrastructure
project is a deliberately small, auditable offline baseline of commonly attempted weak
passwords and passphrases. It is not a complete compromised-password corpus; rejection
of one entry is not proof that it appeared in a breach, and absence from the list is
not evidence that a password is uncompromised.

The server validates the entire NFC-normalized password against the local entries,
using ordinal case-insensitive equality. It does **not** trim or truncate passwords;
spaces in either the input or list entries are significant. No remote query occurs.
The list is packaged as an embedded resource so a missing resource fails closed.

This is an initial S7 baseline, not a substitute for the 15–128 Unicode-code-point
length rule or for safe password hashing. Any later replacement with a larger,
verified local compromised-password corpus requires a separate sourced change and
focused tests. The resource must never be logged with user input or credentials.
