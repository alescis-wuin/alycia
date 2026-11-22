# D01 — Propriété et cohérence PostgreSQL

Décisions de propriété logique prises sur `bc85154`. Les clés étrangères, les noms des colonnes et les migrations déjà appliquées font partie du contrat de reprise. Chaque bascule d’adaptateur doit être précédée d’un essai sur la base initiale restaurée ou sur un cluster de test migré.

| État physique | Propriétaire cible | Bascule et invariant |
|---|---|---|
| `workspaces.workspaces` | Workspaces | L’identité est créée avec le profil dans la même transaction. Conserver le type citext et son index unique comme arbitre concurrent. |
| `chat.workspace_profiles` et notes JSON | Workspaces | Un seul adaptateur pour le profil. Création paresseuse des projets anciens avec contrôle d’existence, insertion idempotente et contrôle de révision. Ne pas déplacer la table. |
| `chat.conversations`, `chat.messages`, appartenance | Conversations | Le déplacement valide la destination et efface les overrides dans sa transaction. Le couple de messages, la révision, le diagnostic et le ledger sont atomiques. Conserver le snapshot du tour et l’unicité par rôle et RequestId. |
| `chat.attachments`, octets des originaux, `chat.document_analyses` | Documents | Garder le contrôle du propriétaire exclusif, la filiation, le chemin serveur et le SHA. Après commit ambigu vérifier la référence sous verrou avec une transaction fraîche. Conserver les fichiers si la preuve manque. |
| `chat.workspace_file_overrides` | Documents | Décision de visibilité locale par paire conversation/fichier. Conserver les FK et l’effacement atomique lors d’un changement de projet. |
| `chat.conversations.MemoryLedgerJson` et colonnes du résumé | Memory pour le calcul, Conversations pour l’application atomique | Changement préparé transmis au propriétaire du tour. Une écriture différée du ledger briserait le rejeu. |
| `chat.temporal_memory` | Memory | Dérivé reconstructible et versionné par séquence. Conserver visibilité, hash, date et portée de conversation. |
| `chat.embedding_jobs`, `chat.embedding_vectors`, `chat.knowledge_units` | Indexing | Maintenir version, digest, dimensions, reprise, concurrence du worker et filtrage actuel. Une clé de cache ne confère aucun droit. |
| `chat.prompt_profiles`, `chat.import_settings` | Configuration | Projection validée des valeurs, lecture des configurations historiques et format JSON inchangé. |

Les dépendances inter-schémas des migrations sont conservées. Une migration ultérieure de propriété EF doit dissocier l’assembly de migration et l’adaptateur courant avant d’enlever un mapping. Vérifier les deux modèles et les données d’une base historique avant et après toute migration. Pour revenir à ce point, utiliser un worktree au parent de la phase. Aucune procédure de rollback du code ne doit effacer une base.
