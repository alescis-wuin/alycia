# Repository Git hooks

This directory contains the Git hooks shared by the repository.

Install or refresh the hooks configuration with:

```bash
make hooks-install
```

Verify the worktree-local hooks configuration with:

```bash
make hooks-check
```

## Installed hooks

| Hook | Purpose |
| --- | --- |
| `pre-commit` | Runs the fast branch, staged-file, syntax, and formatting checks. |
| `commit-msg` | Enforces the repository commit message convention. |
| `post-commit` | Verifies the new signature and displays the commit and worktree state. |
| `pre-push` | Rejects protected or dirty pushes, then runs the complete quality gate. |

The pre-push hook rejects staged, unstaged, and untracked changes before
running the expensive quality gate. The same checks can be run explicitly:

```bash
make worktree-clean
make verify-push
```

Ignored files do not block pushes.

The installer enables Git worktree-specific configuration and stores the
absolute hooks path in the current worktree configuration. This avoids
changing the hooks path of other linked worktrees.

Local hooks remain secondary to GitHub rulesets and continuous integration
checks because they can be bypassed with `--no-verify`.

Git has no client-side `post-push` hook. GitHub verification after a successful
push is therefore implemented by the repository push command in a later
workflow step.
