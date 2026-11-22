# Récupération, exécution et signatures

La branche réservée est `refactor/alycia-cloture-decouplage-20261122`. Elle pointe sur la base signée `58182f2a7492b04f1f0b4c1518017a675519b376`. Les 32 signatures D20–D29 ont été confirmées par l’API GitHub. Le commit applicatif local `a778092475eb0dc28682a5e9284b56a0bc379ad6` a pour parent cette base et pour arbre `75a97fd3b98f77ebe2f90779f67dda956d086b8e`. Git signe le candidat local avec une clé d’attestation propre à la recette. La clé publique figure dans la remise.

L’API GitHub a reçu cet arbre exact. Pour le commit détaché `7864552762621c9f21b944274d21dfb8931dbe17`, elle a renvoyé `verified: false` et `reason: unsigned`. Le Git du poste d’exécution ne dispose pas d’une identité GitHub avec droit d’écriture. Les références vérifiées sont dans `recette/refs-distantes.tsv`. Les PR historiques #50 et #51 sont restées en brouillon. Aucune PR de clôture n’a pu être ouverte avec la branche distante à la base. Les résultats des requêtes REST sont dans `recette/github-base.json` et `recette/github-objet-code.json`.

## Récupérer la livraison complète

Extraire la remise ZIP à côté d’un clone du dépôt. Depuis la racine du clone, les commandes suivantes créent un worktree indépendant contenant le code et les preuves. Le manifeste extérieur indique le SHA de la tête, celui de l’arbre complet et le SHA-256 du bundle. Le commit des preuves a exactement pour parent le commit applicatif.

```bash
bash ../alycia-cloture/recuperer.sh "$PWD" ../alycia-candidat
cd ../alycia-candidat
git rev-parse HEAD HEAD^{tree}
(cd docs/architecture/decouplage/cloture && sha256sum -c SHA256SUMS)
```

## Construire et lancer

```bash
dotnet tool restore
dotnet restore Alycia.slnx --disable-parallel -m:1
dotnet build Alycia.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet build Alycia.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
```

Les tests de conformité complets utilisent PostgreSQL avec droit CREATEDB sur un serveur **jetable**, pgvector et les poids de reranking indiqués dans `../finition/D29-commandes.md`. La suite est prévue pour un serveur sans données utilisateur. Exporter `ALYCIA_CHAT_TEST_CONNECTION`, `ALYCIA_TEST_PGVECTOR=1` et `ALYCIA_RERANK_DIRECTORY` avant la recette.

```bash
bash scripts/check.sh --integration
python3 -m unittest discover -s scripts/tests -p '*_tests.py'
dotnet publish src/Alycia.Api -c Release --no-build --no-restore -m:1 -o artifacts/publish/api
dotnet publish src/Alycia.Client.Avalonia -c Release --no-build --no-restore -m:1 -o artifacts/publish/boreale
python3 scripts/test-mock-api.py --published artifacts/publish/api
```

Pour lancer l’API réelle, configurer une base de développement et un Ollama local. Les deux commandes de migration suivantes s’appliquent uniquement à **cette base de développement**. Le projet technique `Alycia.Persistence.Migrations` porte désormais les migrations Chat.

```bash
: "${ALYCIA_CHAT_CONNECTION:?Définir la connexion PostgreSQL de développement}"
export ALYCIA_WORKSPACES_CONNECTION="$ALYCIA_CHAT_CONNECTION"
export ASPNETCORE_ENVIRONMENT=Development ALYCIA_ENABLE_LOCAL_CHAT=true ALYCIA_ENABLE_LOCAL_WORKSPACES=true
export ALYCIA_OLLAMA_BASE_URL=http://127.0.0.1:11434
dotnet tool run dotnet-ef database update --project src/Alycia.Modules.Workspaces.Infrastructure --context WorkspacesDbContext --no-build
dotnet tool run dotnet-ef database update --project src/Alycia.Persistence.Migrations --context ChatDbContext --no-build
dotnet run --no-build --project src/Alycia.Api --urls http://127.0.0.1:5187
```

Dans un second terminal, lancer Boréale. Pour le DevHost Mock minimal, utiliser un autre terminal de développement et un port libre.

```bash
ALYCIA_API_BASE_URL=http://127.0.0.1:5187 dotnet run --no-build --project src/Alycia.Client.Avalonia
env ASPNETCORE_ENVIRONMENT=Development ALYCIA_ENABLE_LOCAL_CHAT=false ALYCIA_ENABLE_LOCAL_WORKSPACES=false ALYCIA_MOCK_INFERENCE=true dotnet run --no-build --project src/Alycia.DevHost --urls http://127.0.0.1:5190
```

## Publier la clôture depuis le poste du mainteneur

Le clone doit pouvoir lire le dépôt. Git doit être configuré pour signer avec une identité GPG ou SSH déjà reconnue par GitHub, et le client `gh` doit être authentifié avec le droit de créer des objets, mettre à jour la nouvelle branche et ouvrir une PR. Depuis la racine du clone, lancer le script livré dans la remise.

```bash
bash ../alycia-cloture/signer-et-publier.sh "$PWD" ../alycia-cloture-signee
```

Le script crée un commit signé depuis la base historique avec l’arbre complet du bundle. Il vérifie l’objet signé détaché auprès de GitHub avant de mettre à jour la branche de clôture en avance rapide. Il vérifie à nouveau la tête, crée une PR en brouillon vers la branche signée de départ et réutilise une PR existante en cas de reprise. L’arbre doit être identique à l’arbre du manifeste malgré le changement d’auteur et de signature. Une divergence de la référence ou une signature refusée arrête la publication. Aucun jeton ni clé privée n’est fourni dans la remise.

Pour contrôler le résultat publié depuis la branche signée locale, les commandes suivantes affichent la vérification des nouveaux commits et la PR.

```bash
base=58182f2a7492b04f1f0b4c1518017a675519b376
for commit in $(git rev-list "$base..HEAD"); do
  gh api "repos/alescis-wuin/alycia/git/commits/$commit" --jq '[.sha,.tree.sha,.verification.verified,.verification.reason]'
done
gh pr list --repo alescis-wuin/alycia --head refactor/alycia-cloture-decouplage-20261122
```

## Intégration et retour arrière

Les ancêtres D00–D19 incluent des objets non signés. Après validation de **tous** les critères et revue de la clôture, une éventuelle intégration vers `develop` doit partir de la tête à jour de `origin/develop` dans un worktree neuf. Y effectuer un `git merge --squash <clôture-vérifiée>`, vérifier que `git write-tree` égale l’arbre accepté, puis créer un nouveau commit signé et vérifier sa signature GitHub. Une différence d’arbre exige une revue de la divergence. Aucune branche d’intégration ni PR vers `develop` n’a été créée.

Pour revenir au code de départ, arrêter le processus du candidat et utiliser un worktree indépendant de la base. Les originaux et le volume PostgreSQL restent en place. En exploitation, appliquer la politique de sauvegarde habituelle avant toute bascule, notamment pour les nouveaux messages intervenus entre les versions. Aucune migration descendante n’est nécessaire dans ce lot. Le SHA permet aussi de revenir exactement au candidat après comparaison.

```bash
git worktree add --detach ../alycia-retour-58182f2 58182f2a7492b04f1f0b4c1518017a675519b376
git -C ../alycia-retour-58182f2 rev-parse HEAD HEAD^{tree}
```
