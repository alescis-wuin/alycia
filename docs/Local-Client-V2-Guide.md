# Alycia V2 — mise à jour, utilisation et recette

> Pour la version actuelle de cette branche, suivre le
> [guide V3](Local-Client-V3-Guide.md). Il remplace notamment les indications de
> migration, de lecture intégrale des sources et les nombres de contrôles ci-dessous.

Branche conservée : `feature/local-client-v1-20261116`.
Cette version ajoute l’interface réorganisée, le Markdown progressif, la génération
jusqu’au signal d’arrêt, la mémoire récursive et les synthèses de dossiers/archives.
Le [plan](Local-Client-V2-Plan.md) décrit les lots ; le
[compte rendu](Local-Client-V2-Report.md) distingue les essais réalisés et restants.
Les [correctifs de sélection, d’import et de rendu](Local-Client-V2-Fixes.md)
complètent cette version, sans migration supplémentaire.

## 1. Mettre à jour l’installation V1 fonctionnelle

Sur CachyOS/Linux, depuis fish ou Bash, rester dans **le même clone** afin de
conserver le projet Compose et ses volumes. Terminer les générations et fermer
le client. Avec PostgreSQL démarré, sauvegarder avant les migrations :

```sh
git status --short
python3 scripts/backup.py create
git switch feature/local-client-v1-20261116
git pull --no-rebase --ff-only origin feature/local-client-v1-20261116
bash scripts/check.sh
```

Si Git signale des modifications locales ou refuse l’avance rapide, conserver ces
modifications et résoudre leur intégration ; ne pas employer `reset --hard`.
Conserver `.env` et les volumes existants. La sauvegarde arrête brièvement l’API
puis la relance ; ses fichiers sont en clair, à conserver dans un emplacement privé.

Arrêter l’ancien lancement de l’API, puis lancer dans deux terminaux séparés :

```sh
make demo-api
```

```sh
make demo-client
```

`make demo-api` applique deux nouvelles migrations additives, puis reconstruit
l’API. Les anciennes conversations et pièces jointes restent présentes. Le schéma
Chat possède désormais cinq migrations. Les métadonnées inconnues des anciens
imports restent inconnues : la mise à jour ne reconstitue pas les dates de source.

Pour une première installation : SDK .NET 10, Git, Docker Compose, Make, Bash,
Python 3.11+ et Ollama local avec au moins un modèle installé sont nécessaires.
Suivre les prérequis du [guide V1](Local-Client-V1-Guide.md), puis ce guide V2.
Un nouveau clone dans un autre dossier ne retrouve pas automatiquement les volumes
Compose de l’installation existante.

Sur Windows, conserver le même clone et sa configuration : sauvegarder avec
`python scripts\backup.py create --windows`, mettre à jour la même branche,
exécuter `.\scripts\check.ps1`, puis
`.\scripts\demo.ps1 -Action All -Model "NOM_DU_MODELE"`.
Le [guide Windows](Windows-Prototype.md) reste applicable au lancement.

## 2. Lire et piloter une conversation

1. Choisir le modèle, puis **Nouvelle conversation**. Le titre et l’instruction
   initiale sont dans le volet dépliable juste dessous.
2. **Modèle actif**, au-dessus du fil, indique le modèle de la conversation ouverte
   et ouvre ses **Réglages**. Le sélecteur de gauche sert uniquement à créer la
   prochaine conversation. Les listes proposent les modèles locaux annonçant la
   capacité Ollama `completion` ; les modèles réservés aux embeddings sont exclus.
   Les réglages contiennent aussi le système, les notes persistantes et les
   paramètres. La bibliothèque de prompts se trouve dans **Prompts et outils**.
3. Saisir la demande ; **Ctrl+Entrée** envoie. **Ctrl+N** crée une conversation,
   **Ctrl+F** place le curseur dans la recherche, **Échap** interrompt l’opération.
4. La réponse se construit dans le fil. Titres, emphase, listes, citations, code
   et tableaux sont interprétés pendant la réception. Un bloc de code incomplet
   reste lisible avant la réception de sa clôture Markdown. Les réponses ont une
   largeur de lecture limitée ; les tableaux suivent l’alignement de leurs colonnes.
5. **Copier le code** copie le bloc concerné ; **Tout le code** copie les blocs
   du message ; **Copier** conserve le texte source, avec son Markdown. Les actions
   du message sont accessibles dans son en-tête.
6. Remonter dans le fil suspend le suivi automatique. **Revenir à la réponse**
   revient en bas. Le bouton **☰** affiche/masque l’historique, automatiquement
   replié lorsque la fenêtre devient étroite.
7. Duplication, régénération d’une copie, export et suppression sont regroupés
   dans **Autres actions**. La recherche porte sur les échanges enregistrés.

Les liens HTTP/HTTPS ne s’ouvrent qu’au clic. Les images distantes sont représentées
par leur description/URL, sans téléchargement automatique. Le HTML n’est pas
exécuté. La coloration syntaxique, les formules mathématiques et Mermaid ne sont
pas interprétés dans cette version. Les très gros tableaux ou réponses passent
en texte brut au-delà des limites du rendu ; le contenu source reste accessible.

## 3. Génération continue et arrêt

**Générer jusqu’au signal d’arrêt** est activé par défaut, y compris pour les
anciennes configurations JSON ne comportant pas ce réglage. Ollama reçoit
`num_predict=-1`. La réserve de sortie continue de servir au calcul du contexte ;
elle n’impose plus un plafond de génération lorsque cette option est active.

Le modèle indique sa fin par `done=true` et `done_reason=stop` : le token EOS
n’est normalement pas envoyé comme texte. Une fin `length`, inconnue ou absente
n’est pas présentée comme une réponse terminée en mode continu. En désactivant
l’option, la réserve de sortie redevient également la limite de génération.

Le délai configuré, de 1 à 600 secondes, mesure l’**inactivité du flux**, et se
renouvelle à chaque ligne reçue. Une réponse active peut donc durer plus longtemps.
**Arrêter**, une panne, une limite de ressources ou une absence prolongée de données
peuvent toujours interrompre la génération. Un garde-fou d’un million de caractères
borne le texte reçu, raisonnement compris. La réponse partielle n’est pas
enregistrée comme échange terminé ; le brouillon permet de réessayer.

Les résumés utilisent volontairement des générations bornées pour respecter leur
budget, même quand la réponse conversationnelle est en mode continu.

## 4. Mémoire récursive et budget de contexte

**Résumer automatiquement les échanges anciens** est activé par défaut. À chaque
envoi, le client prépare le contexte suivant :

| Élément | Traitement |
|---|---|
| Prompt système et notes persistantes | Toujours conservés intégralement |
| Quatre derniers messages terminés | Deux échanges user/assistant conservés intégralement |
| Demande en cours | Conservée intégralement |
| Échanges plus anciens | Conservés bruts tant qu’ils tiennent, puis résumés avec la mémoire précédente |
| Documents / synthèse active | Ajoutés dans le budget restant, avec priorité aux éléments épinglés |

Les grands textes sont découpés sans casser les caractères Unicode. Le modèle
résume les fragments, puis leurs résumés sont regroupés et réduits récursivement.
Le nombre d’appels est borné : **256 par opération** par défaut, réglable de 8
à 2 048. Une réduction qui ne converge pas ou dépasse son budget échoue clairement.
L’historique original en base n’est ni remplacé ni effacé.

La mémoire dérivée conserve sa couverture, son modèle et une empreinte chaînée des
messages résumés. Elle est réutilisée aux envois suivants. Changer de modèle
invalide cette mémoire. Lors d’un envoi ordinaire, la nouvelle mémoire et l’échange
ne sont enregistrés qu’après une génération réussie, dans la même transaction.

Dans **Mémoire et contexte** :

1. Rédiger d’abord la prochaine demande pour obtenir un aperçu pertinent.
2. Consulter le budget, la mémoire et sa couverture. L’aperçu indique si une
   synthèse reste nécessaire ; sa simple ouverture ne lance pas le modèle.
3. **Préparer le résumé** effectue les appels nécessaires et enregistre explicitement
   la mémoire sans envoyer la demande. Cette action est facultative : l’envoi
   prépare automatiquement le contexte si le réglage est activé.
4. **Réinitialiser la mémoire** supprime seulement le résumé dérivé. Les messages
   restent présents et pourront être résumés à nouveau.

Si système, notes, quatre derniers messages et demande dépassent **à eux seuls**
le budget, l’envoi est refusé sans troncature de ces éléments. Augmenter la fenêtre
si le modèle/matériel le permettent, réduire les notes/réserve, ou commencer une
nouvelle conversation avec une synthèse choisie.

L’estimation repose sur les octets UTF-8 et une marge, **pas sur le tokenizer exact**.
Elle est prudente mais ne garantit ni la fidélité d’un résumé ni la capacité du
modèle à traiter toute fenêtre demandée. Sur la RTX 4070 Laptop 8 Go, conserver
pour commencer les réglages déjà fonctionnels ; 4 096–8 192 de contexte constituent
un point de départ à mesurer, pas une promesse de performance.

## 5. Résumer un dossier ou une archive

1. Importer via **+ Dossier** ou **+ Fichiers / ZIP**. Les archives ZIP, TAR et GZip
   peuvent contenir d’autres archives dans la limite de profondeur prévue.
   Pour un dossier, choisir **Inventaire complet** pour inclure tous les fichiers
   réguliers dans les limites d’import, ou **Sources utiles** pour ignorer avant
   import les chemins techniques et sensibles habituels.
2. Ouvrir **Documents** pour choisir les fichiers exclus ou prioritaires avant
   l’analyse. Les originaux restent téléchargeables.
3. Ouvrir **Synthèses de dossiers** et choisir tous les documents ou un lot importé.
4. **Télécharger tree.txt** permet d’inspecter l’inventaire avant tout appel au modèle.
5. Choisir le budget final, par défaut 2 048 octets UTF-8 estimés, puis
   **Créer une synthèse**. Le suivi indique la lecture de l’arbre, les fichiers
   traités et le nombre d’appels ; **Arrêter** annule l’opération.
6. Ouvrir le résultat et exporter la synthèse Markdown, `tree.txt` et la provenance
   JSON. Cette dernière relie chaque fichier à ses métadonnées, son statut et
   son résumé, ou au motif expliquant pourquoi son contenu n’a pas été résumé.

`tree.txt` contient une arborescence avec chemin, taille en octets, extension,
SHA-256 calculé sur l’original reçu, création et modification de source si connues,
provenance des dates, date d’import, statut d’extraction, mode de contexte et ID.
`!/` matérialise l’intérieur d’une archive. Une date manquante est marquée
**indisponible** ; les dates ZIP restent des heures locales sans fuseau connu.
Les dates de dossier sont rapportées par le système du client, pas certifiées.

Le modèle lit **l’arbre intégral par fragments**, puis le texte extrait de chaque
fichier admissible ; des réductions successives produisent la synthèse finale.
Le fichier `tree.txt` complet reste exportable même s’il dépasse le contexte du
modèle. Les noms et métadonnées des entrées exclues figurent dans cet inventaire,
mais leur contenu n’est pas envoyé au modèle.

Une seule synthèse est active par conversation. Elle remplace dans le contexte
les extraits des fichiers de son périmètre ; les autres fichiers restent éligibles.
Un ajout, une suppression ou un changement d’exclusion peut rendre la synthèse
périmée : elle est alors signalée et automatiquement écartée du contexte. La
relancer pour l’actualiser. Supprimer une analyse conserve ses fichiers sources.
Supprimer une archive retire également toutes ses entrées imbriquées.

### Périmètre et limites d’import

| Limite | Valeur / conséquence |
|---|---|
| Import | 500 entrées au maximum ; archive et entrées comptent |
| Taille | 25 Mio par fichier ; 100 Mio par lot, octets compressés et déployés cumulés |
| Conversation | 500 fichiers et 1 Gio cumulés |
| Archives | Quatre niveaux, racine comprise ; profondeur excessive refusée |
| Dossier du poste | Parcours limité à 16 niveaux, sans suivi des liens symboliques |
| Extraction textuelle | 200 000 octets UTF-8 par fichier au maximum ; certains formats/PDF partiels |
| Synthèse finale | 512 à 16 384 octets UTF-8 ; au plus 50 analyses conservées par conversation |
| Contexte documentaire | Budget restant seulement ; une synthèse/extrait peut n’être inclus qu’en partie |

L’inventaire couvre **les fichiers réellement importés**, pas tout le disque.
En mode **Sources utiles**, le parcours de dossier ignore notamment `.git`, `.venv`,
`node_modules`, `bin`, `obj`, `.env*`, `.ssh`, `.aws` et les clés usuelles ; leurs
descendants ne figurent pas dans `tree.txt`. En mode **Inventaire complet**, ces
fichiers sont conservés localement et inventoriés, mais leur contenu reste exclu
de l’IA. Les dossiers vides ne sont pas inventoriés. Un contenu binaire non extractible
reste inventorié et conservé, sans résumé inventé. Une extraction partielle est
signalée dans la provenance. Le filtrage n’est pas un détecteur exhaustif de secrets.

Pour un projet dépassant ces limites, importer des sous-dossiers ciblés et analyser
un lot à la fois. L’analyse ne lit pas spontanément le disque, n’exécute aucun
fichier et n’effectue aucune modification de code.

## 6. Tester la mise à jour sur le poste

```sh
bash scripts/check.sh
python3 -m unittest discover -s scripts/tests -p '*_tests.py' -v
```

Sans `ALYCIA_CHAT_TEST_CONNECTION`, les tests PostgreSQL sont explicitement ignorés.
Pour les activer, utiliser une base de test et un utilisateur avec `CREATEDB` :

```fish
read --silent --prompt-str 'Connexion PostgreSQL de test : ' ALYCIA_CHAT_TEST_CONNECTION
set -gx ALYCIA_CHAT_TEST_CONNECTION $ALYCIA_CHAT_TEST_CONNECTION
bash scripts/check.sh --integration
python3 scripts/tests/http-integration.py
set -e ALYCIA_CHAT_TEST_CONNECTION
```

Sous Bash, saisir la variable avec `read -r -s`, puis `export`, et la retirer avec
`unset`. Ne pas la placer en clair dans l’historique. La recette HTTP isolée
applique les migrations, lance une vraie API et simule Ollama ; elle attend
**9 groupes réussis** puis nettoie ses bases/données de test, dont le parcours de
réparation d’une ancienne conversation avec modèle d’embeddings. Vérifier aussi son
code de sortie pour confirmer le nettoyage.

Avec l’installation démarrée et un **vrai modèle Ollama** déjà présent :

```sh
ollama list
python3 scripts/verify-local.py --model "NOM_EXACT_DU_MODELE" --extended
```

Cette commande crée uniquement des données synthétiques, effectue plusieurs
générations locales et supprime ses propres conversations/profils à la fin.
La recette avec un vrai modèle conserve huit groupes ; le neuvième groupe de la
recette isolée utilise un modèle d’embeddings simulé.
Le nombre d’appels dépend du modèle et de sa capacité à réduire les textes.
Sans `--extended`, la recette V1 à cinq groupes reste disponible. Un échec de
convergence peut révéler un modèle peu adapté au résumé ou un budget trop serré.
Sous Windows, fournir également `ALYCIA_CHAT_ACCESS_TOKEN` depuis la configuration
du lanceur, sans le publier.

Recette manuelle complémentaire :

| Parcours | Résultat attendu |
|---|---|
| Demander titres, liste, tableau et code | Mise en forme pendant le flux ; copie de chaque bloc correcte |
| Demander `:rocket:`, `👩🏽‍💻`, `🇫🇷`, `❤️`, `1️⃣` et `&amp;` | Émojis et entité interprétés hors code ; séquences composées intactes |
| Remonter pendant une longue réponse | Position de lecture conservée ; bouton de retour disponible |
| Réduire la fenêtre à environ 960 px | Historique replié ; saisie et actions accessibles |
| Générer longtemps puis interrompre | Arrêt explicite, brouillon conservé, aucun faux échange terminé |
| Dépasser le contexte avec plusieurs échanges | Résumé visible, deux derniers échanges et demande intacts |
| Importer un petit dossier/ZIP imbriqué connu | Arbre, tailles et SHA-256 concordants ; dates absentes explicites |
| Exclure un fichier après une synthèse | Résultat signalé périmé, absent du contexte actif |
| Fermer puis rouvrir client/API | Historique, mémoire, analyses et brouillons retrouvés |

Les sauvegardes PostgreSQL complètes incluent mémoires et analyses. L’export JSON
de conversation et la duplication ne transfèrent pas les analyses dérivées ; les
recréer dans la nouvelle conversation. Les originaux joints ne font toujours pas
partie de l’export JSON. Voir la procédure de sauvegarde/restauration isolée du
[guide V1](Local-Client-V1-Guide.md#4-export-sauvegarde-et-restauration-isolée).

## 7. Ollama répond en terminal mais Alycia reçoit HTTP 400

Comparer le modèle indiqué dans **Modèle actif** avec celui de `ollama run`.
La sélection de gauche ne modifie pas une conversation déjà créée. Dans le cas
signalé, la conversation utilisait `embeddinggemma:latest`, tandis que l’essai
réussi en terminal utilisait `qwen3:4b-instruct`. EmbeddingGemma produit des vecteurs
via l’API d’embeddings et ne peut pas générer une réponse conversationnelle.

Pour reprendre une conversation existante : ouvrir **Modèle actif** ou **Réglages**,
choisir `qwen3:4b-instruct` s’il est installé, enregistrer puis renvoyer le brouillon.
Le correctif vérifie `/api/show` avant chaque génération, y compris pour les résumés
et les anciennes conversations. Les noms seuls ne servent pas à classifier les
modèles : un modèle d’embeddings renommé reste exclu. Si Ollama ne fournit pas les
capacités, une erreur demande de mettre Ollama à jour, sans supposer la compatibilité.

Pour récupérer le correctif dans le clone existant :

```sh
git status --short
git pull --no-rebase --ff-only origin feature/local-client-v1-20261116
```

`--no-rebase` neutralise une éventuelle configuration Git imposant le rebasage.
Si des modifications locales empêchent l’intégration, les conserver et examiner
le conflit ; ne pas employer `reset --hard`. Relancer ensuite `make demo-api`
et `make demo-client` dans deux terminaux après avoir arrêté leurs anciens processus.
Ce correctif ne comporte pas de nouvelle migration.

Si HTTP 400 persiste avec **le même modèle génératif actif**, relever le refus
exact d’Ollama avec cette requête locale :

```sh
curl --fail-with-body http://127.0.0.1:11434/api/chat \
  -H 'Content-Type: application/json' \
  -d '{"model":"qwen3:4b-instruct","messages":[{"role":"user","content":"Bonjour"}],"stream":false}'
```

Un autre refus peut venir des paramètres du modèle ; ce test sert alors à séparer
la sélection du modèle de la configuration de génération. Le journal transmis
pour le défaut initial montrait des réponses HTTP d’Ollama, mais pas le corps JSON
de son erreur : aucun contenu de ce corps n’a été supposé ou reconstitué.

Références : [EmbeddingGemma dans Ollama](https://ollama.com/library/embeddinggemma),
[capacité des modèles via `/api/show`](https://github.com/ollama/ollama/blob/main/docs/api.md#show-model-information).

## 8. Import de dossier ou ZIP refusé

Le validateur accepte désormais les chemins relatifs tels que
`./projet/src/./notes.json`, `projet//src/notes.json` et les séparateurs Windows.
Ils deviennent `projet/src/notes.json`. Les espaces, accents et émojis des noms
restent conservés. Les entrées de répertoire ZIP utilisant `\` sont ignorées
comme les autres répertoires vides.

Les chemins absolus, lecteurs Windows, URI, caractères de contrôle et segments
`..` restent refusés. Deux entrées devenant identiques après normalisation font
échouer le lot entier. Les limites de taille et de profondeur restent applicables.

En cas de refus, le message précise le chemin concerné et la règle en cause.
Réessayer **+ Dossier** et **+ Fichiers / ZIP** après avoir relancé **l’API et le
client**. Le test HTTP couvre des chemins relatifs et des ZIP imbriqués, mais pas
les archives originales du signalement : si le problème persiste, conserver le
message complet et préparer une petite archive reproductible sans contenu privé.

## 9. Émojis et lisibilité des réponses

Le rendu reconnaît les codes d’émojis connus de Markdig, par exemple `:rocket:`
et `:smile:`, ainsi que les entités `&amp;` et `&#x1F98A;`. La ponctuation des
smileys comme `:)` n’est pas transformée automatiquement. Le contenu du code et
la copie du message restent fidèles au texte source.

Les émojis Unicode composés sont conservés en un seul groupe de caractères :
couleurs de peau, personnes avec métier, drapeaux, cœurs et touches chiffrées.
Les codes Markdown incomplets sont réinterprétés à mesure que le flux arrive.
Le rendu utilise les polices d’émojis présentes sur le poste ; aucune police
n’est téléchargée à l’ouverture d’une conversation.

Sous CachyOS/Arch, si les émojis apparaissent comme des carrés ou sont absents :

```sh
fc-match emoji
sudo pacman -S --needed noto-fonts-emoji
```

Relancer le client après installation. La couleur et les séquences Unicode
récentes dépendent de la version de la police. Référence du paquet :
[noto-fonts-emoji](https://archlinux.org/packages/extra/any/noto-fonts-emoji/).
