# Alycia V3 — sources de projet, outils et MCP

Branche : `feature/local-client-v1-20261116`.
Le [plan](Local-Client-V3-Plan.md) décrit les quatre lots et le
[compte rendu](Local-Client-V3-Report.md) précise les validations et limites.
Les fonctions de conversation, Markdown, émojis et mémoire du
[guide V2](Local-Client-V2-Guide.md) restent disponibles.

## 1. Mettre à jour l’installation existante

Sur Linux, depuis fish ou Bash, utiliser **le même clone** pour retrouver les
volumes Compose existants. Terminer les générations et fermer le client.
Avec PostgreSQL démarré, sauvegarder puis mettre à jour :

```sh
git status --short
python3 scripts/backup.py create
git switch feature/local-client-v1-20261116
git pull --no-rebase --ff-only origin feature/local-client-v1-20261116
bash scripts/check.sh
```

Si Git signale des modifications locales ou une divergence, conserver ces
modifications avant de résoudre leur intégration. Ne pas utiliser `reset --hard`.
Conserver `.env` et les volumes. Les sauvegardes contiennent les conversations et
les fichiers en clair ; leur création arrête brièvement l’API puis la relance.

Arrêter l’ancien lancement de l’API, puis lancer dans deux terminaux :

```sh
make demo-api
```

```sh
make demo-client
```

Le lancement applique la migration
`20261117071032_ProjectToolsAndPersistentSources`. Le schéma Chat passe de cinq
à **six migrations**. Elle ajoute les traces aux messages et fait passer les
anciens fichiers de dossiers/archives encore en mode `window` au mode persistant
`project`. Les exclusions et les fichiers épinglés sont conservés.

Pour une première installation, suivre les prérequis du
[guide V1](Local-Client-V1-Guide.md) : SDK .NET 10, Git, Docker Compose, Make, Bash,
Python 3.11+ et Ollama avec un modèle local. Un autre dossier de clone ne retrouve
pas automatiquement les volumes de l’installation précédente.

Sous Windows, conserver également le clone et les volumes, sauvegarder avec
`python scripts\backup.py create --windows`, mettre à jour la même branche,
exécuter `.\scripts\check.ps1`, puis
`.\scripts\demo.ps1 -Action All -Model "NOM_DU_MODELE"`.
Voir le [guide Windows](Windows-Prototype.md) pour les prérequis.

## 2. Comprendre les trois usages des fichiers

| Usage | Ce qui est envoyé au modèle | Couverture |
|---|---|---|
| Extraits initiaux | Sélection répartie entre les fichiers, avec priorité aux épingles et au code pertinent pour la demande | Partielle ; visible dans **Mémoire et contexte** |
| Outils de lecture | Inventaire, recherche textuelle et portions des originaux demandées par le modèle | Sélective ; actions et pages lues conservées avec la réponse |
| **Analyse du projet** | Arbre complet puis toutes les sources textuelles admissibles du périmètre, par fragments et résumés récursifs | Mesurée dans le manifeste ; exclut explicitement les contenus indisponibles ou exclus |

Importer un projet ne signifie donc pas placer tout son contenu dans chaque
prompt. Une analyse systématique fournit une base globale ; les outils permettent
ensuite de revenir aux implémentations pour une question précise.

## 3. Importer et explorer le code

1. Importer avec **+ Dossier** ou **+ Fichiers / ZIP**. Les formats et limites
   d’archives du guide V2 restent applicables.
2. Dans **Documents**, vérifier les fichiers présents et leurs modes :
   **Projet** reste disponible au fil de la conversation ; **Épinglé** priorise
   les extraits ; **Fenêtre** suit les échanges récents ; **Exclu** interdit le
   contenu au modèle, y compris via MCP.
3. Dans **Réglages**, choisir un modèle Ollama annonçant la capacité `tools`,
   activer **Autoriser les outils de lecture des sources importées**. Le garde-fou
   de nombre d’appels est réglable de 0 à 4 096 ; 0 désactive ce quota. Les budgets
   de contexte, de sortie et de durée restent actifs. Voir le [guide actualisé](context-budget-indexing.md).
4. Utiliser une fenêtre d’au moins **8 192 tokens** pour les outils, davantage si
   le système, les derniers échanges ou le raisonnement occupent beaucoup de place.
   Les anciennes conversations gardent leurs propres réglages ; augmenter leur
   fenêtre ou désactiver les outils si le budget est insuffisant.
5. Ouvrir **Sources et outils** pour vérifier la capacité du modèle, les comptes
   de fichiers et l’adresse MCP de la conversation.
6. Poser une demande précise, par exemple :

> Cherche ChatService dans les sources importées. Lis son implémentation et les
> tests associés avec les outils, puis explique comment le contexte est construit.
> Cite les chemins et indique ce que tu n’as pas lu.

La réponse affiche les outils employés. Les lectures indiquent chemin, SHA-256
et plage de caractères. **Texte source intégral accessible** signifie que le
lecteur dispose de l’original : cela ne signifie pas que le modèle en a parcouru
toutes les pages. Les traces restent disponibles après rechargement et dans
l’export JSON de la conversation.

Le modèle choisit ses appels : la présence de `tools` ne garantit pas qu’il les
utilise correctement. Un modèle sans cette capacité conserve les extraits et
l’analyse systématique. Un modèle d’embeddings ne peut pas générer une réponse.

Le chat garde les protections de la mémoire V2 lorsque le résumé automatique
est activé : système, notes, deux derniers échanges et demande courante. Les
anciens résultats d’outils sont résumés séparément. Une limite d’appels, un budget
impossible ou une annulation interrompt clairement l’opération ; un échange
partiel n’est pas enregistré comme réponse terminée. Les statistiques de génération
affichées concernent le dernier appel, pas le coût cumulé de tous les outils/résumés.

## 4. Analyser systématiquement un dossier ou ZIP

1. Vérifier les exclusions dans **Documents**.
2. Ouvrir **Analyse du projet**, choisir tous les documents ou un lot importé.
3. **Télécharger tree.txt** permet d’inspecter l’inventaire avant génération.
4. Choisir le budget final, de 512 à 16 384 octets UTF-8 estimés, puis
   **Analyser tous les fichiers**. Ce bouton parcourt le périmètre sélectionné.
5. Exporter la synthèse, `tree.txt` et la provenance `analysis.json`.

Le parcours lit les **originaux textuels complets**, y compris la partie située
après la limite historique d’extraction de 200 000 octets. Les fichiers UTF-8 et
UTF-16 avec BOM sont pris en charge dans les limites d’import. Pour les PDF et
documents bureautiques, le lecteur utilise l’extraction existante ; une extraction
partielle reste signalée. Il n’y a pas d’OCR ajouté.

Le modèle reçoit l’arbre par fragments, puis les fichiers et leurs métadonnées.
Pour le code, les consignes portent sur les symboles, responsabilités, dépendances,
flux et tests effectivement observés. La réduction hiérarchique produit une
synthèse ; les sources originales restent conservées.

Le manifeste de version 2 donne notamment :

| Champ de couverture | Signification |
|---|---|
| `inventoriedFiles` | Nombre d’entrées importées inventoriées |
| `eligibleFiles` / `summarizedFiles` | Sources textuelles admissibles / traitées |
| `fullyReadFiles` / `partialFiles` | Sources complètes / extractions partielles |
| `codeFiles` / `codeFilesSummarized` | Code inventorié / code traité |
| `excludedFiles` / `noTextFiles` | Contenus exclus / sans texte exploitable |
| `allEligibleTextRead` | Toutes les sources textuelles admissibles ont été lues intégralement |

Chaque résumé de fichier conserve aussi son origine, le nombre de caractères et
d’octets UTF-8 lus. La couverture atteste le parcours des entrées admissibles,
pas la justesse ou l’exhaustivité intellectuelle du résumé généré.

Les anciennes analyses ne sont pas recalculées automatiquement. Les relancer pour
obtenir la nouvelle lecture intégrale et le manifeste de couverture. Une analyse
active remplace les extraits de son périmètre, mais laisse les originaux accessibles
aux outils. Les changements de fichiers/exclusions rendent l’analyse périmée.

Les limites restent : 500 entrées, 25 Mio par fichier, 100 Mio par lot, 1 Gio par
conversation, quatre niveaux d’archives et 16 niveaux de parcours de dossier.
Les entrées d’archives comptent dans ces plafonds. **Sources utiles** filtre certains
dossiers avant import ; ils ne figurent donc pas dans l’inventaire. **Inventaire
complet** les conserve sous réserve des limites, avec exclusions de contenu.
Le filtrage des chemins sensibles ne détecte pas tous les secrets possibles.

Une grosse analyse nécessite plusieurs appels : budget de résumé par défaut 256,
réglable de 8 à 2 048. Réduire le périmètre ou augmenter ce budget si nécessaire.
Un original manquant, un décodage invalide ou une réduction impossible produit une
erreur explicite, sans prétendre avoir terminé l’analyse.

## 5. MCP local

Le client Alycia utilise réellement le transport MCP pour exécuter les appels
natifs Ollama. L’URL affichée dans **Sources et outils** suit cette forme :

```text
http://127.0.0.1:5187/api/chat/conversations/IDENTIFIANT/mcp
```

Le serveur expose `project_list`, `project_search` et `project_read`, uniquement
sur les imports de cette conversation. Les identifiants de fichiers remplacent
les chemins libres. Les exclusions et la désactivation des outils sont revérifiées
à chaque appel. Les métadonnées des fichiers exclus restent inventoriées.

Le transport est **Streamable HTTP**, JSON-RPC 2.0, sans session persistante ni
flux serveur : POST répond en JSON, GET/DELETE répondent 405. Protocoles acceptés :
`2025-11-25`, `2025-06-18`, `2025-03-26`. Un client doit initialiser la connexion,
envoyer `notifications/initialized`, puis demander `tools/list` / `tools/call`.
En-têtes : `Content-Type: application/json`,
`Accept: application/json, text/event-stream` et
`MCP-Protocol-Version: 2025-11-25` après négociation.

La liste et la recherche sont paginées : poursuivre `nextOffset` jusqu’à `null`,
y compris après une page de recherche sans résultat. Les offsets de lecture sont
des positions UTF-16, les budgets des tailles UTF-8. Réutiliser les offsets renvoyés
plutôt que de les recalculer. Une recherche fournit au plus un résultat par fichier.

En mode conteneur, un client MCP externe doit fournir le même jeton local que le
client graphique via `Authorization: Bearer …`. L’accès reste soumis aux contrôles
locaux existants. Ce serveur ne doit pas être exposé publiquement.

Ce lot ne fournit ni gestionnaire de serveurs MCP tiers, ni accès arbitraire au
disque, ni écriture de fichier, ni exécution de commandes.

## 6. Tester

Contrôles locaux et compilation :

```sh
bash scripts/check.sh
```

Les tests PostgreSQL sont ignorés sans `ALYCIA_CHAT_TEST_CONNECTION`. Utiliser
un serveur de test et un rôle autorisé à créer des bases pour les activer.
Depuis fish, sans mettre la connexion dans l’historique :

```fish
read --silent --prompt-str 'Connexion PostgreSQL de test : ' ALYCIA_CHAT_TEST_CONNECTION
set -gx ALYCIA_CHAT_TEST_CONNECTION $ALYCIA_CHAT_TEST_CONNECTION
bash scripts/check.sh --integration
python3 scripts/tests/http-integration.py
set -e ALYCIA_CHAT_TEST_CONNECTION
```

Sous Bash, saisir avec `read -r -s ALYCIA_CHAT_TEST_CONNECTION`, puis `export`
avant les contrôles et `unset` après. La recette HTTP crée une base isolée et
simule Ollama ; **13 groupes** sont attendus, avec nettoyage des données de test.

Avec l’API et le vrai Ollama démarrés, choisir le nom exact d’un modèle installé
annonçant `tools`, puis lancer :

```sh
python3 scripts/verify-local.py --model "NOM_EXACT_DU_MODELE" --extended --tools
```

Cette recette attend **12 groupes** et crée puis supprime ses conversations.
Elle vérifie notamment qu’un appel natif lit effectivement du code situé après
200 000 octets, ainsi que traces, export, rejeu et révocation. Un refus d’utiliser
les outils est un échec explicite, même si une réponse textuelle est produite.
Elle lance plusieurs générations et résumés ; sans `--tools`, le contrôle conserve
la recette classique compatible avec un modèle de génération sans outils.

Terminer sur le poste par un essai de dossier réel : repérer une fonction connue,
demander sa lecture, vérifier les chemins/plages de la trace et comparer la
synthèse aux sources. Mesurer RAM/VRAM et temps de réponse avec les réglages choisis.
