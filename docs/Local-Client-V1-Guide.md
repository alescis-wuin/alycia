# Alycia — utiliser et vérifier le client local V1

Branche : `feature/local-client-v1-20261116`. Version de travail du 16 novembre 2026.
Voir le [plan et l'audit](Local-Client-V1-Plan.md) et le [compte rendu](Local-Client-V1-Report.md).

## 1. Installation sur CachyOS / Linux, depuis fish ou Bash

Prérequis : Git, SDK **.NET 10**, Docker Engine démarré avec Docker Compose,
GNU Make, Bash, Python **3.11 ou supérieur**, session graphique et Ollama local.
Le client .NET n'utilise pas Python/PyTorch pour l'inférence : Ollama gère le modèle.
Le SDK est sélectionné par `global.json` dans la famille stable 10.0.

Vérifier les outils :

```sh
git --version
dotnet --list-sdks
docker compose version
docker info
python3 --version
ollama --version
```

Pour installer les composants manquants, utiliser les instructions officielles :
[SDK .NET](https://dotnet.microsoft.com/en-us/download/dotnet/10.0),
[Docker](https://docs.docker.com/engine/install/),
[Ollama sous Linux](https://docs.ollama.com/linux).
La première compilation et le premier lancement téléchargent des dépendances.
Les échanges peuvent ensuite fonctionner hors ligne avec un modèle déjà installé.

Créer un clone séparé pour conserver intact un éventuel checkout existant :

```sh
git clone --branch feature/local-client-v1-20261116 https://github.com/alescis-wuin/alycia.git alycia-local-v1
cd alycia-local-v1
bash scripts/check.sh
```

Sans connexion de test PostgreSQL, ce contrôle signale explicitement les tests
de base de données sautés. Ce résultat seul ne valide pas la persistance.

Lister les modèles disponibles :

```sh
ollama list
curl --fail http://127.0.0.1:11434/api/tags
```

Si le service n'est pas déjà démarré, lancer `ollama serve` dans un terminal dédié.
Installer au préalable le modèle local souhaité avec `ollama pull NOM_DU_MODELE`.
Remplacer ce nom par celui d'un modèle choisi dans le catalogue Ollama ; ne pas
sélectionner une variante cloud. Alycia ne télécharge pas de modèle à l'envoi.

Dans un premier terminal, depuis le dossier du clone :

```sh
make demo-api
```

Dans un second terminal, depuis le même dossier :

```sh
make demo-client
```

Ces commandes fonctionnent depuis fish : Make lance les scripts Bash nécessaires.
Le lancement crée `.env` avec un secret aléatoire **seulement s'il n'existe pas**,
démarre PostgreSQL, applique les migrations et construit l'API Docker. Conserver
ce fichier : changer son mot de passe ne change pas celui d'un volume existant.
Sur une installation déjà utilisée, effectuer d'abord une sauvegarde avant la
migration. Les migrations ajoutées sont additives ; ne pas revenir à un ancien
schéma avec une commande de suppression.

| Composant Linux | Adresse / stockage |
|---|---|
| Client Avalonia | Application native sur le poste |
| API de conversation | `http://127.0.0.1:5187` |
| Ollama natif | `http://127.0.0.1:11434` |
| PostgreSQL | Port local `5433`, volume Compose `postgres_data` |
| Fichiers importés | Volume Compose `chat_files` |
| Journaux API | Volume Compose `api_logs` |
| Brouillons | Dossier `LocalApplicationData/Alycia`, fichier `drafts.json` |

Sous Linux, les brouillons se trouvent habituellement dans
`$XDG_DATA_HOME/Alycia` ou `~/.local/share/Alycia`. La variable
`ALYCIA_CLIENT_STATE_DIRECTORY` permet de choisir un autre dossier. Sous Windows,
ils sont normalement sous `%LOCALAPPDATA%\Alycia`. Le fichier est en clair ;
les permissions Unix sont limitées à l'utilisateur. Une seule instance de client
par dossier de brouillons est prévue.

Le nom du projet Compose détermine ses volumes. Un clone dans un autre dossier
crée généralement un projet différent et ne récupère pas automatiquement les
anciennes conversations. Utiliser une restauration isolée ou réutiliser
intentionnellement le projet existant après sauvegarde ; ne pas supprimer ses volumes.

## 2. Conversation et travail de développement

1. Choisir un modèle dans la liste, saisir le titre et l'instruction système,
   puis créer une conversation. Les profils intégrés couvrent les usages général,
   développement, revue et conception. La bibliothèque permet d'ajouter, modifier
   et supprimer des profils personnels.
2. Dans **Réglages**, définir le modèle, l'instruction, les notes persistantes,
   la fenêtre de contexte, la réserve de sortie, le nombre d'échanges récents,
   la température, la graine facultative et le délai. Un profil charge son texte
   dans la conversation : sa modification ultérieure ne réécrit pas les échanges.
3. Mettre dans les notes les contraintes stables du projet : environnement,
   conventions, périmètre et décisions validées. Le modèle peut se tromper ; ces
   notes sont éditées par l'utilisateur, sans synthèse automatique cachée.
4. Importer les fichiers utiles ou un petit dossier de sources. Dans **Documents**,
   choisir **Fenêtre**, **Épinglé** ou **Exclu**. L'original reste téléchargeable.
5. Saisir la question, puis ouvrir **Contexte** avant l'envoi : l'aperçu indique
   les messages et documents préparés, le budget et les messages omis.
6. Envoyer. La réponse apparaît progressivement. **Annuler** interrompt la
   requête ; une réponse incomplète n'est pas enregistrée comme échange terminé.
   L'état final confirme la persistance. En cas d'échec réseau, le brouillon et
   son identifiant permettent de réessayer sans doubler un échange déjà enregistré.
7. Copier le texte ou les blocs de code, appliquer les changements dans un
   dépôt séparé, exécuter ses tests puis fournir les résultats à Alycia.
   Le client n'exécute pas lui-même le code généré.
8. **Dupliquer** crée une conversation indépendante avec ses documents.
   **Régénérer** reprend la dernière question dans une copie, en conservant
   intégralement la conversation originale.

La recherche porte sur le titre et le contenu des messages enregistrés. Les
conversations sont paginées par 50 ; les messages par 100. Charger les éléments
plus anciens n'augmente pas à lui seul le contexte envoyé au modèle.

### Réglages et limites

| Réglage | Valeur par défaut | Limite / effet |
|---|---:|---|
| Fenêtre du modèle | 8 192 | 1 024 à 131 072 ; doit convenir au modèle et à la mémoire disponible |
| Sortie réservée | 1 024 | Au moins 1 ; sortie + marge strictement inférieures à la fenêtre |
| Marge interne | 256 | Minimum 128 ; conservée par l'interface, configurable via API |
| Échanges récents | 12 | 0 à 100 paires complètes, puis réduction selon le budget |
| Température | 0,7 | 0 à 2 ; saisir un point décimal dans le formulaire |
| Délai Linux | 180 s | 1 à 600 s ; le lanceur Windows initialise 300 s |
| Graine | Absente | Optionnelle ; aucune garantie d'identité entre matériels ou versions |
| Message utilisateur | — | 32 000 caractères |
| Fichier / lot | — | 25 Mio / 100 Mio, au plus 100 entrées par import |
| Conversation : fichiers | — | 500 fichiers ou 1 Gio |
| Texte extrait par fichier | — | Au plus 200 000 octets UTF-8 ; extraction parfois partielle |
| Archive de conversation JSON | — | 5 Mio et 2 000 messages |

Sur la RTX 4070 Laptop 8 Go, commencer par une fenêtre de 4 096 à 8 192 et une
sortie de 512 à 1 024, puis mesurer avec le modèle choisi. Ce sont des valeurs de
départ, pas une garantie de débit ou de placement intégral en VRAM. `ollama ps`
permet de vérifier ce qu'Ollama a chargé. Augmenter la fenêtre consomme davantage
de mémoire ; aucun benchmark de ce matériel n'a été réalisé pour cette livraison.

L'estimation du contexte repose sur les octets UTF-8 et une marge, **pas sur le
tokenizer exact du modèle**. Elle est volontairement prudente et peut retenir
peu d'historique. Les anciens échanges restent dans PostgreSQL. Les documents
reçoivent un budget plafonné ; « épinglé » signifie prioritaire, pas garanti
intégralement présent. Les notes et instructions occupent elles aussi ce budget.

Les imports de projets ignorent notamment `.git`, `.venv`, `node_modules`,
`bin`, `obj`, `.env*`, `.ssh`, `.aws` et les clés usuelles. Ce filtrage n'est pas
un détecteur de secrets. Une archive originale conservée peut toujours contenir
ses fichiers sensibles même si leur texte est exclu du contexte. Vérifier les
sources avant import. Les documents sont signalés au modèle comme des données
non fiables ; cela ne rend pas un modèle immunisé aux injections de prompt.

## 3. Vérifications sur la machine

Contrôles de code :

```sh
bash scripts/check.sh
python3 -m unittest discover -s scripts/tests -p '*_tests.py' -v
```

Le premier script restaure les dépendances et l'outil EF, compile, contrôle les
espaces des fichiers modifiés/stagés, vérifie l'absence de migration oubliée et
exécute les tests .NET. Les rapports TRX sont placés dans `artifacts/tests`.
Les tests Python vérifient les protections de sauvegarde avec Docker simulé.

Pour tester la persistance, fournir une connexion PostgreSQL **de test** dont
l'utilisateur possède `CREATEDB`, par exemple une chaîne au format
`Host=127.0.0.1;Port=5433;Database=postgres;Username=alycia;Password=...`.
Ne pas utiliser un service de production. Les tests créent et suppriment leurs
propres bases aux noms aléatoires ; ils n'effacent pas la base `alycia`.

Depuis fish, saisir la chaîne sans l'inscrire dans l'historique du shell :

```fish
read --silent --prompt-str 'Connexion PostgreSQL de test : ' ALYCIA_CHAT_TEST_CONNECTION
set -gx ALYCIA_CHAT_TEST_CONNECTION $ALYCIA_CHAT_TEST_CONNECTION
bash scripts/check.sh --integration
python3 scripts/tests/http-integration.py
set -e ALYCIA_CHAT_TEST_CONNECTION
```

Depuis Bash :

```bash
read -r -s -p 'Connexion PostgreSQL de test : ' ALYCIA_CHAT_TEST_CONNECTION
export ALYCIA_CHAT_TEST_CONNECTION
bash scripts/check.sh --integration
python3 scripts/tests/http-integration.py
unset ALYCIA_CHAT_TEST_CONNECTION
```

La recette HTTP lance une vraie API, applique les migrations sur une base isolée
et utilise un serveur Ollama **simulé**. Elle ne demande aucun modèle ni GPU.
Le JSON final doit indiquer cinq groupes de contrôles réussis ; vérifier aussi
le code de sortie du programme, notamment pour confirmer le nettoyage final.

Avec l'API démarrée et un vrai modèle déjà installé :

```sh
python3 scripts/verify-local.py --model "NOM_EXACT_RETOURNE_PAR_OLLAMA_LIST"
```

Cette recette crée ses conversations et profils temporaires, vérifie les accès,
réglages, flux, rejeu, conflit, fichiers, recherche, import/export et duplication,
puis tente de supprimer uniquement ses données de test. Elle effectue une vraie
génération et peut donc durer le temps de chargement du modèle.

Recette graphique manuelle : créer deux conversations, conserver un brouillon
différent dans chacune, fermer/rouvrir le client, vérifier les brouillons ; envoyer
un message long puis annuler ; reprendre une conversation après redémarrage de
l'API ; comparer l'aperçu de contexte avec et sans un fichier épinglé ; essayer
la copie de code et la régénération dans une copie. Vérifier l'affichage et le
presse-papiers sur KDE/Wayland. Ce sont les essais restant à réaliser sur le poste.

## 4. Export, sauvegarde et restauration isolée

| Action | Contenu | Limite |
|---|---|---|
| Export JSON dans le client | Messages, modèle, instruction, notes, réglages | Pièces jointes non incluses ; import dans une nouvelle conversation |
| Export Markdown | Texte lisible et partageable | Pas de réimport Markdown ; pas de pièces jointes |
| Dupliquer | Historique sélectionné, réglages et copies des documents | Dans la même installation |
| Sauvegarde Compose | Base PostgreSQL complète et volume de fichiers | N'inclut ni `.env`, ni modèles Ollama, ni brouillons du poste, ni logs |

Depuis la racine du dépôt, avec PostgreSQL démarré :

```sh
python3 scripts/backup.py create
python3 scripts/backup.py verify backups/NOM_DU_DOSSIER_CREE
python3 scripts/backup.py restore backups/NOM_DU_DOSSIER_CREE --project alycia-restore-validation
```

La sauvegarde arrête brièvement `chat-api` si elle tourne puis la redémarre,
y compris après un échec du dump. Elle produit `database.dump`, `files.tar.gz`
et un manifeste SHA-256 dans un dossier privé. Terminer les générations en cours
avant cette opération. Les fichiers de sauvegarde sont en clair : conserver
séparément et de façon protégée `.env` et les brouillons si nécessaire.

La restauration refuse tout nom de projet déjà associé à un conteneur ou volume.
Elle utilise des volumes neufs, un port PostgreSQL libre sous Linux, vérifie les
archives, restaure puis affiche les comptes de conversations/messages/fichiers.
Elle ne démarre pas l'API restaurée et ne remplace pas les volumes d'origine.
Le conteneur PostgreSQL de restauration reste présent pour inspection. Cette
procédure est un exercice de récupération isolé ; elle n'effectue pas une bascule
automatique de l'installation active. Son exécution Docker réelle reste à valider
sur la machine de l'utilisateur.

## 5. Windows et dépannage

Le lanceur Windows existant utilise Docker Desktop, PostgreSQL et un Ollama
conteneurisé, avec jeton local entre le client et l'API. Voir le
[guide Windows](Windows-Prototype.md) pour le bootstrap et le téléchargement
du modèle. La configuration fournie est une base CPU ; l'accélération GPU n'est
pas configurée par cette livraison.

```powershell
.\scripts\demo.ps1 -Action All -Model "NOM_DU_MODELE"
.\scripts\check.ps1
# Avec une connexion de test déjà définie :
.\scripts\check.ps1 -Integration
# Python 3.11+ est nécessaire uniquement aux recettes et sauvegardes Python :
python scripts\backup.py create --windows
```

Pour vérifier/restaurer une sauvegarde Windows, employer les mêmes commandes
Python ; ajouter `--windows` à `restore`. La recette réelle `verify-local.py`
lit `ALYCIA_CHAT_ACCESS_TOKEN` si défini : utiliser l'environnement configuré
par le lanceur, sans publier le jeton. Les nouveaux parcours Windows n'ont pas
été exécutés sur Windows dans cet audit.

| Symptôme | Vérification / action |
|---|---|
| Liste de modèles vide | Vérifier `ollama list` et `/api/tags` ; les modèles cloud sont exclus |
| PostgreSQL refuse le mot de passe | Faire correspondre `.env` au volume existant ; ne pas le supprimer |
| API inaccessible | Contrôler le terminal `make demo-api`, le port 5187 et `/healthz` |
| Réponse trop lente | Vérifier `ollama ps`, réduire contexte/modèle, augmenter le délai dans la limite de 600 s |
| « Conversation a changé » | Recharger ; une autre action a modifié la révision, le brouillon reste disponible |
| Contexte trop volumineux | Réduire question, notes ou instruction ; inspecter l'aperçu |
| Flux interrompu | Recharger puis réessayer le brouillon conservé ; ne pas supposer la réponse partielle enregistrée |
| Brouillons illisibles | Sauvegarder le fichier existant pour diagnostic ; le client ne l'écrase pas |

Les routes de conversation sont réservées au mode Development explicitement
activé, avec contrôle du pair réseau, de l'origine et de l'hôte. Ne pas publier
ces services sur Internet ou le LAN : aucune authentification multi-utilisateur
complète n'est livrée. Les journaux d'erreur sont dans `api_logs` ; retirer les
informations sensibles avant de les partager.
