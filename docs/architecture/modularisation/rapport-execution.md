# Rapport d’exécution

Branche de revue `refactor/alycia-modularisation-20261121`. Base fonctionnelle vérifiée sur sept branches le 21 novembre 2026, `309d610425786e668782769f5b7650902d11d37b`. Chaque section correspond au commit dont le sujet commence par le numéro de phase. `git log --reverse --oneline 309d610..HEAD` donne les identifiants, y compris celui du présent document.

## P0 — Référence

Inventaire des projets, routes, DTO, services, tables, migrations et branches dans [baseline.md](baseline.md). Le flux HTTP est du NDJSON. Le SHA Git et les blocs ci-dessous permettent de reproduire la mesure.

| Contrôle sur la base | Résultat |
|---|---|
| `dotnet --info` et `dotnet sln Alycia.slnx list` | SDK 10.0.404 Linux x64, 11 projets dans la solution. |
| `bash scripts/check.sh` sans connexion de test | Compilation 0 avertissement / 0 erreur, lint réussi, aucune modification EF Chat en attente. Avalonia 48/48, Chat 182 réussis et 7 ignorés (PostgreSQL), Workspaces 11/11. Total 241 réussis, 0 échec, 7 ignorés. Durée 156,8 s avec restauration à froid. |
| `dotnet build benchmarks/Alycia.ContextBenchmarks` puis `python3 scripts/evaluate-context.py offline --pipeline-v2 --output <nouveau-fichier>` | 11,1 s. Corpus principal, 10 cas, k=3, Recall@3, nDCG@3 et MRR à 1 en référence et en combinaison. Aucun seuil du protocole en échec. |
| Prestataires externes | PostgreSQL 17.12 isolé provisionné sur loopback avec authentification SCRAM pour les recettes. Aucun modèle Ollama ou poids ONNX chargé. |

Les TRX et sorties complètes des mesures initiales ont été conservés dans l’espace de travail, hors Git. Le benchmark utilise son propre projet hors solution. Le comportement d’intégration est renseigné dans les phases concernées.

## P1 — Contrats de propriété

[ownership.md](ownership.md) fixe l’entrée, la sortie et la transaction de chaque domaine, les six décisions de conception et les fichiers réservés à l’intégrateur. Revue du graphe actuel et des consommateurs du client. Une seule nouvelle assembly de transport est prévue. Aucun changement exécutable pendant cette phase.

## P2 — Caractérisation

Export original v1 enregistré avant refonte dans `Fixtures/conversation-settings-v1.json`, test de restitution à l’octet, et scénario HTTP de collision de noms ajoutés. [Matrice des contrôles](verification.md). Sur PostgreSQL 17.12 jetable, le filtre Chat couvrant ConversationSettingsTransfer, PostgresChat, PersistentEmbedding, KnowledgePipeline, Streaming et LocalChatAccess a donné 44 réussis, 0 échec, 0 ignoré (38 s). La recette `python3 scripts/tests/http-integration.py --case boreale` est passée de 3 à 4 contrôles réussis. Le premier essai d’infrastructure n’a pu joindre le serveur d’un autre processus isolé. Les validations ci-dessus démarrent PostgreSQL dans le même environnement réseau que les tests.

## P3 — Transport et réglages

`Alycia.Contracts` rassemble les enregistrements JSON partagés, la lecture NDJSON et le codec v1. `ChatSettings` contient les champs persistés. `ChatOptions` en dérive et porte les données transitoires exclues du JSON. Les vues de génération, fenêtre, classement, mémoire, outils et index sont utilisées par leurs consommateurs. Les valeurs persistées sont scalaires ou immuables, y compris Enrichment. Les collections et le compteur transitoires des copies d’un tour sont lus, ou remplacés, sans mutation en place du réglage d’une autre discussion. Le client utilise des projections du contexte et des notes et conserve sa validation locale. Le Dockerfile inclut le projet de contrats.

`dotnet list src/Alycia.Client.Avalonia reference` indique uniquement Alycia.Contracts. Compilation de la solution réussie. Tests du client 48/48, filtres serveur des réglages, budgets, classement et mémoire 34/34. Après ajustement du validateur d’export serveur, 16/16 tests de transfert et mémoire. Lint réussi. Le fichier v1 produit par la base reste reproductible à l’octet.

## P4 — Inférence et ingestion

Deux worktrees indépendants sur `4d40072`. Les commits de chantier `9bf59a4` (inférence) et `d9164c0` (documents) sont parents du commit d’intégration. La réservation Ollama partage le créneau entre chat et embeddings, avec annulation de l’attente et démarrage du délai après acquisition. Les composants Documents préparent les flux, métadonnées, SHA et entrées d’archive avant la persistance par FileImport. La composition API enregistre séparément documents et fournisseurs.

Filtres OllamaChatModel, Streaming, TokenBudget et ToolCalling avec HTTP simulé : 48 réussis. DocumentImportBatch, File, StructuredDocuments et CodeStructure avec PostgreSQL jetable : 29 réussis. Aucun échec ni cas ignoré dans ces deux filtres. Lint des chantiers réussi. Lors de l’intégration, compilation de l’API corrigée après rétablissement de l’import FormOptions encore utilisé pour les uploads. Build intégré 0 avertissement / 0 erreur, recette HTTP `--case indexing` réussie, nettoyage confirmé.

## P5 — Index et classement

Chantiers `6115704` (index) et `7e170f5` (classement) intégrés sur `9371e9a`. Le worker appelle un cycle de maintenance par scope. Découverte, lecture des vecteurs, préparation des passages et cache versionné ont des propriétaires distincts. Le store et le classement partagent le calcul de cosinus. `IndexCatalog` assure les lectures et commandes HTTP, avec admission du propriétaire en amont. L’infrastructure fournit un instantané déjà filtré au classement applicatif.

Tests d’index sur PostgreSQL 17.12 et fournisseurs simulés : 17/17. Tests du classement, des diagnostics, citations et replis : 30/30. Lint et compilation de l’intégration réussis. Les 4 scénarios de `http-integration.py --case knowledge` passent, notamment retrait immédiat des dérivés après exclusion. Corpus offline identique : 92 lignes ordonnées, mêmes chemins, rangs, scores retournés, budgets et facettes. 18/20 classifications, 20/20 relations, 16/16 affirmations sur la base et la branche. Les durées et allocations mesurées varient entre processus. L’extension pgvector n’était pas activée pour cette phase.

## P6 — Contexte, mémoire et outils

Trois chantiers depuis `70cbbfe` : contexte `cf0bf3c`, mémoire `93da89b`, outils `51136dc`. `ITurnContext` prépare la fenêtre et sa réserve en mode envoi, aperçu ou préparation explicite. `IConversationMemory` applique les changements préparés dans la transaction de l’appelant. L’index temporel possède ses propres opérations. `ITurnTools` sélectionne les outils ordinaires ou l’instantané de projet et conduit la génération. Les appels de compatibilité de ProjectAgent délèguent le budget au même composant.

34 scénarios ciblés validés pour le contexte, dont deux après correction des préconditions de leur fixture. Mémoire 15/15, y compris rollback PostgreSQL, outils 36/36. Après raccordement, correction d’un import C# manquant, puis 28/28 tests de flux/outils/mémoire et 1/1 scénario HTTP de synthèse, relecture et rejeu sur PostgreSQL isolé. Aucun échec restant. Lint réussi.

## P7 — Projets et persistance

Base `3de4704`. Catalogue `cae1dd9`, fichiers `bf9de40`, tests de concurrence `635ffcc`. `IProjectCatalog` porte les opérations de projet. `ProjectCatalog` emploie l’entité canonique Workspaces et une même transaction sur les deux schémas pour la création. La nouvelle référence directe de Chat.Infrastructure vers Workspaces.Domain est unidirectionnelle. `ProjectFiles` assure le partage, les aperçus, retraits et exclusions. Les routes de projet adaptent les requêtes et les statuts HTTP.

PostgreSQL isolé : deux créations réellement bloquées en écriture, un seul couple projet/profil après libération. Une panne injectée à l’insertion du profil laisse zéro ligne dans les deux tables ; la nouvelle tentative réussit. Les deux tests xUnit passent. Fichiers projet 25/25 ; autres filtres Workspaces 11/11 et Chat PostgreSQL 2/2. La recette Boréale intégrée a validé les six scénarios, dont collisions HTTP 400 et 409. Les huit vérifications de déploiement couvrent Production avec opt-in et Development sans opt-in, y compris santé et OpenAPI. Aucune divergence des migrations historiques ni modification du modèle Workspaces en attente. Compilation API sans avertissement.

## P8 — Tours et transport HTTP

Base `589be30`, chantiers historique `b7811a8`, analyse `9796e3f`, configuration et mémoire `9c61cf4`. ChatService passe de 138 à 89 lignes. Il contrôle le verrou, le rejeu avant la révision, l’ajout atomique des deux messages et le commit. Le routage, l’audit de la réponse et les diagnostics ont des entrées distinctes. Les routes Chat n’accèdent plus à ChatDbContext. Les règles de transport, contrôles d’accès et formats restent dans l’API. Les trois flux utilisent le même sérialiseur NDJSON et vident le tampon après chaque événement. Le catalogue DI distingue explicitement les propriétaires. Les DTO de conversation et de messages sont construits explicitement. La vue Avalonia référence le type de transport partagé pour son binding de titre.

Historique 5/5, analyse 9/9, configuration 56/56. Intégration des services : 36/36 tests ciblés, puis 2/2 après finalisation du cycle de vie du jeton de préparation. La recette HTTP intégrée passe 35/35 avec PostgreSQL et fournisseur simulé, et les huit vérifications Production/Development passent. La comparaison de la réponse NDJSON et de sa relecture a reproduit sur la version antérieure la résolution de 100 ns de .NET contre la microseconde persistée. Le contrôle compare désormais l’instant à la précision du stockage et exige l’égalité exacte de tous les autres champs. Le comportement produit est conservé. Le binding Avalonia corrigé compile en Debug et Release.
