# Publication des vecteurs et révocation

Le worker effectue l’inférence hors transaction. Pour chaque lot, il passe les passages et les coordonnées à `PostgresEmbeddingStore.PublishEligibleVectorsAsync`. La primitive fige les coordonnées avant le premier `await`, vérifie le nombre et les dimensions des vecteurs, la finitude des valeurs, les identifiants, les bornes et la longueur des passages. Les écritures sont ordonnées par clé déterministe.

## Ordre des verrous

1. L’adaptateur ouvre une connexion et une transaction PostgreSQL `READ COMMITTED`. La ligne du job est réservée avec `FOR UPDATE`. Le contrôle porte sur le propriétaire, sa nature, la révision, l’état, les réglages, le digest et la dimension. La liaison initiale du digest d’un ancien espace vectoriel est réalisée sous ce verrou.
2. Conversations et Workspaces reçoivent la même transaction. Leurs adaptateurs relisent l’identité, l’appartenance, la révision et l’archivage sous verrous de lecture partagés. Pour l’historique, ils vérifient aussi le message, son rôle, le RequestId, le hash et le texte aux coordonnées demandées.
3. Pour les fichiers, l’adaptateur Documents enrôle son contexte EF sur la connexion et la transaction existantes. Il prend `LOCK TABLE chat.attachments, chat.workspace_file_overrides IN SHARE MODE NOWAIT`. Cette portée protège aussi l’absence d’un override et l’insertion concurrente d’un doublon. Il vérifie chaque paire identifiant–empreinte du lot contre les originaux et les exclusions effectives.
4. Sous ces protections, Indexing insère les vecteurs. Il utilise `ON CONFLICT (identity,key) DO NOTHING`. Il valide le lot en un commit. Un conflit de verrous renvoie un refus de publication, libère les verrous et laisse la découverte reprendre à partir des droits courants. Le délai de verrou PostgreSQL est borné à trois secondes. Les compteurs de suivi du worker sont mis à jour par révision après les lots et recalculés à la reprise.
5. Une réponse perdue à `COMMIT` déclenche une seule tentative sur une autre connexion. Elle reprend les contrôles d’autorisation et relit toutes les clés. Si le lot est présent, il retourne le résultat acquis. La clé déterministe protège les lignes déjà commises. L’annulation demandée par le client se propage.

Une révocation arrivée la première empêche la validation du lot. Si la publication détient déjà les verrous, la révocation attend qu’elle se termine. Après la révocation, les lecteurs recontrôlent la visibilité et le hash. La présence physique d’une ancienne entrée du cache ne suffit jamais à l’exposer à une portée étrangère. Le cache documentaire conserve les dérivés par identité et clé. L’historique filtre aussi le propriétaire, la date, la séquence et l’empreinte source.

## Observation PostgreSQL

La trace de la campagne se trouve dans `recette/ordre-commits.tsv`. Dans le test avec barrière, le backend 252 détient le verrou consultatif 947262. Le test interroge `pg_blocking_pids` jusqu’à constater que le backend de révocation 253 attend le backend de publication 254. À l’événement 265, la barrière est libérée. La publication enregistre son `COMMIT` à l’événement 267. La mise à jour de révocation, exécutée en autocommit, aboutit ensuite. Le test attend sa réponse avant de vérifier le refus de republication et l’absence du passage révoqué à la lecture. Les PID sont propres à cette exécution.

| Contrôle réel | Observation vérifiée |
|---|---|
| Révocation et autorisation de plusieurs sources | Le retrait du deuxième fichier fait refuser tout le lot. Deux publications concurrentes autorisées produisent deux lignes, une par fichier. Une autre conversation ne retrouve aucune de ces clés. |
| Conflit inter-processus | Une fonction de test et des verrous PostgreSQL imposent l’ordre des commits. Aucun délai probabiliste ne sert de barrière. |
| Erreur au commit | Un trigger de contrainte différé annule toutes les écritures. La même opération réussit après retrait du trigger de test. |
| Réponse perdue | Un relais de test abandonne la première réponse serveur à COMMIT après son exécution. Un compteur de trigger prouve qu’aucune deuxième insertion du lot n’a eu lieu. |
| Override, archivage, appartenance, désactivation | Les changements modifient les droits de lecture et de publication, y compris sur une clé déjà en cache. Une révision périmée échoue. |
| Date, dimension, identité, périmètre | La lecture pgvector refuse une autre portée, un autre modèle ou une dimension non correspondante. Les bornes de temps historiques sont exclusives. |
| Tours, reprise et mémoire | Le dépôt publie le couple utilisateur/assistant, l’état de conversation et sa mémoire ensemble. La conformité du dépôt vérifie le rollback et le commit d’un tour avec le même RequestId, puis la remise à zéro sous contrôle de révision. Les tests historiques exercent aussi la reprise du worker. |

Les identifiants VSTest, les TRX et le journal ciblé sont sous `recette/`. Les verrous de table privilégient la correction dans les cas d’insertions concurrentes. Leur contention dépend de la fréquence des imports et des overrides. Les mesures disponibles figurent dans `PERFORMANCE.md`.
