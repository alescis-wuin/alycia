# Reproduire la clôture

## Récupérer la révision

```sh
git fetch origin develop prepare/extraction-multidepots-p0-p8
git switch --detach 89f2ba67439c5c78c2817e277c261c64a8a413b5
git rev-parse HEAD 'HEAD^{tree}'
```

Le second SHA doit être `6cd494b4e756f947f58bf22da04dd8deeb91733a`. La branche de la PR ajoute le rapport et les preuves. Utiliser `git switch prepare/extraction-multidepots-p0-p8` pour travailler sur cette branche.

## Exécuter les recettes

Fournir les deux fichiers du modèle ONNX, Docker Compose et une instance PostgreSQL jetable munie de pgvector, avec les paramètres .NET et libpq indiqués dans le [guide](ASSEMBLAGE.md). Depuis Bash ou fish, à la racine du checkout, le même lanceur sélectionne le SDK 10.0.404.

```sh
bash scripts/with-sdk.sh dotnet --version
bash scripts/with-sdk.sh python3 scripts/modules.py bootstrap-packages
bash scripts/with-sdk.sh python3 scripts/modules.py check-extraction
bash scripts/with-sdk.sh python3 scripts/modules.py check-master
python3 scripts/architecture/test_identity.py artifacts/acceptance/trx.zip artifacts/extraction
```

Pour une seule solution, utiliser par exemple `bash scripts/with-sdk.sh python3 scripts/modules.py check-module inference`. Sous PowerShell, une fois le SDK épinglé disponible, employer `py -3 scripts/modules.py bootstrap-packages`, `py -3 scripts/modules.py check-extraction` et `py -3 scripts/modules.py check-master`. La validation native Windows est différée.

## Lire et vérifier les résultats

La commande maître appelle successivement la vérification Horizon, le bootstrap, la restauration verrouillée, les builds Debug/Release, les publications, le contrôle du Mock en production, les tests Python, les migrations sur base neuve, Roslyn/MSBuild, les tests .NET, les conformités, HTTP, OpenAPI, RAG et Docker Compose. La sortie contient `PASS check-master` après la suppression des ressources Compose. Les journaux de synthèse remplacent le chemin absolu du checkout par `$CHECKOUT` et celui de l’environnement temporaire par `$ENV`. Les TRX et la capture Roslyn sont les archives d’origine. Les journaux de présentation retirent les espaces de fin de ligne. Les trois flux texte intégraux sont disponibles dans [journaux-originaux.zip](preuves/journaux-originaux.zip).

```sh
git fetch origin refs/pull/54/head
git switch --detach FETCH_HEAD
bash -c 'cd docs/architecture/preparation-multidepots/preuves && sha256sum -c SHA256SUMS'
python3 scripts/architecture/test_identity.py docs/architecture/preparation-multidepots/preuves/trx.zip docs/architecture/preparation-multidepots/preuves/extractions
```

## Lancer Alycia

Après le bootstrap et la configuration locale de PostgreSQL et d’Ollama, lancer dans deux terminaux séparés.

```sh
bash scripts/with-sdk.sh make demo-api
bash scripts/with-sdk.sh make demo-client
```

## Signer la livraison

Relever dans la [PR #54](https://github.com/alescis-wuin/alycia/pull/54) la tête et l’arbre intégral du **commit final de preuves**. Avec une identité de signature déjà associée à son compte GitHub, exécuter depuis le clone. Remplacer les deux valeurs en capitales.

```sh
bash scripts/architecture/preparer-signature.sh fadd9aa41148ac3f147b25a73fb6d023928242c6 SHA_FINAL_PR ARBRE_FINAL_PR ../alycia-signee develop prepare/extraction-multidepots-p0-p8
git -C ../alycia-signee log --format='%h %G? %s' fadd9aa41148ac3f147b25a73fb6d023928242c6..HEAD
git -C ../alycia-signee push -u origin HEAD
```

Le script crée la branche locale `integration/alycia-signee-<12 premiers caractères de la tête>`, contrôle l’arbre de chaque commit et vérifie le diff final. Après envoi, vérifier les quatre commits recréés auprès de GitHub. La valeur attendue est `true valid` pour chacun. La branche signée peut alors être proposée vers `develop` en remplacement de la PR #54. Les SHA des commits recréés changent. Les publications natives API et Boréale intègrent le SHA applicatif dans leur version informative. Les empreintes de DLL documentées sont rattachées au commit testé. La décision de fusion revient au mainteneur.

```bash
git -C ../alycia-signee rev-list --reverse fadd9aa41148ac3f147b25a73fb6d023928242c6..HEAD |
while read -r sha; do
    gh api "repos/alescis-wuin/alycia/git/commits/$sha" --jq '[.verification.verified,.verification.reason] | @tsv'
done
```
