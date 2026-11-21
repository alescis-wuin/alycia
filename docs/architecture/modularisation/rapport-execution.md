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

## P9 — Présentation Boréale

Base `cf8e5db`. Les chantiers `5f11808` (conversations/navigation), `50cc76d` (documents/connaissances/index), `e6c729d` (inspecteur/outils) et `61fe70a` (configuration) sont intégrés. [presentation.md](presentation.md) cartographie les 13 partiels initiaux, 89 contrôles nommés et les événements. Les présentateurs portent le brouillon et son RequestId de réessai, la pagination, la portée projet, la révision des opérations sur les documents, l’annulation du suivi d’index et la résolution des citations. Les contrôles, le focus et les raccourcis restent dans Boréale. Le formulaire de configuration et sa validation sont autonomes.

Par chantier : 51 tests client pour la navigation, 11 pour les sources et l’index, 7 pour l’inspecteur et les outils, 12 pour les fonctionnalités et la configuration (filtres recouvrants). Après intégration : 59/59 dans la suite Avalonia. Sur un affichage X11 virtuel à rendu logiciel, le client Release a ouvert sa fenêtre de 1240 × 840 et Ctrl+Maj+F a masqué puis rétabli la barre latérale. Une sonde du véritable éditeur sur X11 avec transport simulé a vérifié l’annulation sans écriture, puis un seul PUT avec le modèle choisi et la révision d’origine. Le GPU, le bureau KDE/Wayland et le parcours visuel exhaustif n’ont pas été testés.

## P10 — Recette et livraison

Base de la phase `e24b4ef`. Le [graphe final](architecture.md), le [guide de contribution](CONTRIBUTING-AGENTS.md) et le README décrivent les projets, leurs propriétaires, les commandes d’intégration et les reprises. Le contrôle local vérifie projets, cycles, imports et dépendances transitives du client. Son premier essai signalait à tort la référence légitime du projet de tests Chat vers l’API. La règle concerne désormais les couches produit ; les tests restent dans la vérification des cycles. Une fixture temporaire a confirmé le rejet de dépendances effectivement interdites.

| Recette finale sur Linux x64, SDK 10.0.404 | Résultat |
|---|---|
| `ALYCIA_TEST_PGVECTOR=1 bash scripts/check.sh --integration` avec PostgreSQL 17.12 jetable | Une campagne globale, 86,6 s. Compilation sans avertissement ni erreur, lint réussi, deux modèles EF sans changement en attente. Avalonia 59/59, Chat 230/230, Workspaces 11/11 : **300 réussis, 0 échec, 0 ignoré**. Extension pgvector 0.8.0 activée dans les tests de vecteurs persistants et de mémoire temporelle. |
| `python3 scripts/tests/http-integration.py` | 35/35, 21,8 s. API réelle, PostgreSQL isolé, Ollama simulé. Révisions, commit/rejeu du tour, import/export, MCP, index et sources ; les scénarios isolés ont nettoyé leurs données. |
| Compilation du benchmark et `python3 scripts/evaluate-context.py offline --pipeline-v2` | 11,4 s pour l’évaluation. 92 lignes ordonnées identiques hors mesures de ressources ; mêmes sources, budgets et résultats sur chaque jeu. [Comparatif et reproduction](recette.md). |
| Inspection par rapport à `309d610` | 63 déclarations de routes et deux préfixes identiques. Les 25 fichiers de migrations historiques et 36 déclarations de paquets NuGet sont identiques. Sur les trois fichiers AXAML, seul le type de binding partagé de la conversation et sa déclaration de namespace changent. Ressources, licences de polices et workflows existants correspondent. |
| `dotnet publish src/Alycia.Api -c Release` | Publication locale réussie, comprenant Alycia.Contracts. Sur l’API publiée, huit assertions vérifient santé, chat, Workspaces et OpenAPI en Production avec opt-in et Development sans opt-in. La construction d’une image Docker n’a pas été exécutée. |
| Présentation | 59 tests Avalonia dans la campagne globale ; affichage X11 réel en mémoire et interaction avec le clavier validés en P9. Les poids Ollama/ONNX, l’accélération GPU et KDE/Wayland n’ont pas été exercés. |

Pour reproduire la partie base de données, fournir `ALYCIA_CHAT_TEST_CONNECTION` pour une instance dédiée autorisant la création de bases temporaires. Pour la variante pgvector, installer le serveur d’extension correspondant ; la suite crée l’extension dans ses bases de test. Les mesures initiales se trouvent en P0. Le nombre de tests ignorés à cette étape venait de l’absence de connexion de test pendant la campagne de référence.

## Critères d’acceptation

| Critère | Preuve sur la branche de revue |
|---|---|
| AC01 | Les sept têtes et leurs relations figurent dans l’inventaire P0. Le premier parent de P0 est `309d610`. |
| AC02 | [Carte avant](baseline.md), [graphe final](architecture.md), [propriété des données et décisions](ownership.md). |
| AC03 | Le contrôle d’architecture parcourt les 13 projets ; une fixture négative rejette cycles, imports de couches interdits et dépendance transitive du client. |
| AC04 | Le client ne référence que Alycia.Contracts. Réglages v1, DTO de conversation et messages NDJSON sont couverts par les tests de transport. |
| AC05 | Les sept chantiers A à G ont chacun leur commit, leur port ou point d’entrée et leurs tests de domaine aux phases P4 à P6. |
| AC06 | ChatService possède le commit du tour ; contexte, outils, routage, audit et historique ont leurs propriétaires. Aucun handler Chat ne reçoit un DbContext. |
| AC07 | Collision réelle, rollback inter-schémas injecté, révisions concurrentes et usages d’un espace préexistant. Les 25 fichiers de migrations correspondent aux blobs de la base ; les deux modèles EF sont à jour. |
| AC08 | [Composants de présentation](presentation.md), tests headless, fenêtre Release et raccourci sur X11, annulation et enregistrement du formulaire. |
| AC09 | Tests de sécurité du pair réseau et des fichiers, rejeu et annulation, citations/provenance, version stricte des exports, flux et reprise des index ; cas HTTP en environnement isolé. |
| AC10 | Résultats séparés ci-dessus pour la suite globale, PostgreSQL/pgvector, l’API, la présentation et le RAG hors ligne. |
| AC11 | [Contrat de tâche, développement et intégration parallèles, reprise par commit](CONTRIBUTING-AGENTS.md). |
| AC12 | Historique de phase lisible via le premier parent, commits de chantier accessibles comme parents supplémentaires, branche dédiée à la revue. |

Les identifiants exacts des phases se récupèrent avec `git log --first-parent --reverse --format='%H %s' 309d610..HEAD`. Pour obtenir la liste de fichiers et les volumes par phase, utiliser `git diff --stat <phase>^1 <phase>` et `git diff --name-status -M 309d610 HEAD`. Les sorties de test sont produites localement dans `artifacts/tests/` au format TRX. Les montées de version des paquets, une revue visuelle complète sur KDE/Wayland, une mesure de débit GPU avec un vrai modèle et l’orchestration Docker restent hors de cette recette.

## Consolidation postérieure à P0–P10

Le [rapport de livraison C0–C7](consolidation/C7.md) retrace les correctifs, les décisions, les commits et la campagne indépendante sur PostgreSQL/pgvector. La [matrice de composition](consolidation/C6.md) décrit les dépendances directes et transitives après intégration.
