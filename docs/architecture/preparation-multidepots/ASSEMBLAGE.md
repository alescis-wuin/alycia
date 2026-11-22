# Assembler Alycia et exporter un domaine

Le maître possède la composition DI, l’API, Boréale, le DevHost, les intégrations et l’historique EF Chat. Les douze répertoires sous `modules/` sont aujourd’hui des fichiers ordinaires du même dépôt. Leurs projets et leurs dépendances autorisées sont déclarés dans `modules.lock.json`.

## Préparer le build

Installer Git, Python 3.10 ou ultérieur et le SDK .NET 10.0.404. La version exacte sert à reconstruire les mêmes paquets et leurs empreintes. Exécuter depuis la racine.

```sh
git fetch origin develop prepare/extraction-multidepots-p0-p8
git switch --track origin/prepare/extraction-multidepots-p0-p8
python3 scripts/modules.py bootstrap-packages
python3 scripts/modules.py build-module inference
python3 scripts/modules.py test-module inference
python3 scripts/modules.py check-module retrieval
python3 scripts/modules.py check-extraction
python3 scripts/modules.py check-master
```

Chaque module accepte aussi `python3 scripts/check.py check --feed /chemin/vers/local-feed` depuis une copie vierge de son seul dossier. Le maître prépare `artifacts/local-feed/`. NuGet.org fournit les paquets tiers. Le mapping `Alycia.*` réserve les identités internes au feed local. Une restauration qui reçoit d’autres octets pour une version connue doit échouer. Pour diagnostiquer un cache différent, définir `NUGET_PACKAGES` vers un nouveau répertoire dédié à cette recette.

Sous PowerShell, utiliser `py -3` à la place de `python3`. La recette historique est accessible par `bash scripts/check.sh --integration` et `.\scripts\check.ps1 -Integration`. La recette maître conserve ses sorties dans `artifacts/acceptance/`. Les logs des copies isolées sont sous `artifacts/extraction/`.

## Recette PostgreSQL et ONNX

Fournir une instance PostgreSQL 17 jetable avec pgvector, les clients `psql`, `createdb` et `dropdb`, et un rôle de recette avec CREATEDB. Définir `ALYCIA_CHAT_TEST_CONNECTION` ainsi que les variables libpq `PGHOST`, `PGPORT`, `PGUSER` et, si nécessaire, `PGPASSWORD` pour ce même serveur. La recette crée des bases nommées par UUID. La migration Workspaces précède Chat. Le catalogue SQL et les deux listes d’IDs EF sont comparés aux références signées.

Définir `ALYCIA_RERANK_DIRECTORY` avec les deux fichiers du modèle Horizon à la révision `23139fec6f1f448cdf89a5ac2d2c6bf5e8c7b398`. Les empreintes et les commandes de récupération sont dans la [recette ONNX de référence](../decouplage/finition/D29-commandes.md). La suite de transport HTTP utilise un serveur Ollama contrôlé. Le contrôle de conteneurs requiert un moteur Docker et Docker Compose v2.24.4 ou ultérieur.

## Lancer l’application

Après le bootstrap, suivre la configuration locale du README, puis lancer `make demo-api` et `make demo-client`. Pour le DevHost de démonstration avec inférence déterministe, utiliser les commandes suivantes sous Linux.

```sh
ASPNETCORE_ENVIRONMENT=Development ALYCIA_MOCK_INFERENCE=true dotnet run --project src/Alycia.DevHost --no-launch-profile
```

Le test de production exerce les refus au démarrage et examine les DLL ainsi que le manifeste de l’API publiée. Le maître est le seul lieu de composition des implémentations appartenant à des domaines différents.

## Schéma et évolution

Le propriétaire fonctionnel propose la migration et les invariants. Workspaces conserve son contexte et ses migrations. Le maître intègre les évolutions du modèle Chat partagé dans Alycia.Persistence.Migrations avec les projets de modèle explicitement référencés. Chaque changement de schéma exige validation des contraintes et test des transactions sur une base jetable. Les migrations historiques, leurs snapshots et leurs identifiants sont immuables. Une séparation future des données exige une opération transactionnelle distincte.

## Versions, intégration et retour

Pour changer un package, modifier la version chez son propriétaire, documenter son API et recalculer les verrous concernés avec `dotnet restore --force-evaluate --configfile NuGet.Config` après construction de la nouvelle version. Relever ses empreintes puis actualiser le manifeste et les références exactes des consommateurs. La recette courante refuse tout autre contenu à version identique. La surface publique initiale est dans `modules/contracts/tests/Alycia.Contracts.Tests/Fixtures/public-api.txt`.

Lors de la future subdivision, exporter l’historique propre à chaque chemin en conservant la table de déplacements pour les anciens chemins. Substituer une révision Git immuable à chacun des douze dossiers dans un seul commit du maître. Documenter alors les URL et SHA réels. Pour la mise à jour suivante, choisir une révision revue, mettre à jour sa référence et ses packages ensemble, puis lancer les deux recettes avant la PR. Un retrait avant intégration consiste à revenir à `develop`. Après intégration, une PR de retour rétablit ensemble les sources, les références et leurs verrous (`git revert` du commit d’intégration, ou `git revert -m 1` si c’est un commit de fusion). Aucune migration inverse n’est nécessaire pour ce lot.

Pour obtenir dès maintenant un instantané de la seule révision d’Inference et de son entrée de compilation, exécuter depuis le maître après le bootstrap. Le répertoire affiché contient aussi les résultats locaux du contrôle.

```sh
export_domaine=$(mktemp -d)
git archive HEAD:modules/inference | tar -x -C "$export_domaine"
python3 "$export_domaine/scripts/check.py" check --feed "$PWD/artifacts/local-feed"
printf '%s\n' "$export_domaine"
```

Après la subdivision future et l’enregistrement des douze gitlinks dans le maître, les opérations locales ci-dessous récupèrent les révisions épinglées puis préparent un changement pour Inference. Définir `REVISION_MODULE` sur le SHA complet déjà revu dans son futur dépôt. Inclure dans la même PR les mises à jour de version, de verrous et de manifeste qui s’appliquent à ce changement.

```sh
git submodule update --init --recursive
git -C modules/inference fetch origin "$REVISION_MODULE"
git -C modules/inference switch --detach "$REVISION_MODULE"
git add modules/inference
python3 scripts/modules.py bootstrap-packages
python3 scripts/modules.py check-extraction
python3 scripts/modules.py check-master
```

Pour une tête signée par le mainteneur, renseigner sa clé de signature Git enregistrée sur GitHub. Exécuter `bash scripts/architecture/preparer-signature.sh BASE TETE ARBRE ../alycia-signee develop prepare/extraction-multidepots-p0-p8` avec les trois SHA de la PR. Le script crée une branche locale distincte et vérifie chaque arbre. Vérifier la signature auprès de GitHub après l’envoi fast-forward de cette nouvelle branche.
