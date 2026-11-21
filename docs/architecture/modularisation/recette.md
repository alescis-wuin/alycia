# Recette finale

## Évaluation hors ligne

Deux exécutions du même programme de benchmark et des mêmes fixtures sous .NET 10 ont été comparées. Les huit variantes du corpus principal et les deux du corpus de stress représentent 92 lignes ordonnées. La comparaison inclut rangs, chemins, facettes, jetons, matrices, références et verdicts. Les durées et allocations brutes dépendent du processus.

| Corpus / variante | Cas | Rappel@3, nDCG@3, MRR avant → après | Jetons de contexte avant → après | p50 avant → après (ms) | p95 avant → après (ms) |
|---|---:|---|---:|---:|---:|
| Principal, reference | 10 | 1/1/1 → 1/1/1 | 3385 → 3385 | 0.319 → 0.227 | 44.672 → 42.683 |
| Principal, combined | 10 | 1/1/1 → 1/1/1 | 2092 → 2092 | 0.735 → 0.841 | 74.558 → 67.844 |
| Sources voisines, reference | 6 | 1/1/1 → 1/1/1 | 7338 → 7338 | 0.357 → 0.434 | 50.452 → 54.764 |
| Sources voisines, combined | 6 | 1/1/1 → 1/1/1 | 3293 → 3293 | 1.083 → 1.011 | 105.101 → 104.683 |

Classification 18/20, relations 20/20, affirmations 16/16. Aucun seuil du benchmark en échec. L’empreinte SHA-256 de la totalité du JSON canonique, hors mesures de ressources, est identique pour les deux révisions : `0811f60b9dd1b5f42b9249e9876172c3a6b716f311fcc403da21e0f5407a4990`.

### Reproduire les deux mesures

Depuis le checkout candidat, avec le SDK .NET 10 et Python 3. Le benchmark est un projet distinct de la solution. Les commandes Bash utilisent un nouveau répertoire de sortie.

```bash
out=$(mktemp -d)
git worktree add --detach "$out/base" 309d610425786e668782769f5b7650902d11d37b
(cd "$out/base" && dotnet build benchmarks/Alycia.ContextBenchmarks && python3 scripts/evaluate-context.py offline --pipeline-v2 --output "$out/base.json")
dotnet build benchmarks/Alycia.ContextBenchmarks
python3 scripts/evaluate-context.py offline --pipeline-v2 --output "$out/candidat.json"
python3 - "$out/base.json" "$out/candidat.json" <<'PY'
import hashlib, json, sys
def stable(value):
    if isinstance(value, dict):
        return {k: stable(v) for k, v in value.items() if k not in {'p50Ms', 'p95Ms', 'milliseconds', 'allocatedBytes'}}
    if isinstance(value, list):
        return [stable(v) for v in value]
    return value
def fingerprint(path):
    with open(path, encoding='utf-8') as source:
        canonical = json.dumps(stable(json.load(source)), sort_keys=True, separators=(',', ':'))
    return hashlib.sha256(canonical.encode()).hexdigest()
a, b = map(fingerprint, sys.argv[1:])
print(a, b, sep='\n')
sys.exit(a != b)
PY
```

Le point de comparaison local reste dans le worktree créé. Les deux mesures doivent utiliser le même runtime pour comparer l’intégralité des champs stables. Les valeurs de latence sont des observations ponctuelles, sans conclusion de gain ou de perte de débit.
