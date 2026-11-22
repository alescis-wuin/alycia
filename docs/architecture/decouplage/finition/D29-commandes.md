# Reproduire la finition

Les blocs Linux utilisent Bash. Les commandes de base fonctionnent avec le SDK .NET 10, Python 3 et Git. PostgreSQL et les modèles sont nécessaires aux essais d’intégration. Les répertoires de résultats doivent être neufs pour comparer les TRX.

## Récupérer la branche

```bash
git clone --branch refactor/alycia-finition-decouplage-20261121 https://github.com/alescis-wuin/alycia.git alycia-finition
cd alycia-finition
git fetch origin
git status --short --branch
git merge-base --is-ancestor 7b04de23ee1854cfdbe2c4f8d91e3adf204f4f53 HEAD
git rev-parse HEAD
```

Depuis un clone existant, un worktree détaché laisse les fichiers locaux en place.

```bash
git fetch origin
git worktree add --detach ../alycia-finition-verification origin/refactor/alycia-finition-decouplage-20261121
cd ../alycia-finition-verification
```

## Compiler et publier

```bash
dotnet tool restore
dotnet restore Alycia.slnx --disable-parallel -m:1
dotnet build Alycia.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet build Alycia.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet publish src/Alycia.Api -c Release --no-build -o artifacts/publish/api
dotnet publish src/Alycia.Client.Avalonia -c Release --no-build -o artifacts/publish/boreale
dotnet build benchmarks/Alycia.ContextBenchmarks -m:1 -p:UseSharedCompilation=false
python3 scripts/test-mock-api.py --published artifacts/publish/api
```

## Exercer PostgreSQL et les vrais fournisseurs

Pour reproduire le passage sans cas ignoré, fournir les poids ONNX vérifiés. Le dépôt de modèles est public. Les deux commandes de contrôle vérifient les octets utilisés en D29.

```bash
export ALYCIA_RERANK_DIRECTORY="$PWD/artifacts/horizon"
mkdir -p "$ALYCIA_RERANK_DIRECTORY/onnx"
horizon=https://huggingface.co/Horizon-Labs/multilingual-reranker-small/resolve/23139fec6f1f448cdf89a5ac2d2c6bf5e8c7b398
curl -fL --retry 2 "$horizon/tokenizer.json" -o "$ALYCIA_RERANK_DIRECTORY/tokenizer.json"
curl -fL --retry 2 "$horizon/onnx/model_quantized.onnx" -o "$ALYCIA_RERANK_DIRECTORY/onnx/model_quantized.onnx"
(cd "$ALYCIA_RERANK_DIRECTORY" && sha256sum -c <<'HASH'
7925f638b5444897efe3b4608eed56c1652f0ed39c2662c8302de1bc264c6088  tokenizer.json
f913154682468bfe92d6a7e02ed4e994e548c112fddc74452b573b17c949046e  onnx/model_quantized.onnx
HASH
)
```

Avec Docker, cette commande du projet crée et arrête son propre serveur d’essai. La campagne publiée ici a utilisé PostgreSQL natif.

```bash
env -u ALYCIA_CHAT_TEST_CONNECTION bash scripts/check-context-v2.sh --pgvector
```

Pour conserver les résultats détaillés, utiliser une instance jetable avec pgvector et un rôle autorisé à créer des bases. Dans le même shell, renseigner sa chaîne `ALYCIA_CHAT_TEST_CONNECTION`. Le test crée puis supprime ses propres bases. Le dossier `$proof` reçoit une seule campagne.

```bash
: "${ALYCIA_CHAT_TEST_CONNECTION:?Connexion au serveur jetable requise}"
export ALYCIA_TEST_PGVECTOR=1
mkdir -p artifacts
proof=$(mktemp -d "$PWD/artifacts/finition-XXXXXXXX")
python3 scripts/check-modules.py --matrix > "$proof/chat.tsv"
python3 -m unittest discover -s scripts/tests -p '*_tests.py'
dotnet test Alycia.slnx --no-build --no-restore -m:1 --logger trx --results-directory "$proof/trx"
python3 scripts/architecture/finition-test-ids.py "$proof/trx" --reference docs/architecture/decouplage/finition/D20-tests.tsv > "$proof/ids.tsv"
for suite in all originals mock; do
  python3 scripts/tests/http-integration.py --case "$suite" > "$proof/http-$suite.json" || exit 1
done
python3 scripts/architecture/capture-openapi.py "$proof/openapi.json"
python3 scripts/evaluate-context.py offline --pipeline-v2 --output "$proof/rag.json"
python3 scripts/architecture/verify-baseline.py --http "$proof/http-all.json" --rag "$proof/rag.json" --openapi "$proof/openapi.json" --trx "$proof/trx"
for module in Chat Workspaces; do
  dotnet tool run dotnet-ef migrations has-pending-model-changes --project "src/Alycia.Modules.$module.Infrastructure" --context "${module}DbContext" --no-build || exit 1
done
python3 scripts/build-isolated-modules.py --require-complete
```

Le dernier contrôle compile et teste les projets enregistrés, puis tente aussi de compiler les assemblies restantes dans des répertoires privés de toute implémentation voisine. Il sort avec le code 1 tant que l’autonomie de Chat.Application et Chat.Infrastructure reste ouverte. Pour mesurer uniquement les modules déjà isolés, exécuter `python3 scripts/build-isolated-modules.py`.

Ollama doit écouter sur loopback. Dans un terminal, lancer `ollama serve` si le service est arrêté. Dans le terminal de recette, avec la connexion de test encore définie, exécuter les commandes suivantes. Les identités de la campagne figurent dans D29-preuves.json. Le résultat indique le délai réel jusqu’au premier événement delta.

```bash
ollama pull qwen2.5:0.5b
ollama pull all-minilm:22m
export ALYCIA_TEST_OLLAMA_URL=http://127.0.0.1:11434
export ALYCIA_TEST_CHAT_MODEL=qwen2.5:0.5b
python3 scripts/tests/http-integration.py --case ollama
curl -fsS http://127.0.0.1:11434/api/ps
```

## Inspecter les frontières et reproduire les inventaires

Le collecteur conserve les références de projets évaluées et les usages de symboles. Le schéma s’extrait avec `psql` dans une base **déjà migrée par les deux contextes**, au moyen de ses variables libpq ou de son service local. Les bases éphémères par test sont indépendantes de cette inspection.

```bash
mkdir -p "$proof/capture" "$proof/matrices"
dotnet build scripts/architecture/capture/Inventory.csproj -m:1 -p:UseSharedCompilation=false
capture=(-m:1 -p:UseSharedCompilation=false -p:RunAnalyzers=true -warnaserror:AD0001
  "-p:CustomAfterMicrosoftCommonTargets=$PWD/scripts/architecture/capture/Capture.targets"
  "-p:AlyciaArchitectureAnalyzer=$PWD/scripts/architecture/capture/bin/Debug/net10.0/Inventory.dll"
  "-p:AlyciaArchitectureRoot=$PWD" "-p:AlyciaArchitectureOutput=$proof/capture")
dotnet build Alycia.slnx --no-restore -t:Rebuild "${capture[@]}"
dotnet build benchmarks/Alycia.ContextBenchmarks --no-dependencies -t:Rebuild "${capture[@]}"
python3 scripts/architecture/export-review.py "$proof/capture" "$proof/matrices"
psql -X -v ON_ERROR_STOP=1 -f scripts/architecture/finition-schema.sql > "$proof/schema.tsv"
python3 scripts/architecture/finition-audit.py "$proof/capture" "$proof/trx" "$proof/matrices" --schema "$proof/schema.tsv"
dotnet run --no-build --project benchmarks/Alycia.ContextBenchmarks -- "$proof/performance.json"
```

## Lancer l’application

Avec Ollama et Docker installés sous Linux, ouvrir deux terminaux depuis la racine.

```bash
make demo-api
```

```bash
make demo-client
```

Pour lancer l’API hors conteneur, utiliser une **base de développement** distincte, avec les migrations Workspaces et Chat appliquées. Dans le premier terminal, définir la connexion de développement et lancer l’hôte. L’URL cliente utilise le même port.

```bash
: "${ALYCIA_CHAT_CONNECTION:?Connexion à la base de développement requise}"
export ALYCIA_WORKSPACES_CONNECTION="$ALYCIA_CHAT_CONNECTION"
dotnet tool run dotnet-ef database update --project src/Alycia.Modules.Workspaces.Infrastructure --context WorkspacesDbContext --no-build
dotnet tool run dotnet-ef database update --project src/Alycia.Modules.Chat.Infrastructure --context ChatDbContext --no-build
export ASPNETCORE_ENVIRONMENT=Development ALYCIA_ENABLE_LOCAL_CHAT=true ALYCIA_ENABLE_LOCAL_WORKSPACES=true
export ALYCIA_OLLAMA_BASE_URL=http://127.0.0.1:11434
dotnet run --no-build --project src/Alycia.Api --urls http://127.0.0.1:5187
```

```bash
ALYCIA_API_BASE_URL=http://127.0.0.1:5187 dotnet run --no-build --project src/Alycia.Client.Avalonia
```

La démonstration Mock sans base ni Ollama expose le catalogue et `/api/chat/mock/sample`. Le mode conversation complet du DevHost utilise PostgreSQL et l’inférence simulée. Dans un shell neuf pour la démonstration minimale, lancer le premier bloc. Avec la configuration Chat de développement ci-dessus, employer le second.

```bash
env ALYCIA_ENABLE_LOCAL_CHAT=false ALYCIA_ENABLE_LOCAL_WORKSPACES=false ALYCIA_MOCK_INFERENCE=true ASPNETCORE_ENVIRONMENT=Development dotnet run --no-build --project src/Alycia.DevHost --urls http://127.0.0.1:5187
```

```bash
ALYCIA_MOCK_INFERENCE=true dotnet run --no-build --project src/Alycia.DevHost --urls http://127.0.0.1:5187
```

## Essais externes et signature

Pour Docker, définir le secret de développement `POSTGRES_PASSWORD` exigé par Compose, puis utiliser `docker compose config --quiet`, `docker compose up --build -d`, `docker compose ps`, `curl -fsS http://127.0.0.1:8080/healthz`, puis `docker compose stop`. Pour le GPU Ollama, rejouer la recette réelle et inspecter la mémoire vidéo du modèle pendant sa résidence avec `ollama ps` et `/api/ps`. Pour KDE, lancer Boréale dans une session Wayland et exercer clavier, navigation, dialogue de fichiers et flux d’un tour. Sous Windows, avec PostgreSQL de test et les poids disponibles, exécuter `./scripts/check.ps1 -Integration`, puis `./scripts/demo.ps1 -Action All`. Ces essais ont le statut NON TESTÉ sur leurs plateformes respectives.

La PR #51 conserve les commits publiés par le connecteur. GitHub les classe `unsigned`. L’intégrateur prépare la livraison signée sur sa machine, avec une identité SSH ou GPG de signature enregistrée auprès de GitHub. Les trois SHA complets sont ceux du fichier `LIRE-MOI.md` de l’archive de preuves. Le script vérifie les deux branches distantes, signe chaque commit sur une nouvelle branche locale et contrôle chaque arbre. Il s’arrête si la base ou la tête a changé.

```bash
bash scripts/architecture/preparer-signature.sh "$base_testee" "$tete_pr" "$arbre_attendu" ../alycia-finition-signee
git -C ../alycia-finition-signee push --set-upstream origin HEAD
```

Après l’envoi, exécuter ce contrôle avec GitHub CLI dans Bash. Le compte GitHub doit reconnaître chaque signature.

```bash
(
  set -e
  cd ../alycia-finition-signee
  test "$(git rev-parse 'HEAD^{tree}')" = "$arbre_attendu"
  for commit in $(git rev-list "$base_testee..HEAD"); do
    test "$(gh api "repos/alescis-wuin/alycia/git/commits/$commit" --jq '.verification.verified')" = true
  done
)
```

La branche signée est distincte de la tête de la PR #51. Le fichier `LIRE-MOI.md` contient également la commande de création de sa PR en brouillon vers la même candidate.

Le rapport des phases donne les SHA de retour arrière. Sur une branche de correction, `git revert <sha>` inverse une phase. Pour abandonner toute la série, quitter la branche de finition et utiliser la tête candidate de la PR #50. Les migrations historiques et les originaux existants sont conservés. Une promotion ultérieure requiert fermeture des écarts obligatoires, vérification GitHub, revue de la PR empilée, puis revue de #50 vers develop. Les promotions vers testing et main suivent ensuite le processus du dépôt.
