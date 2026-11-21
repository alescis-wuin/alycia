# Alycia Boréale sur CachyOS

Pour les nouveaux réglages de budgets, les imports configurables et l’indexation PostgreSQL/pgvector, consulter le [guide contexte et indexation](../context-budget-indexing.md).

## Préparer le poste

Les commandes se lancent depuis la racine du dépôt, sous fish ou Bash. Il faut Git, Bash, Python 3.11 ou ultérieur, le SDK .NET 10, Docker avec Compose et un modèle génératif installé dans Ollama. Les polices Luciole et Hack sont embarquées dans le client. Le backend, PostgreSQL et les originaux des documents restent sur la machine.

```shell
dotnet --version
docker compose version
docker info --format '{{.ServerVersion}}'
python3 --version
ollama list
```

Si Ollama indique que le serveur est inaccessible, lancer `ollama serve` dans un terminal dédié. Si la liste est vide, installer le modèle de son choix avec `ollama pull NOM_DU_MODELE`. Un modèle d’embeddings seul ne permet pas de converser.

## Récupérer la branche

Pour une première installation, le chemin ci-dessous est un exemple adapté à ton arborescence.

```shell
mkdir -p /developpement/seynax
cd /developpement/seynax
git clone --branch feature/local-client-v4-context-quality-20261117 git@github.com:alescis-wuin/Alycia.git Alycia
cd Alycia
```

Pour une installation existante, utiliser le répertoire habituel afin de retrouver le même projet Compose et les mêmes volumes. Vérifier d’abord les changements locaux avec `git status --short --branch`. Sur une copie de travail propre, exécuter ensuite les commandes suivantes.

```shell
git fetch origin
git switch feature/local-client-v4-context-quality-20261117
git pull --ff-only origin feature/local-client-v4-context-quality-20261117
git log -1 --oneline
```

## Mettre à jour des données existantes

Le démarrage de l’API applique les migrations. **Avec des conversations existantes, faire la sauvegarde avant de lancer `demo.sh`.** L’outil sauvegarde la base et le volume des originaux. Il suspend brièvement l’API Compose si elle tourne, puis la redémarre. Fermer auparavant le client et terminer les générations en cours. Conserver également le fichier `.env` de l’installation et le dossier d’état du client si les brouillons doivent être sauvegardés.

1. Démarrer le PostgreSQL de l’installation existante s’il est arrêté.
2. Créer la sauvegarde et relever le chemin exact affiché.

```shell
docker compose -f compose.yaml -f compose.demo.yaml up -d --wait postgres
python3 scripts/backup.py create --output ../sauvegardes-alycia
```

3. Remplacer `CHEMIN_AFFICHE` par ce répertoire et répéter la migration sur une copie.

```shell
python3 scripts/backup.py verify CHEMIN_AFFICHE
bash scripts/recette-migration-boreale.sh CHEMIN_AFFICHE
```

La recette crée un projet Compose au nom unique et utilise un port PostgreSQL libre sur l’adresse locale. Elle compare par SHA-256 les conversations, messages, mémoires, métadonnées historiques et originaux de la copie avant et après migration. Elle lance ensuite la compilation, les tests .NET avec PostgreSQL et la recette HTTP Boréale avec un Ollama simulé. En cas d’échec, l’identifiant Compose affiché permet d’examiner la copie. Pour arrêter cette copie, utiliser `docker compose -p NOM_AFFICHE -f compose.yaml -f compose.demo.yaml stop`.

Pour revenir aux données antérieures, le script `python3 scripts/backup.py restore CHEMIN_AFFICHE --project alycia-restore-retour` crée une autre restauration isolée. Le retour de branche Git seul ne restaure pas les données. Le schéma refuse la rétrogradation lorsqu’il contient déjà des profils de projet.

## Compiler et démarrer

Sur une installation neuve, exécuter `bash scripts/check.sh`. Cette commande restaure les dépendances, compile, contrôle la migration EF et lance les tests. Sur une installation existante, la recette sur copie ci-dessus comprend déjà ces contrôles. En cas d’échec de compilation, conserver la première erreur et suspendre la mise à jour de la base principale.

**Terminal 1 — API locale**

```shell
bash scripts/demo.sh
```

Attendre que l’API écoute. Le script crée `.env` pour un nouveau dépôt, démarre PostgreSQL, applique les migrations Workspaces puis Chat et démarre l’API sur le port 5187. Docker et la restauration NuGet nécessitent l’accès aux dépendances au premier démarrage.

**Terminal 2 — client Avalonia**

```shell
cd /developpement/seynax/Alycia
env ALYCIA_API_BASE_URL=http://127.0.0.1:5187 dotnet run --project src/Alycia.Client.Avalonia
```

Adapter le `cd` si le dépôt est ailleurs. La variable `ALYCIA_API_BASE_URL` prime sur l’adresse enregistrée dans **Paramètres → Connexion et outils**. Pour arrêter l’application, fermer la fenêtre puis utiliser `Ctrl+C` dans le terminal de l’API. `docker compose -f compose.yaml -f compose.demo.yaml stop postgres` arrête également la base en conservant ses volumes.

## Premier parcours

1. **Choisir le projet.** Le sélecteur de gauche ouvre **Hors projet** ou un projet existant. Le bouton **+** crée un projet. L’historique et la recherche suivent cette sélection. Les anciennes conversations apparaissent dans **Hors projet**.
2. **Préparer les sources.** Dans **Documents**, choisir **Importer des fichiers ou archives** ou **Importer un dossier**. Le titre de la page indique la portée. Sélectionner une ligne pour voir son état d’extraction, son aperçu et son empreinte. Sélectionner plusieurs lignes pour modifier leur mode en une opération.
3. **Donner les règles communes.** Dans **Consignes**, saisir par exemple la langue des réponses, la convention de code ou la méthode d’analyse. Le modèle du projet sert aux nouvelles conversations. Les notes partagées peuvent être ajoutées, corrigées ou retirées.
4. **Converser.** Cliquer sur **Nouvelle conversation** puis saisir, par exemple, « Compare les responsabilités des deux fichiers et cite les passages qui fondent ta réponse. » Envoyer avec `Ctrl+Entrée`. Le même emplacement propose **Arrêter** pendant la génération. Une interruption affiche le texte provisoire reçu et conserve la demande à reprendre.
5. **Lire et vérifier.** Cliquer sur une citation documentaire reconnue ouvre le passage enregistré et l’empreinte de l’original. **Sources et détails** redonne accès aux extraits fournis, aux diagnostics et aux outils. Une source retirée ou une version indisponible est signalée lors de l’ouverture.
6. **Affiner.** Ouvrir l’inspecteur dans l’en-tête. **Contexte** prévisualise les messages et les extraits préparés pour le brouillon courant. **Mémoire** affiche la mémoire privée et permet de promouvoir une citation structurée en note partagée, avec sa provenance. Modifier le brouillon ou une source peut modifier le prochain contexte.

| Besoin | Action |
| --- | --- |
| Joindre un fichier pour un seul échange de travail | Trombone → Fichier, Dossier ou Archive. La pièce appartient à la conversation. |
| Rendre une pièce privée disponible aux autres conversations | Dans Documents, ouvrir la portée de la conversation, sélectionner la pièce puis **Partager avec le projet**. |
| Prioriser ou écarter une source | Sélectionner **Épingler** ou **Exclure** dans la portée souhaitée. L’épinglage reste soumis au budget. **Reprendre les réglages du projet** supprime la dérogation locale. |
| Lancer ou reprendre une analyse | **Documents → Analyses de cette conversation**, ou **Actions → Analyses**. Les résultats disposent de leur propre état et empreinte. |
| Changer le modèle d’une conversation | Cliquer sur le modèle actif. Le changement de modèle efface son résumé généré. Les options de budget, de recherche et d’outils se trouvent dans **Avancé**. |
| Rattacher un ancien fil | Dans **Hors projet**, ouvrir le fil puis **Actions → Changer de projet**. Les pièces privées et la mémoire suivent le fil. |
| Dupliquer, régénérer, renommer ou exporter | Ouvrir **Actions** dans l’en-tête. L’export JSON permet la réimportation dans **Hors projet**. Les dépendances partagées sont inventoriées et leurs octets restent dans la sauvegarde complète. |
| Choisir l’apparence | **Paramètres → Système / Aube / Nuit**, taille de lecture de 14 à 36, repli du code, puis **Appliquer**. |
| Travailler au clavier | `Ctrl+N` crée un fil, `Ctrl+F` ouvre la recherche, `Ctrl+Maj+F` bascule la concentration. `Échap` ferme le panneau ou le menu ouvert, puis peut arrêter une génération. |

## Code, mathématiques et copie

Pour obtenir une coloration, demander une clôture Markdown avec un identifiant comme `csharp`, `axaml`, `python`, `json`, `yaml`, `sql`, `bash`, `fish`, `powershell` ou `diff`. Pour un autre langage, le code reste lisible dans Hack. **Copier le code** récupère le texte du bloc avec son indentation. Le pictogramme de copie du message récupère le Markdown source complet, y compris les liens, tableaux et formules. La sélection à la souris est locale à chaque contrôle de texte.

Pour une formule dans une phrase, utiliser `$E=mc^2$`. Pour une équation isolée, écrire les deux délimiteurs sur leurs propres lignes.

```text
$$
\frac{a+b}{2}
$$
```

Cliquer sur une formule en ligne, ou utiliser **Copier le LaTeX** sous une équation. Les commandes inconnues et les expressions dépassant les limites de rendu peuvent s’afficher en TeX. Les images Markdown distantes apparaissent comme références textuelles, sans téléchargement automatique.

## Vérifier le rendu sur ton MSI

Les commandes suivantes produisent des captures de la fenêtre Avalonia headless et un relevé de lecture synthétique dans `artifacts/boreale`. Elles utilisent le résultat d’une compilation Debug réussie. Le relevé couvre une fenêtre de 1 000 messages chargés par pages et la construction Markdown de 100 000 caractères en quatre fragments. Le temps y est exprimé en millisecondes et la mémoire en octets.

```shell
mkdir -p artifacts/boreale
env ALYCIA_UI_CAPTURE="$PWD/artifacts/boreale/conversation.png" ALYCIA_UI_BENCHMARK="$PWD/artifacts/boreale/lecture.json" dotnet test tests/Alycia.Client.Avalonia.Tests --no-build --filter FullyQualifiedName~ChatWindowCreatesRenamesSendsRetainsFailedDraftAndDeletes
```

Dans ta session KDE Wayland, vérifier ensuite le clavier, la copie réelle, le suivi du thème système et l’affichage à ton facteur d’échelle. Tester une réponse en cours, remonter le fil, puis ouvrir la source d’une réponse plus ancienne. Si le thème du bureau ne remonte pas à Avalonia dans ta session, **Aube** et **Nuit** restent sélectionnables.

En cas d’erreur de connexion, vérifier `curl -fsS http://127.0.0.1:5187/healthz`, puis `ollama list`. Une erreur de mot de passe PostgreSQL sur une installation existante demande de rétablir les identifiants de son fichier `.env`. Les brouillons et préférences graphiques utilisent par défaut le dossier local d’Alycia du compte utilisateur. `ALYCIA_CLIENT_STATE_DIRECTORY` permet de choisir un autre répertoire pour une recette isolée.
