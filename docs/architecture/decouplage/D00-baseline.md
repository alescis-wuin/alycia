# D00 — Référence de compatibilité

**VALIDÉ sur Debian 13 x64.** Le checkout propre correspond à `origin/develop` au SHA `bc8515475d73f30842344156f3de3850a67b4c4f`, arbre `5e7f406f5f927d7a50b464d69ed867da260b2336`. La PR nº 49 y a intégré `458f28f` après les PR de modularisation 46–48. `main` et `testing` pointaient sur `b1777f6`. La branche de travail a été créée sur la tête contrôlée. Le commit D00 ne change que les preuves.

| Contrôle sur le SHA initial | Résultat |
|---|---|
| Solution Debug, SDK 10.0.404, restauration et outil EF 10.0.14 | Compilation réussie, 0 avertissement et 0 erreur |
| PostgreSQL 17.12 temporaire, SCRAM, pgvector 0.8.0 | 316 tests .NET réussis, dont 63 Avalonia, 242 Chat, 11 Workspaces. Aucun ignoré |
| Garde C7, lint, suites Python | 75 couples de propriétaires observés, lint réussi, 17 tests réussis |
| Modèles EF Chat et Workspaces | Aucun changement de modèle en attente |
| HTTP avec PostgreSQL réel et fournisseur Ollama simulé | 35 scénarios réussis. Le JSON conserve les noms et compteurs |
| OpenAPI en Development avec les deux opt-ins | 47 chemins. Empreinte du document complet avec un Host fixe |
| RAG V2 hors ligne | Résultat ordonné contrôlé par empreinte canonique |
| Configuration v1 | Export de 2 074 octets, SHA-256 `b1d22617099059db98ec9a44cefa109927067d1a1ed25182f601e77135a2751b` |

[D00-tests.tsv](D00-tests.tsv) consigne les résultats individuels extraits des trois TRX. Les sorties brutes restent locales. [baseline.json](baseline.json) donne les empreintes, compteurs, modèles, projets et références. Les blobs Git y figent migrations, corpus, contrats, ressources et fixtures. [D00-edges.tsv](D00-edges.tsv) provient du scanner antérieur et comporte aussi les passages autorisés par port. Les chiffres de C7 sont renseignés séparément dans `historical`.

La première tentative d’intégration a échoué au raccordement de PostgreSQL, lancé dans un espace réseau différent du processus de test. La campagne retenue a lancé serveur et tests dans le même espace réseau avec un rôle et un cluster éphémères. Les essais GPU/Ollama réel, poids ONNX, construction Docker, Windows et KDE/Wayland ne figurent pas dans les résultats de référence.

## Commandes exécutées

Les commandes d’intégration utilisent `ALYCIA_CHAT_TEST_CONNECTION` fourni par le cluster éphémère et `ALYCIA_TEST_PGVECTOR=1`. L'administrateur nécessite CREATEDB sur cette instance de test.

```bash
git ls-remote https://github.com/alescis-wuin/alycia.git refs/heads/develop
dotnet restore Alycia.slnx -m:1
dotnet tool restore
dotnet build Alycia.slnx --no-restore -m:1 -p:UseSharedCompilation=false
python3 scripts/check-modules.py --matrix
bash scripts/quality.sh lint
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/Alycia.Modules.Chat.Infrastructure --context ChatDbContext --no-build
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/Alycia.Modules.Workspaces.Infrastructure --context WorkspacesDbContext --no-build
dotnet test Alycia.slnx --no-build --no-restore -m:1 --logger trx --results-directory /tmp/alycia-evidence/d00-trx
python3 -m unittest discover -s scripts/tests -p '*_tests.py'
python3 scripts/tests/http-integration.py --case all
dotnet build benchmarks/Alycia.ContextBenchmarks -m:1 --no-dependencies
python3 scripts/evaluate-context.py offline --pipeline-v2 --output /tmp/alycia-evidence/baseline-rag.json
```

L’API démarre dans `src/Alycia.Api/Program.cs`, Boréale dans `src/Alycia.Client.Avalonia/Program.cs` et le benchmark dans `benchmarks/Alycia.ContextBenchmarks/Program.cs`. La composition Chat est dans `Chat/ChatRegistration.cs`, celle de Workspaces dans le démarrage de l’API. Pour retrouver la sortie de D00, utiliser `git log -1 --format=%H -- docs/architecture/decouplage/D00-baseline.md`.
