# D19 — Rapport de recette du découplage

**Découplage partiel.** La branche part du commit develop `bc8515475d73f30842344156f3de3850a67b4c4f`. Elle conserve le monorepo et le déploiement monolithique. La PR nº 50 cible develop et reste en brouillon pour revue.

Les 327 tests .NET passent sans échec ni cas ignoré. Les 316 identifiants VSTest de D00 sont tous présents. Les 21 tests Python et les 35 scénarios HTTP passent. Le document OpenAPI de 47 chemins est identique à l’octet. L’export v1 mesure 2 074 octets. Les huit configurations RAG et les deux cas de stress produisent les mêmes 92 lignes ordonnées. Le rapport canonique inclut aussi 16 jugements et 20 classifications. Les empreintes et compteurs par suite figurent dans [D19-preuves.json](D19-preuves.json).

Les 21 projets évalués ont 67 références directes. Le compilateur relève 67 couples de propriétaires dans le code Chat. Douze assemblies se construisent en isolation et huit cas de la suite autonome Inference passent sans Chat, base ou serveur de modèle. Les dépendances encore admises sont listées dans transition.tsv et module_rules.py. [Graphe final](D19-graphe-final.tsv) · [Matrice des contrats](D19-matrice-contrats.csv) · [Critères restant ouverts](D19-risques-residuels.md).

## Bilan par phase

Les SHA courts sont ceux des commits GitHub de la branche de revue. D00 comprend deux commits de publication. D19-preuves.json détaille les correspondances avec les commits locaux signés et leurs parents. Le manifeste de signatures ajouté par le commit de livraison donne les SHA complets de D19 et permet de vérifier les vingt commits natifs.

| Phase et rapport | Statut final | Entrée GitHub | Sortie GitHub | Résultat et limite |
|---|---|---|---|---|
| [D00](D00-baseline.md) | VALIDÉ | `bc8515475` | `6ee4da414` | Référence Linux figée. PostgreSQL, contrats, tests, corpus et migrations mesurés. |
| [D01](D01-dependency-graph.md) | PARTIEL | `6ee4da414` | `ff641acda` | 3 307 déclarations et 712 accès recensés. Les appels dynamiques ne sont pas entièrement attribuables. |
| [D02](D02-target-architecture.md) | PARTIEL | `ff641acda` | `617c1b8dd` | DAG cible et invariants spécifiés. La matrice exhaustive de toutes les relations négatives reste ouverte. |
| [D03](D03-garde.md) | PARTIEL | `617c1b8dd` | `024c0cc73` | Compilations Roslyn, transitivité, cycles et fixtures négatives. Des accès hérités restent autorisés et nommés. |
| [D04](D04-configuration.md) | PARTIEL | `024c0cc73` | `b86dea58b` | 42 tests. Instantané avant attente du modèle et options isolées par résolution. ChatOptions traverse encore des ports. |
| [D05](D05-contrats.md) | PARTIEL | `b86dea58b` | `84f03b7c3` | 52 tests. Protocoles du fournisseur extraits. Plusieurs ports utilisent encore les entités et options historiques. |
| [D06](D06-transactions.md) | PARTIEL | `84f03b7c3` | `3ba4a5068` | 11 tests PostgreSQL. Session et commit partagés pour le projet. Répartition des autres écritures inachevée. |
| [D07](D07-workspaces.md) | PARTIEL | `3ba4a5068` | `5d6b226ff` | 3 tests ciblés. Identité canonique et profil créés atomiquement. Notes, adhésions et lectures de profil encore partagées. |
| [D08](D08-tour.md) | PARTIEL | `5d6b226ff` | `12a955ade` | 20 tests. Ports du tour injectés et révision relue après rollback. Des diagnostics et projections restent couplés. |
| [D09](D09-documents.md) | PARTIEL | `12a955ade` | `c96bb4de6` | 33 tests. Stockage et extraction isolés. Le mapping des pièces et quelques accès physiques restent à déplacer. |
| [D10](D10-inference.md) | VALIDÉ | `c96bb4de6` | `ee2c92dda` | 58 tests ciblés à D10. Bail, priorité et permis séparés à D17. Suite indépendante et mesures sur fournisseur simulé à D19. |
| [D11](D11-indexation.md) | PARTIEL | `ee2c92dda` | `5284758db` | Magasin de jobs et vecteurs autonome, découverte paginée. Le traitement des lots lit encore des entités EF voisines. |
| [D12](D12-retrieval.md) | PARTIEL | `5284758db` | `9354e8d9d` | 29 tests et 92 lignes RAG identiques. Classement et adaptateur ONNX isolés. Orchestrateur historique et poids réels restent ouverts. |
| [D13](D13-contexte.md) | PARTIEL | `9354e8d9d` | `6adf4d494` | 31 tests. La préparation consomme un port de lecture. Options et entités Chat encore visibles. |
| [D14](D14-memoire.md) | PARTIEL | `6adf4d494` | `21ef3487c` | 11 tests. Changement mémoire écrit avec le tour. Le magasin temporel conserve le contexte EF partagé. |
| [D15](D15-outils.md) | PARTIEL | `21ef3487c` | `040dc3310` | 19 tests. Autorisation revérifiée à la lecture, EF et construction des voisins retirés de Tools. Assembly encore partagée. |
| [D16](D16-mocks.md) | PARTIEL | `040dc3310` | `54838af27` | Démo opt-in et deux suites communes texte/vecteurs. La matrice de 37 ports identifie les conformités manquantes. |
| [D17](D17-composition.md) | PARTIEL | `54838af27` | `2566ce926` | 30 tests et scénario HTTP transport. Durées de vie et priorité explicites. Essais de bureau natif bloqués. |
| [D18](D18-barrieres.md) | PARTIEL | `2566ce926` | `00e2c8d99` | Douze assemblies compilées isolément. Une suite autonome supplémentaire à D19. Plusieurs domaines résident toujours dans les assemblies Chat. |
| [D19](D19-risques-residuels.md) | PARTIEL | `00e2c8d99` | [manifeste](signatures/manifest.json) | Recette globale et preuve des sources. La propriété unique et la conformité de tous les ports restent ouvertes. |

## Récupération et compilation

Depuis un clone existant, Git crée le suivi local de la branche si elle existe seulement sur origin.

```bash
git fetch origin
git switch alycia-decouplage-modulaire-20261121
git pull --ff-only origin alycia-decouplage-modulaire-20261121
dotnet restore Alycia.slnx --disable-parallel -m:1
dotnet tool restore
dotnet build Alycia.slnx -c Release --no-restore -m:1
dotnet publish src/Alycia.Api -c Release --no-build -m:1 -o artifacts/publish/api
dotnet publish src/Alycia.Client.Avalonia -c Release --no-build -m:1 -o artifacts/publish/client
```

Pour un premier clone, utiliser `git clone --branch alycia-decouplage-modulaire-20261121 https://github.com/alescis-wuin/alycia.git`. Le SDK est épinglé par global.json.

## Recette PostgreSQL et comparaison avec D00

Utiliser une instance PostgreSQL de **test** et un administrateur autorisé à créer les bases temporaires. Configurer ALYCIA_CHAT_TEST_CONNECTION avec sa chaîne de connexion et ALYCIA_TEST_PGVECTOR=1 avec l’extension vector disponible. Utiliser des répertoires de résultats vides pour chaque campagne. Le comparateur attend une seule exécution et l’évaluateur RAG crée un nouveau fichier.

```bash
mkdir -p artifacts/recette
bash scripts/check.sh --integration
python3 -m unittest discover -s scripts/tests -p '*_tests.py'
python3 scripts/tests/http-integration.py --case all > artifacts/recette/http.json
dotnet build benchmarks/Alycia.ContextBenchmarks -m:1 --no-dependencies
python3 scripts/evaluate-context.py offline --pipeline-v2 --output artifacts/recette/rag.json
python3 scripts/architecture/capture-openapi.py artifacts/recette/openapi.json
python3 scripts/architecture/verify-baseline.py --http artifacts/recette/http.json \
  --rag artifacts/recette/rag.json --openapi artifacts/recette/openapi.json --trx artifacts/tests
python3 scripts/check-modules.py --matrix
python3 scripts/build-isolated-modules.py
env ALYCIA_MEASURE_OLLAMA_QUEUE=1 dotnet test tests/Alycia.Modules.Chat.Tests --no-build --filter FullyQualifiedName~OllamaSchedulingTests
```

check.sh exécute entre autres dotnet test Alycia.slnx, le lint et la vérification des modèles des deux contextes EF. Sur Windows, l’entrée prévue est `pwsh -File scripts/check.ps1 -Integration` avec Python 3 et .NET 10 disponibles. Cette commande Windows n’a pas été exécutée.

## Démo et lancement

La démo de développement suivante expose le catalogue et un échantillon de génération sur loopback sans dépendance à PostgreSQL ou Ollama. La sonde démarre sa propre instance, éprouve le refus des origines et hôtes étrangers puis le refus de l’opt-in en Production.

```bash
python3 scripts/test-mock-api.py
env ASPNETCORE_ENVIRONMENT=Development ALYCIA_MOCK_INFERENCE=true \
  dotnet run --no-launch-profile --project src/Alycia.Api -- --urls http://127.0.0.1:5187
```

Dans un deuxième terminal, `curl -fsS http://127.0.0.1:5187/api/chat/mock/sample` retourne les deux fragments et la réponse. Pour le chat réel sous Linux, démarrer le serveur Ollama local avec un modèle conversationnel installé, puis lancer `make demo-api` et, dans un deuxième terminal, `make demo-client`. Le script de démonstration utilise Docker Compose, PostgreSQL et le volume des originaux. Le contrôle réel facultatif s’appelle avec `python3 scripts/verify-local.py --url http://127.0.0.1:5187 --model NOM_LOCAL --extended --tools`.

## Preuves et intégrité Git

Les migrations et le corpus d’évaluation sont ceux de develop. Les arbres publiés sont comparés aux arbres testés avant la mise à jour de la branche. Les 20 objets de commit locaux ont une signature SSH. GitHub crée ses propres objets de commit via son API et les rapporte comme non signés. La procédure dans le dossier signatures vérifie la chaîne native et l’égalité des arbres sur un clone frais. L’attestation locale ne satisfait pas à elle seule la règle de signature GitHub de la branche protégée.

D19-preuves.json donne également les fichiers modifiés et les commandes de reproduction par phase. Les empreintes des traces locales identifient cette exécution. La comparaison reproductible utilise les empreintes canoniques et les identifiants de test. Le compte rendu ne publie pas les contenus bruts des échanges HTTP, des campagnes de génération ou des traces TRX. Aucune phase n’est entièrement bloquée. Les contrôles qui n’ont pas pu être exécutés et les conditions de reprise figurent dans le registre des risques.
