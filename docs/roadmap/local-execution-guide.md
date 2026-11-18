# Alycia — exécution et utilisation après les cinq phases

17 novembre 2026 · Branche `feature/local-client-v4-context-quality-20261117`.
Cible : votre MSI sous CachyOS/KDE, terminal fish, RTX 4070 Laptop 8 Go et 32 Go de RAM.
Les commandes ci-dessous fonctionnent dans fish et Bash ; les scripts `.sh` sont appelés explicitement avec Bash.

## 1. Préparer ou mettre à jour le dépôt

Prérequis : Git, SDK .NET 10, Python 3, Docker avec Compose, Ollama local et au moins
un modèle génératif déjà installé. Ce projet C# n'utilise pas d'environnement Python uv.

```sh
dotnet --info
docker compose version
python3 --version
ollama list
```

Pour une nouvelle copie, si `/developpement/seynax/Alycia` n'existe pas :

```sh
mkdir -p /developpement/seynax
git clone --branch feature/local-client-v4-context-quality-20261117 https://github.com/alescis-wuin/alycia.git /developpement/seynax/Alycia
cd /developpement/seynax/Alycia
```

Pour votre copie existante : fermer le client, attendre la fin des générations,
vérifier `git status` et conserver vos modifications locales avant toute mise à jour.
Si votre installation Compose contient déjà des conversations, sauvegarder avec
PostgreSQL démarré. Cette commande arrête brièvement l'API si nécessaire, puis la relance :

```sh
python3 scripts/backup.py create --output backups
```

La sauvegarde contient la base, les originaux et les dérivés du volume de fichiers,
dont les points de reprise. Après sauvegarde :

```sh
git fetch origin
git switch feature/local-client-v4-context-quality-20261117
git pull --ff-only origin feature/local-client-v4-context-quality-20261117
git log -5 --oneline
```

Un refus de fast-forward exige d'examiner vos commits locaux ; ne pas forcer leur effacement.
Ces cinq phases n'ajoutent aucune migration SQL. Les migrations déjà présentes sont
appliquées normalement au démarrage. Les index dérivés incompatibles sont reconstruits.

## 2. Compiler et contrôler

Depuis la racine du dépôt :

```sh
bash scripts/check.sh
python3 -m unittest discover -s scripts/tests -p '*_tests.py' -v
python3 scripts/evaluate-context.py validate
python3 scripts/evaluate-context.py validate --suite evaluation/context-v2
```

`check.sh` restaure les dépendances, compile, contrôle le format et le modèle EF,
puis lance les tests. Sans `ALYCIA_CHAT_TEST_CONNECTION`, quatre tests PostgreSQL
sont ignorés explicitement. Cela ne valide pas votre serveur de base de données.

Pour les tests d'intégration, utiliser un serveur **de test** et un rôle disposant de
`CREATEDB`. Les tests créent et suppriment uniquement leurs bases isolées. Exemple
à adapter ; ne pas utiliser un identifiant de production :

```sh
env ALYCIA_CHAT_TEST_CONNECTION='Host=127.0.0.1;Port=5432;Database=postgres;Username=ROLE_TEST;Password=SECRET_TEST' bash scripts/check.sh --integration
env ALYCIA_CHAT_TEST_CONNECTION='Host=127.0.0.1;Port=5432;Database=postgres;Username=ROLE_TEST;Password=SECRET_TEST' python3 scripts/tests/http-integration.py
```

La seconde commande utilise l'API réelle et PostgreSQL avec un fournisseur Ollama
simulé. Elle n'évalue ni votre GPU ni la qualité d'un modèle. Éviter d'enregistrer
un vrai secret dans l'historique du terminal ; passer la variable depuis votre
mécanisme local habituel de gestion des secrets.

## 3. Démarrer sur CachyOS

Ollama doit écouter localement sur `127.0.0.1:11434`. Si son service fonctionne déjà,
ne pas lancer une seconde instance. Sinon, dans un terminal distinct :

```sh
ollama serve
```

Dans le terminal du dépôt :

```sh
bash scripts/demo.sh
```

Ce script ne prend pas d'argument : il crée `.env` seulement s'il manque, démarre
PostgreSQL, vérifie son authentification, applique les migrations et lance l'API
conteneurisée au premier plan. L'API écoute sur `127.0.0.1:5187`, PostgreSQL est
publié sur `127.0.0.1:5433`. Les fichiers sont dans le volume persistant `chat_files`.
Ne pas remplacer le mot de passe d'une base existante en régénérant `.env`.

Dans un autre terminal :

```sh
cd /developpement/seynax/Alycia
env ALYCIA_API_BASE_URL=http://127.0.0.1:5187 dotnet run --project src/Alycia.Client.Avalonia
```

Cette commande lance le client Avalonia. Le protocole Linux Compose utilise le
réseau hôte ; les procédures Windows existantes restent dans les guides antérieurs.
Le rendu KDE/Wayland et votre pilote NVIDIA n'ont pas été testés dans l'environnement
de développement de cette livraison.

## 4. Régler une conversation

Choisir le nom exact fourni par `ollama list`, puis vérifier **Réglages** de la
conversation active : le sélecteur de création ne change pas une conversation existante.

| Réglage | Point de départ | Conséquence |
| --- | --- | --- |
| Recherche | `lexical` | BM25 local, aucun modèle d'embeddings nécessaire |
| Fenêtre | 8192 | Même budget de référence ; ne garantit pas que tout modèle tient en VRAM |
| Sortie | 1024 pour la recette | Désactiver la génération jusqu'à arrêt pour une limite comparable |
| Outils | Activés si le modèle annonce `tools` | Accès ciblé aux sources et à l'historique |
| Mémoire | Résumé automatique et registre structuré | Deux derniers échanges protégés ; anciennes informations récupérables |
| Compteur | `auto` en usage, `conservative` pour une comparaison fixée | Sans tokenizer compatible, le compteur reste conservateur |
| Appels d'outils | 24 par défaut, 12 pour les campagnes proposées | Arrêt explicite si la limite est atteinte |

La place prise par les neuf outils est comptée dans la fenêtre. Avec de longs
messages protégés et le compteur conservateur, 8192 peut être insuffisant : réduire
le périmètre ou le message, désactiver les outils pour ce tour, ou changer la fenêtre
pour l'usage interactif. Pour un A/B, garder la même fenêtre dans les deux runs.

Alycia sérialise désormais ses requêtes de génération et d'embeddings dans un même
processus d'API. Cette limite ne contrôle pas d'autres clients Ollama ni les modèles
qu'Ollama garde chargés. La consommation VRAM dépend du modèle, de sa quantification
et de son contexte ; elle n'a pas été mesurée sur votre RTX 4070.

## 5. Importer, explorer et obtenir des preuves

Utiliser **+ Dossier** ou **+ Fichiers / ZIP**. Les originaux sont conservés ; les
exclusions et limites d'archives restent appliquées. Modes : `project` pour la
recherche à la demande, `pinned` pour inclure explicitement une source, `excluded`
pour l'écarter. Une source épinglée peut être fournie sans correspondance lexicale.

Exemple de demande :

> Recherche les déclarations de Store.ReadAsync, lis leur implémentation et leurs
> tests. Cite les passages utilisés, distingue les homonymes et indique les fichiers
> qui restent à consulter.

| Outil | Fonction |
| --- | --- |
| `project_list` | Inventaire paginé |
| `project_search` | Recherche littérale dans les originaux |
| `project_read` | Lecture par identifiant, position ou symbole |
| `project_search_passages` | Passages BM25 classés, scores nuls écartés |
| `project_find_symbols` | Déclarations C# |
| `project_find_references` | Usages syntaxiques C#, homonymes possibles |
| `project_read_many` | Une à quatre lectures, plafond global et erreurs par entrée |
| `history_search` | Recherche dans les messages originaux de cette conversation |
| `history_read` | Lecture de la paire utilisateur/assistant, avec provenance |

Les outils restent en lecture seule. Aucun shell, compilation du code importé ou
accès à une autre conversation. Les autres langages utilisent un découpage textuel.
Une erreur de budget sur une lecture groupée se traite en réduisant le nombre de
lectures ou en lisant un symbole précis. Suivre les offsets pour poursuivre.

Dans les détails d'une réponse, examiner les sources fournies, citées, inconnues,
les empreintes, les plages, le coût des appels et les omissions. Une référence `[S…]`
reconnue prouve la présence d'un extrait dans le contexte, pas la vérité d'une phrase.

## 6. Analyser un projet et reprendre une interruption

1. Importer les documents, puis ouvrir **Analyse du projet**.
2. Choisir tous les documents ou un lot d'import. Vérifier le budget d'appels dans
   **Réglages** ; il doit couvrir les fichiers et les réductions intermédiaires.
3. Lancer **Analyser tous les fichiers**. Le libellé lance l'analyse du périmètre
   choisi. La progression indique inventaire, fichiers, réutilisations et réduction.
4. Après **Arrêter**, erreur ou redémarrage, revenir dans cette fenêtre, sélectionner
   l'exécution interrompue et cliquer **Reprendre**.
5. Ouvrir le rapport, son manifeste et `tree.txt`. Vérifier fichiers complets,
   partiels, exclus et sans texte. Activer la synthèse si elle doit guider la recherche.

Un point de reprise est enregistré après chaque fichier terminé et pour l'inventaire.
Un fichier interrompu peut être recalculé en entier ; la réduction finale est relancée.
Les unités terminées compatibles sont réutilisées. Une modification du périmètre,
du modèle ou du budget peut rendre la reprise incompatible : démarrer alors une
nouvelle analyse, qui réutilise les unités encore compatibles. Les options/version/SHA
font partie des clés. Sans digest de modèle, les résumés ne sont pas réutilisés.

La synthèse activée coexiste avec les originaux pertinents. Son texte d'orientation
est plafonné au quart du budget de sources ; elle n'atteste pas une lecture exhaustive.
PDF : repères de pages ; DOCX : blocs/tableaux ; XLSX : cellules, formules et valeurs
XML disponibles ; PPTX : diapositives. Office reste marqué partiel. Pas d'OCR,
d'exécution de formules/macros, de reconstruction garantie de mise en page ou de notes.

Limite : 50 rapports SQL et 50 métadonnées d'exécution par conversation. Supprimer
un rapport dans l'UI ne supprime pas les métadonnées de reprise. Leur retrait explicite
est disponible par `DELETE /api/chat/conversations/{id}/analysis-runs/{runId}` ;
cette action retire uniquement la métadonnée d'exécution, pas les originaux ni les
unités réutilisables. Il n'y a pas encore de bouton dédié dans l'UI.

## 7. Décisions anciennes, corrections et audit

Exemples :

> Décision [stockage]: utiliser PostgreSQL pour les conversations.

> Correction [stockage]: utiliser PostgreSQL seulement pour les métadonnées ; les
> originaux restent dans le stockage local.

> Retrouve ma décision initiale et sa correction dans l'historique. Cite les deux
> messages originaux et distingue ma décision de tes propositions.

La recherche historique est bornée à 4096 messages ou 4 millions de caractères par
scan, puis paginable. La limite du registre de 256 entrées n'efface pas les originaux.
Les références `[Hséquence:empreinte]` portent rôle, date et SHA. Les variantes de clé
avec séparateurs équivalents sont regroupées ; les synonymes et contradictions
implicites ne sont pas résolus automatiquement.

L'audit final est heuristique et non bloquant : `direct_quote` = texte retrouvé,
`indirect_lexical` = recouvrement lexical, `uncertain` = à vérifier,
`not_demonstrated` = fondement non établi. Au plus 64 blocs sont contrôlés ; le nombre
omis est indiqué. Même une citation directe peut rapporter une affirmation fausse.

## 8. Vérifier votre modèle et comparer à contexte égal

Avec l'API démarrée et un modèle compatible outils :

```sh
python3 scripts/verify-local.py --model "NOM_DU_MODELE" --extended --tools
```

Cette recette crée puis supprime ses propres conversations. Elle lance de vraies
générations locales. Si le modèle ne propose pas `tools`, omettre `--tools` ; les
fonctions historiques et de lecture autonome ne seront alors pas validées.

Campagne de développement, même modèle et paramètres :

```sh
python3 scripts/evaluate-context.py run --suite evaluation/context-v2 --model "NOM_DU_MODELE" --label legacy --retrieval legacy --counter conservative --memory structured --context 8192 --output-tokens 1024 --tools --calls 12 --repeats 3 --output artifacts/evaluation/p05-legacy
python3 scripts/evaluate-context.py run --suite evaluation/context-v2 --model "NOM_DU_MODELE" --label lexical --retrieval lexical --counter conservative --memory structured --context 8192 --output-tokens 1024 --tools --calls 12 --repeats 3 --output artifacts/evaluation/p05-lexical
python3 scripts/evaluate-context.py compare artifacts/evaluation/p05-legacy/run.json artifacts/evaluation/p05-lexical/run.json > artifacts/evaluation/p05-comparison.json
```

Les scénarios longs créent chacun 142 échanges avant la question finale. Prévoir
un temps de calcul conséquent. Un rappel attendu absent produit un échec à examiner.
Les dossiers de sortie doivent être nouveaux. Pour la validation finale, refaire
les deux runs avec `--split holdout` et d'autres dossiers, sans changer les fixtures.
Pour `hybrid`, fournir `--embedding-model "NOM_EMBEDDINGS_LOCAL"` déjà installé et
conserver tous les autres paramètres. Le plafond de 2 048 passages a été supprimé
et l’indexation est désormais asynchrone : attendre son état terminé avant de
comparer la couverture sémantique. Voir le [guide d’indexation](../context-budget-indexing.md).

`legacy` compare des modes du moteur livré ; ce n'est pas un retour complet à V4.
Pour mesurer V4 contre cette livraison, conserver un checkout distinct de
`0b31f13c76a8e8075d798f76c30cc2f3438e9b0e`, avec base, volumes et ports distincts,
et utiliser le même pilote d'évaluation contre les deux URL. Ne pas partager les
caches ni modifier la base active pour cette comparaison.

Examiner les faits corrects/manquants, contradictions, affirmations sans fondement,
références et plages. Les métriques automatiques de récupération sont au niveau
**fichier**, pas passage exact ; les regex ne valent pas jugement de vérité.
Additionner les coûts de l'aperçu, de l'historique préparatoire et de la génération.
Les runs conservent les temps d'import et d'aperçu, les coûts par appel et le premier
fragment de réponse lorsqu'observé. Le temps total d'un scénario long inclut sa préparation.

Benchmarks CPU reproductibles, sans Ollama :

```sh
dotnet run --project benchmarks/Alycia.ContextBenchmarks -c Release -- artifacts/evaluation/cpu-benchmark.json
dotnet run --project benchmarks/Alycia.ContextBenchmarks -c Release -- --documents evaluation/context-v2/corpus artifacts/evaluation/documents-benchmark.json
```

## 9. Arrêt, sauvegarde et retour arrière

Fermer le client, puis `Ctrl+C` dans le terminal de `demo.sh` arrête l'API au premier
plan. Pour arrêter aussi PostgreSQL sans supprimer ses données :

```sh
docker compose -f compose.yaml -f compose.demo.yaml stop postgres
```

Relancer `demo.sh` pour repartir. Ne pas utiliser `down -v` pour un redémarrage.
Vérifier une sauvegarde existante avec :

```sh
python3 scripts/backup.py verify backups/NOM_DU_DOSSIER
```

Le script de restauration existant exige un nouveau projet `alycia-restore-*` et
refuse d'écraser un projet déjà présent. Pour examiner l'ancienne version sans
toucher au checkout courant :

```sh
git worktree add --detach ../Alycia-v4-reference 0b31f13c76a8e8075d798f76c30cc2f3438e9b0e
```

Ne pas lancer cette copie avec les mêmes volumes/ports que l'installation active.
Le retour de code ne restaure pas automatiquement vos données : conserver la
sauvegarde complète. Aucun downgrade EF n'est nécessaire pour les cinq phases.
