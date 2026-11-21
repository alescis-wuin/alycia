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

## P2 — Caractérisation

Export original v1 enregistré avant refonte dans `Fixtures/conversation-settings-v1.json`, test de restitution à l’octet, et scénario HTTP de collision de noms ajoutés. [Matrice des contrôles](verification.md). Sur PostgreSQL 17.12 jetable, le filtre Chat couvrant ConversationSettingsTransfer, PostgresChat, PersistentEmbedding, KnowledgePipeline, Streaming et LocalChatAccess a donné 44 réussis, 0 échec, 0 ignoré (38 s). La recette `python3 scripts/tests/http-integration.py --case boreale` est passée de 3 à 4 contrôles réussis. Le premier essai d’infrastructure n’a pu joindre le serveur d’un autre processus isolé. Les validations ci-dessus démarrent PostgreSQL dans le même environnement réseau que les tests.

## P3 — Transport et réglages

`Alycia.Contracts` rassemble les enregistrements JSON partagés, la lecture NDJSON et le codec v1. `ChatSettings` contient les champs persistés. `ChatOptions` en dérive et porte les données transitoires exclues du JSON. Les vues de génération, fenêtre, classement, mémoire, outils et index sont utilisées par leurs consommateurs. Les valeurs persistées sont scalaires ou immuables, y compris Enrichment. Les collections et le compteur transitoires des copies d’un tour sont lus, ou remplacés, sans mutation en place du réglage d’une autre discussion. Le client utilise des projections du contexte et des notes et conserve sa validation locale. Le Dockerfile inclut le projet de contrats.

`dotnet list src/Alycia.Client.Avalonia reference` indique uniquement Alycia.Contracts. Compilation de la solution réussie. Tests du client 48/48, filtres serveur des réglages, budgets, classement et mémoire 34/34. Après ajustement du validateur d’export serveur, 16/16 tests de transfert et mémoire. Lint réussi. Le fichier v1 produit par la base reste reproductible à l’octet.
