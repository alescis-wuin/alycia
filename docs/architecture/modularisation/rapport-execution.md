# Rapport d’exécution

Branche de revue `refactor/alycia-modularisation-20261121`. Base fonctionnelle vérifiée sur sept branches le 21 novembre 2026, `309d610425786e668782769f5b7650902d11d37b`. Chaque section correspond au commit dont le sujet commence par le numéro de phase. `git log --reverse --oneline 309d610..HEAD` donne les identifiants, y compris celui du présent document.

## P0 — Référence

Inventaire des projets, routes, DTO, services, tables, migrations et branches dans [baseline.md](baseline.md). Le flux HTTP est du NDJSON. Le SHA Git et les blocs ci-dessous permettent de reproduire la mesure.

| Contrôle sur la base | Résultat |
|---|---|
| `dotnet --info` et `dotnet sln Alycia.slnx list` | SDK 10.0.404 Linux x64, 11 projets dans la solution. |
| `bash scripts/check.sh` sans connexion de test | Compilation 0 avertissement / 0 erreur, lint réussi, aucune modification EF Chat en attente. Avalonia 48/48, Chat 182 réussis et 7 ignorés (PostgreSQL), Workspaces 11/11. Total 241 réussis, 0 échec, 7 ignorés. Durée 156,8 s avec restauration à froid. |
| `dotnet build benchmarks/Alycia.ContextBenchmarks` puis `python3 scripts/evaluate-context.py offline --pipeline-v2 --output <nouveau-fichier>` | 11,1 s. Corpus principal, 10 cas, k=3, Recall@3, nDCG@3 et MRR à 1 en référence et en combinaison. Aucun seuil du protocole en échec. |
| Prestataires externes | PostgreSQL 17.12 isolé provisionné sur loopback avec authentification SCRAM pour les recettes. Aucun modèle Ollama ou poids ONNX chargé. |

Les TRX et sorties complètes des mesures initiales ont été conservés dans l’espace de travail, hors Git. Le benchmark utilise son propre projet hors solution. Le comportement d’intégration est renseigné dans les phases concernées.

## P1 — Contrats de propriété

[ownership.md](ownership.md) fixe l’entrée, la sortie et la transaction de chaque domaine, les six décisions de conception et les fichiers réservés à l’intégrateur. Revue du graphe actuel et des consommateurs du client. Une seule nouvelle assembly de transport est prévue. Aucun changement exécutable pendant cette phase.
