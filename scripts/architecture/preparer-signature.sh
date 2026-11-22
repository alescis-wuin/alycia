#!/usr/bin/env bash
# Rejoue chaque commit de la PR sur une nouvelle branche locale avec l’identité Git configurée.
set -euo pipefail
if [[ $# != 4 && $# != 6 ]]; then
    echo "Usage: bash preparer-signature.sh BASE_TESTEE SHA_DE_LA_PR ARBRE_TESTE CHEMIN_DU_NOUVEAU_WORKTREE [BRANCHE_BASE BRANCHE_PR]" >&2
    exit 2
fi
expected_base=$1
tested=$2
tree=$3
destination=$4
[[ $expected_base =~ ^[0-9a-f]{40}$ && $tested =~ ^[0-9a-f]{40}$ && $tree =~ ^[0-9a-f]{40}$ ]] || { echo "Trois SHA Git complets sont requis." >&2; exit 2; }
candidate=${5:-alycia-decouplage-modulaire-20261121}
preview=${6:-refactor/alycia-finition-decouplage-20261121}
git check-ref-format --branch "$candidate" >/dev/null
git check-ref-format --branch "$preview" >/dev/null
git fetch --no-tags origin "refs/heads/$candidate:refs/remotes/origin/$candidate" "refs/heads/$preview:refs/remotes/origin/$preview"
base=$(git rev-parse "refs/remotes/origin/$candidate")
tip=$(git rev-parse "refs/remotes/origin/$preview")
[[ $base == "$expected_base" && $tip == "$tested" && $(git rev-parse "$tested^{tree}") == "$tree" ]] || { echo "La base, la PR ou son arbre a changé depuis la recette. Vérifier la nouvelle livraison." >&2; exit 1; }
git merge-base --is-ancestor "$base" "$tip"
[[ -z $(git rev-list --merges "$base..$tip") ]] || { echo "L'historique contient une fusion. Examiner ses parents avant de signer." >&2; exit 1; }
branch="integration/alycia-signee-${tested:0:12}"
git worktree add -b "$branch" "$destination" "$base"
cd "$destination"
while read -r original; do
    git cherry-pick --allow-empty -S "$original"
    [[ $(git rev-parse 'HEAD^{tree}') == "$(git rev-parse "$original^{tree}")" ]] || { echo "Arbre différent pour $original." >&2; exit 1; }
done < <(git rev-list --reverse "$base..$tip")
[[ $(git rev-parse 'HEAD^{tree}') == "$tree" ]]
git diff --exit-code "$tested" HEAD
printf 'Arbre vérifié %s\nBranche signée locale %s\n' "$tree" "$branch"
