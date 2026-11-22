# Mesures appariées sur CPU

Base signée et candidat ont été exécutés sur le même hôte avec le SDK .NET 10.0.404 et le runtime 10.0.15. Les processus ont alterné dans l’ordre base/candidat, candidat/base, base/candidat. Les durées ci-dessous sont les médianes des trois exécutions. Les valeurs individuelles, les moyennes et les p95 figurent dans `recette/mesures.json`.

| Mesure | Unité | Base signée | Candidat | Écart |
|---|---|---:|---:|---:|
| Construction froide · 64 passages | ms | 37,76 | 29,23 | -22.6 % |
| Allocation BM25 chaud · 64 passages | octets/appel | 171 439,00 | 171 609,00 | +0.1 % |
| BM25 chaud · 64 passages · p50 des 24 mesures | ms | 0,21 | 0,23 | +12.7 % |
| BM25 chaud · 64 passages · p95 des 24 mesures | ms | 0,36 | 0,42 | +16.5 % |
| Classement chaud · 64 passages · p50 des 24 mesures | ms | 0,45 | 0,45 | -0.6 % |
| Classement chaud · 64 passages · p95 des 24 mesures | ms | 0,67 | 0,65 | -3.7 % |
| Construction froide · 512 passages | ms | 91,87 | 147,01 | +60.0 % |
| Allocation BM25 chaud · 512 passages | octets/appel | 1 357 565,00 | 1 357 575,00 | +0.0 % |
| BM25 chaud · 512 passages · p50 des 24 mesures | ms | 1,34 | 1,41 | +5.5 % |
| BM25 chaud · 512 passages · p95 des 24 mesures | ms | 3,03 | 3,88 | +28.3 % |
| Classement chaud · 512 passages · p50 des 24 mesures | ms | 2,51 | 2,56 | +1.7 % |
| Classement chaud · 512 passages · p95 des 24 mesures | ms | 5,10 | 4,85 | -4.9 % |
| Construction froide · 2048 passages | ms | 271,05 | 318,44 | +17.5 % |
| Allocation BM25 chaud · 2048 passages | octets/appel | 5 453 179,00 | 5 453 180,00 | +0.0 % |
| BM25 chaud · 2048 passages · p50 des 24 mesures | ms | 6,35 | 6,97 | +9.8 % |
| BM25 chaud · 2048 passages · p95 des 24 mesures | ms | 14,11 | 13,83 | -2.0 % |
| Classement chaud · 2048 passages · p50 des 24 mesures | ms | 10,99 | 10,47 | -4.7 % |
| Classement chaud · 2048 passages · p95 des 24 mesures | ms | 19,26 | 16,36 | -15.0 % |
| Indexation persistante et reprise | s | 44,87 | 13,05 | -70.9 % |
| Mémoire et réparation du checkpoint | s | 2,40 | 2,45 | +2.1 % |
| Recherche hybride sur vecteurs préparés | s | 0,07 | 0,06 | -9.3 % |
| RSS maximale du benchmark | MiB | 105,31 | 102,28 | -2.9 % |
| RSS maximale de la recette de persistance | MiB | 348,18 | 338,70 | -2.7 % |
| Premier delta Ollama CPU | ms | 546,63 | 516,76 | -5.5 % |
| Tour Ollama CPU avec persistance | ms | 1 250,76 | 982,85 | -21.4 % |

Les tailles de corpus sont 64, 512 et 2 048 passages. Chaque processus exécute 24 requêtes par taille. Les scores et l’ordre sont comparés aux algorithmes de référence pour chaque corpus. La mesure froide inclut la construction de l’index. La mesure chaude inclut la vérification de son empreinte. Les maxima RSS proviennent de wait4 sur les processus de commande. La VRAM résidente du modèle CPU est de 0 octet.

Les cas de persistance utilisent la même méthode VSTest sur les deux arbres et PostgreSQL jetable. Le temps comprend la création de la base, les opérations et le nettoyage. Le scénario HTTP utilise Ollama CPU, un modèle Qwen préchargé et une base isolée. Le premier delta et le tour complet sont chronométrés par le client. Les durées des builds réels sont consignées dans `recette/build-debug.log` et `recette/build-release.log`.

Trois répétitions ne permettent pas d’estimer précisément une queue de distribution. Des compilations et recettes ont également utilisé la machine. Les variations constatées sont conservées sans attribution causale. Les nouveaux verrous couvrent les prédicats d’autorisation et peuvent retarder les publications lors d’imports concurrents. La latence de production sous charge multi-utilisateur et les consommations GPU restent non mesurées.

Pour reproduire le microbenchmark sur chaque arbre, restaurer et compiler `benchmarks/Alycia.ContextBenchmarks`, puis exécuter trois fois `dotnet run --no-build --project benchmarks/Alycia.ContextBenchmarks -- résultat.json` avec des noms de fichiers distincts. Pour les autres mesures, exécuter les trois méthodes VSTest du tableau et `python3 scripts/tests/http-integration.py --case ollama` par arbre et par répétition.
