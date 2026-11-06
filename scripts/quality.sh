#!/usr/bin/env bash
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

mode="${1:-}"

case "$mode" in
    format|lint|install-hooks) ;;
    *)
        echo "Usage: $0 {format|lint|install-hooks}" >&2
        exit 2
        ;;
esac

if [[ "$mode" == "install-hooks" ]]; then
    if [[ ! -x .githooks/pre-commit ]]; then
        echo "The versioned pre-commit hook is not executable." >&2
        exit 1
    fi

    if current=$(git config --get core.hooksPath); then
        if [[ "$current" == ".githooks" ]]; then
            echo "Git hooks are already installed."
            exit 0
        fi

        echo "Existing core.hooksPath: $current" >&2
        echo "Installation refused to preserve existing hooks." >&2
        exit 1
    fi

    hook_dir=$(git rev-parse --git-path hooks)

    for hook in "$hook_dir"/*; do
        [[ -e "$hook" || -L "$hook" ]] || continue
        [[ "$hook" == *.sample ]] && continue

        echo "Existing hook detected: $hook" >&2
        echo "Installation refused." >&2
        exit 1
    done

    git config --local core.hooksPath .githooks
    echo "Repository-local Git hooks installed."
    exit 0
fi

text_files=()
cs_files=()
declare -A seen=()

collect_file() {
    local file="$1"

    [[ -f "$file" ]] || return 0
    [[ -z "${seen[$file]+x}" ]] || return 0

    case "$file" in
        *.cs|*.csproj|*.slnx|*.json|*.md|\
        *.yml|*.yaml|*.axaml|*.xml|*.sh|*.ps1|\
        Makefile|.editorconfig|.gitignore|\
        .githooks/pre-commit) ;;
        *) return 0 ;;
    esac

    # Do not process likely binary or empty files.
    LC_ALL=C grep -Iq . -- "$file" || return 0

    seen["$file"]=1
    text_files+=("$file")

    if [[ "$file" == *.cs ]]; then
        cs_files+=("$file")
    fi
}

# Modified tracked files and non-ignored untracked files.
while IFS= read -r -d '' file; do
    collect_file "$file"
done < <(git ls-files --modified --others --exclude-standard -z)

# Lint must also examine files already staged.
if [[ "$mode" == "lint" ]]; then
    while IFS= read -r -d '' file; do
        collect_file "$file"
    done < <(git diff --cached --name-only --diff-filter=ACM -z)
fi

if [[ "$mode" == "format" ]]; then
    if (( ${#cs_files[@]} > 0 )); then
        dotnet format whitespace Alycia.slnx \
            --no-restore \
            --include "${cs_files[@]}"
    fi

    for file in "${text_files[@]}"; do
        if LC_ALL=C grep -Eq '[[:blank:]]+$' -- "$file"; then
            LC_ALL=C sed -i 's/[[:blank:]]\+$//' "$file"
        fi

        if [[ -s "$file" ]] &&
           [[ $(tail -c 1 "$file" | wc -l) -eq 0 ]]; then
            printf '\n' >> "$file"
        fi
    done

    echo "Formatting completed. Review git diff before staging."
    exit 0
fi

failed=0

git diff --check || failed=1
git diff --cached --check || failed=1

for file in "${text_files[@]}"; do
    if LC_ALL=C grep -Eq '[[:blank:]]+$' -- "$file"; then
        echo "Trailing whitespace: $file" >&2
        failed=1
    fi

    if [[ -s "$file" ]] &&
       [[ $(tail -c 1 "$file" | wc -l) -eq 0 ]]; then
        echo "Missing final newline: $file" >&2
        failed=1
    fi
done

if (( ${#cs_files[@]} > 0 )); then
    dotnet format whitespace Alycia.slnx \
        --no-restore \
        --verify-no-changes \
        --include "${cs_files[@]}" || failed=1
fi

if (( failed != 0 )); then
    echo "Lint failed. Run make format and review changes." >&2
    exit 1
fi

echo "Lint passed."
