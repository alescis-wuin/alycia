# Contexte, outils et indexation persistante

Mise à jour du 18 novembre 2026, branche `feature/local-client-v4-context-quality-20261117`.

## Ce qui a changé

Les trois budgets sont séparés :

| Domaine | Fonctionnement |
|---|---|
| Corpus et embeddings | Traitement des sources admissibles par lots, en arrière-plan, sans quota global de passages. Vecteurs et progression persistés dans PostgreSQL. |
| Contexte envoyé au modèle | Fenêtre finie, partagée entre instructions, historique, mémoire, extraits, outils et réserve de réponse. |
| Sorties d’outils | Seuls les textes encore présents dans le contexte, avec leur mémoire réduite, consomment le budget des outils. Une sortie retirée ne reste pas débitée. |

L’ancien plafond de 2 048 passages en recherche hybride est supprimé. Le plafond de 8 millions de caractères pour les candidats documentaires devient un réglage, désactivé par défaut. Le nombre d’appels d’outils devient un garde-fou facultatif, distinct du budget en tokens. Le plafond de sortie par appel est également réglable.

L’allocation automatique réserve la place disponible après les messages protégés. Les résultats anciens sont réduits seulement sous pression. Le nombre d’échanges récents protégés est réglable. Lorsque le modèle expose sa longueur de contexte dans les métadonnées Ollama, la fenêtre demandée est plafonnée à cette capacité ; elle n’est pas augmentée automatiquement au-delà du réglage choisi. La consommation réelle de VRAM n’est pas mesurée.

Les réglages d’import sont désormais persistés dans PostgreSQL et modifiables depuis le client. Ils s’appliquent aux projets et aux conversations de cette installation. Abaisser un quota ne supprime aucun document existant.

## Mise à jour sur CachyOS / Linux

Les commandes suivantes conviennent à fish et Bash. Fermer le client et arrêter le terminal de l’API avant sa relance. Pour une installation contenant des données importantes, utiliser la sauvegarde existante du projet :

```shell
cd /developpement/seynax/Alycia
python3 scripts/backup.py create --output ../sauvegardes-alycia
git switch feature/local-client-v4-context-quality-20261117
git pull --ff-only
make demo-api
```

`make demo-api` restaure les outils .NET, applique les migrations puis démarre l’API. La nouvelle migration crée les tables d’indexation et de réglages d’import ; elle ne réécrit pas les messages ni les documents. Si Git signale des modifications locales incompatibles, les conserver et résoudre ce conflit avant la mise à jour.

Dans un second terminal :

```shell
cd /developpement/seynax/Alycia
make demo-client
```

Le client et l’API doivent être mis à jour ensemble. Le correctif du conflit `CloseInspector` est inclus dans l’historique de cette branche.

## Réglages conseillés pour commencer

Ouvrir une conversation, puis **Réglages de conversation → Avancé**.

| Réglage | Valeur initiale | Sens |
|---|---:|---|
| Fenêtre du modèle | 8 192 tokens | Essayer 16 384 si le modèle et la mémoire disponible le permettent ; aucune promesse de capacité VRAM. |
| Réserve de réponse | 1 024 tokens | Place protégée pour chaque génération. |
| Marge de sécurité | 256 tokens | Marge supplémentaire dans la fenêtre. |
| Résultats d’outils conservés | `0` | Allocation adaptative. Une valeur positive impose un budget maximal en tokens estimés. |
| Résultat maximal par appel | 24 000 octets | Limite de transport, jusqu’à 131 072 octets. Le contexte peut imposer un résultat plus petit. |
| Garde-fou du nombre d’appels | `0` | Pas de quota de nombre d’appels. Répétitions, délai et fenêtre restent contrôlés. |
| Durée maximale des outils | 1 200 secondes | Réglable indépendamment du délai d’inactivité d’un appel au modèle. |
| Extraits documentaires | `0` | Utiliser la place disponible, sans dépasser la fenêtre. |
| Échanges récents protégés | `2` | Échanges utilisateur/assistant conservés bruts ; les plus anciens restent stockés et peuvent être réduits ou recherchés. |
| Caractères des candidats à la recherche | `0` | Sans plafond global applicatif ; un plafond positif peut limiter les candidats proposés à la recherche. |
| Passages par lot d’embeddings | `16` | Baisser à 4 ou 8 si le fournisseur manque de mémoire. |
| Caractères par passage d’embeddings | `1 600` | Baisser si le modèle d’embeddings refuse un passage trop long. Les caractères restants sont traités dans les passages suivants. |

**Une conversation ayant déjà enregistré `ToolMaxCalls = 24` conserve cette valeur. La remplacer par `0`, ou choisir un plafond plus élevé, puis enregistrer.** Les autres réglages existants sont également conservés.

Les paramètres de génération, température, graine, résumé, comptage, recherche et mémoire restent disponibles dans la même fenêtre. Les valeurs serveur initiales utilisent toujours la section `Chat` de la configuration ASP.NET, par exemple `Chat__EmbeddingModel`, mais les réglages enregistrés d’une conversation prennent priorité.

## Activer l’indexation

1. Vérifier les modèles installés avec `ollama list`. Un modèle génératif et un modèle d’embeddings ont des rôles différents. Exemple de modèle d’embeddings fourni par Ollama : `ollama pull embeddinggemma`.
2. Dans les réglages de la conversation, choisir `hybrid` et saisir le **nom exact** du modèle d’embeddings local. Activer l’indexation des messages si souhaité.
3. Importer les fichiers, dossiers ou archives. Le worker découvre les changements au prochain passage, généralement dans les cinq secondes lorsqu’il est disponible. Une opération déjà en cours peut retarder la suivante.
4. Lire la progression en bas du client : attente, traitement, pause, erreur ou fin. Le compteur porte sur les documents et messages ; le détail indique les passages traités pour la source courante. Ce n’est pas une estimation du temps restant.
5. Dans **Documents**, utiliser **Mettre l’indexation en pause** ou **Démarrer ou reprendre l’indexation**. Une pause prend effet entre les lots ; le lot en cours peut terminer.

Pour les documents d’un projet sans conversation, ouvrir **Documents du projet → Démarrer ou reprendre l’indexation**, choisir le modèle, la taille des lots et des passages. Ce choix est conservé et les prochains imports du projet sont pris en compte. Utiliser le même modèle et le même découpage dans les conversations permet de réutiliser les vecteurs des documents partagés.

Le worker enregistre les vecteurs après chaque lot. Après un arrêt de l’API, un travail laissé en cours est repris. Après une erreur du fournisseur, le message reste visible et la relance explicite reprend les passages manquants. Un changement de modèle/digest ou de contenu crée de nouvelles clés, sans mélanger les espaces vectoriels.

La conversation ne recalcule plus les embeddings du corpus : elle encode sa requête et recherche dans les vecteurs disponibles. Tant que l’indexation est partielle, BM25 reste utilisable ; les omissions sémantiques sont signalées dans l’inspection du contexte. Une indexation terminée ne signifie pas que le modèle a lu tous les passages : une réponse utilise une sélection d’extraits. L’analyse exhaustive et la synthèse de projet restent des opérations distinctes.

Les messages sont indexés par conversation, avec leur rôle et leur provenance. `history_search` combine la recherche lexicale et sémantique lorsqu’elle est activée ; `history_read` permet de relire les échanges originaux. Les messages d’autres conversations ne deviennent pas accessibles implicitement. Les notes partagées du projet restent un partage explicite.

## Modifier les limites d’import

Dans **Paramètres → Connexion et outils → Imports et stockage** :

| Paramètre | Par défaut | Plage proposée |
|---|---:|---:|
| Taille d’un fichier | 25 Mio | 1–256 Mio |
| Lot, après décompression | 100 Mio | Taille d’un fichier–1 024 Mio |
| Entrées par lot, archives comprises | 500 | 1–10 000 |
| Fichiers stockés par portée | 500 | 0–1 000 000 ; 0 désactive le quota |
| Volume stocké par portée | 1 024 Mio | 0–8 388 608 Mio ; 0 désactive le quota |
| Profondeur des archives | 4 | 1–16 |
| Profondeur des dossiers | 16 | 1–64 |

Les plafonds par lot limitent la mémoire utilisée et les archives décompressées. Désactiver un quota de stockage ne crée pas de place sur le disque. Les protections de chemins, liens symboliques, fichiers sensibles et exclusions restent appliquées. Les formats non pris en charge ou les extractions partielles ne deviennent pas complets grâce aux embeddings ; les PDF scannés nécessitent toujours de l’OCR.

## PostgreSQL et pgvector

Le fonctionnement de base utilise PostgreSQL 17 : vecteurs `real[]`, identités de modèles et clés de sources. Sans extension, la similarité est calculée dans l’application. Si l’extension `vector` est installée dans la base, les distances cosinus sont calculées par pgvector en SQL, après filtrage des sources admissibles.

Cette version utilise une recherche exacte. Elle ne crée pas d’index HNSW ou IVFFlat et ne revendique pas de benchmark de performance à grande échelle. Cela évite aussi d’imposer une dimension vectorielle unique à tous les modèles.

L’image PostgreSQL standard reste le choix par défaut. Une image optionnelle ajoute pgvector 0.8.7 à la même base `postgres:17-alpine`, avec vérification SHA-256 de l’archive de compilation :

```shell
docker build -f docker/postgres-pgvector.Dockerfile -t alycia-postgres-pgvector:17-0.8.7 .
```

Après sauvegarde, ajouter cette ligne au fichier `.env` du dépôt, sans remplacer les autres lignes :

```dotenv
ALYCIA_POSTGRES_IMAGE=alycia-postgres-pgvector:17-0.8.7
```

Relancer `make demo-api`, puis, dans un autre terminal :

```shell
docker compose exec -T postgres sh -c 'psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "CREATE EXTENSION IF NOT EXISTS vector;"'
```

Ne pas supprimer le volume PostgreSQL. Après activation de l’extension, conserver une image contenant cette extension pour démarrer cette base. La recette d’image Docker n’a pas été exécutée dans l’environnement de développement de ce lot ; pgvector a été compilé et testé directement sur PostgreSQL isolé.

Les anciens fichiers `vectors-v1.json` ne sont plus lus. L’index sémantique est reconstruit en arrière-plan. Les originaux et l’historique sont conservés. Les sauvegardes PostgreSQL incluent désormais les vecteurs, les tâches et les réglages d’import.

## Vérifier et poursuivre

```shell
dotnet build Alycia.slnx
bash scripts/check.sh
```

Pour les tests PostgreSQL, fournir `ALYCIA_CHAT_TEST_CONNECTION` vers une instance de test dont le compte dispose de `CREATEDB`, puis lancer `bash scripts/check.sh --integration`. Les tests créent des bases isolées. `ALYCIA_TEST_PGVECTOR=1` active le scénario nécessitant l’extension installée sur cette instance. La recette HTTP complète est `python3 scripts/tests/http-integration.py --case all` avec la même connexion de test.

Les évolutions suivantes demandent une évaluation séparée : reranking des passages pour les questions complexes, index vectoriels approximatifs par modèle/dimension pour les très gros corpus, purge des anciens espaces vectoriels, et ordonnanceur priorisant davantage les demandes interactives. Les premières mesures à suivre sont la couverture des sources, la justesse des citations, les oublis de décisions anciennes et le délai avant le premier contenu utile.

Références : [API embeddings Ollama](https://docs.ollama.com/api/embed), [pgvector](https://github.com/pgvector/pgvector). L’absence de quota global d’indexation ne supprime pas la fenêtre maximale de chaque entrée du modèle d’embeddings.
