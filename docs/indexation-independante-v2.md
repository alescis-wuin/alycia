# Indexation indépendante et contextualisation dans Alycia

Lot du 20 novembre 2026. Branche `feature/local-client-v4-context-quality-20261117`. La base analysée est `2cc4b42a33585fff51fb320c288a4a2532e24836`.

## Récupérer, tester, démarrer

Dans le dépôt existant sur CachyOS, ces commandes fonctionnent sous fish et Bash. Git exige une avance rapide et signale un historique divergent.

```bash
cd /developpement/seynax/Alycia
git fetch origin && git switch feature/local-client-v4-context-quality-20261117 && git pull --ff-only origin feature/local-client-v4-context-quality-20261117
git log -1 --oneline
```

Pour une première installation, placer le dépôt dans un répertoire libre.

```bash
cd /developpement/seynax
git clone --branch feature/local-client-v4-context-quality-20261117 --single-branch https://github.com/alescis-wuin/Alycia.git Alycia
cd Alycia
```

Le client et les tests utilisent le SDK .NET 10. La démonstration Linux utilise GNU Make, Python 3, Docker avec Compose et Ollama sur la machine. L’environnement graphique peut rester sous KDE Wayland.

| Contrôle | Commande à la racine |
|---|---|
| Compilation, format, cohérence des migrations et tests disponibles | `bash scripts/check.sh` |
| Recette complète avec PostgreSQL, API, fournisseur Ollama simulé, tests Python et évaluation hors ligne | `bash scripts/check-context-v2.sh` |
| Même recette avec les distances pgvector en SQL | `bash scripts/check-context-v2.sh --pgvector` |

Choisir une seule recette complète. Le script utilise une instance PostgreSQL de test déjà désignée par `ALYCIA_CHAT_TEST_CONNECTION` si elle existe. À défaut il démarre un conteneur temporaire, avec port local libre et identifiants générés. L’option pgvector construit l’image du dépôt si elle manque. La première installation requiert l’accès aux paquets .NET et aux images nécessaires. Les résultats de la campagne vont dans un nouveau dossier sous `artifacts/evaluation/`. Une fois les dépendances installées, la campagne de classement seule s’exécute sans Docker, API ou Ollama.

```bash
dotnet build benchmarks/Alycia.ContextBenchmarks -m:1
python3 scripts/evaluate-context.py offline --pipeline-v2 --output artifacts/evaluation-manuelle.json
```

La campagne demande un nom de sortie neuf. Pour lancer Alycia, ouvrir deux terminaux à la racine.

**Terminal 1 — services et API**

```bash
ollama list
make demo-api
```

Si `ollama list` indique que le serveur est inaccessible, démarrer le service local Ollama ou lancer `ollama serve` dans un terminal dédié. La démonstration prépare PostgreSQL, applique les migrations et ouvre l’API sur `http://127.0.0.1:5187`.

**Terminal 2 — client Boréale**

```bash
cd /developpement/seynax/Alycia
curl -fsS http://127.0.0.1:5187/healthz
make demo-client
```

## Utiliser les nouvelles fonctions

1. **Configurer la conversation.** Dans *Réglages*, choisir le modèle génératif principal. Ouvrir *Indexation des connaissances* et cocher *Préparer les annotations et synthèses*. Avec le champ modèle vide, Alycia extrait des passages, mots-clés et dates localement. Pour obtenir des annotations générées, saisir le nom exact d’un modèle génératif déjà présent dans `ollama list`. Le plan propose par exemple `qwen3:0.6b` ou `qwen3:1.7b`. Le modèle principal et l’encodeur d’embeddings se choisissent séparément.
2. **Importer et suivre.** Ajouter les fichiers, le dossier ou le ZIP. Le bandeau indique la source en cours, le nombre de sources terminées et les appels consommés dans le cycle. Une tranche épuisée passe à l’état partiel et reprend automatiquement. Dans *Documents*, *Démarrer ou reprendre l’indexation* relance aussi les unités en repli. La pause conserve les travaux déjà enregistrés. Un projet peut indexer ses documents depuis *Documents du projet*, y compris avant l’ouverture d’une conversation.
3. **Examiner une source.** Déplier le document puis choisir *Lire la synthèse indexée*. Descendre du document aux sections puis aux passages. *Annotations et provenance* affiche les faits proposés, les incertitudes, les dates disponibles, l’empreinte et le coût de traitement. Un passage original reste consultable pendant la construction de sa synthèse.
4. **Indexer les conversations.** Activer *Indexer aussi les messages de cette conversation*. Dans l’inspecteur *Mémoire*, ouvrir *Synthèses des échanges*, choisir un groupe, puis le message original. Les groupes séparent la conversation, la journée UTC et le rôle. Activer également le rappel temporel pour exploiter ces groupes lors de la préparation des réponses.
5. **Choisir le mode de recherche.** Le routage automatique, les facettes, la classification et la sélection dynamique sont des réglages distincts. La recherche lexicale suffit pour utiliser les annotations. Pour ajouter le rappel vectoriel, choisir le mode hybride et un modèle d’embeddings local. L’indexeur traite le corpus par lots. Une question encode uniquement ses requêtes et consulte les vecteurs disponibles. Pour activer pgvector dans la base de démonstration existante, suivre la procédure dédiée. [PostgreSQL et pgvector](context-budget-indexing.md#postgresql-et-pgvector)
6. **Contrôler la réponse.** Ouvrir l’inspecteur du contexte avant l’envoi puis *Sources* sur la réponse. Le registre indique les passages fournis, les synthèses ajoutées, les messages historiques, les sorties d’outils présentes ou retirées et les candidats seulement classés. Les boutons interrogent la portée actuelle avant de rouvrir un original. Une contradiction explicite reste admissible comme preuve.
7. **Reprendre une analyse manuelle.** Dans *Analyses*, sélectionner l’exécution interrompue puis *Reprendre*. Les fragments et fichiers déjà validés sont réutilisés lorsque l’identité du modèle est connue et que les entrées sont compatibles. L’analyse manuelle utilise son propre budget et son propre modèle. L’indexation automatique poursuit ses cycles sans action de l’utilisateur.

### Régler les budgets

| Réglage | Valeur initiale | Effet |
|---|---:|---|
| Appels d’indexation par cycle | 64, de 1 à 8 192 | Se partage entre annotations et lots d’embeddings lorsqu’on active l’enrichissement. Plusieurs cycles couvrent un corpus plus grand. |
| Essais par unité | 2, de 1 à 4 | Borne les tentatives JSON. Un résultat refusé peut aboutir à une extraction de secours. |
| Fenêtre / réserve de sortie / cible de synthèse | 4 096 / 768 / 256 tokens | La génération conserve aussi une marge. Le client valide la compatibilité des trois valeurs. |
| Documents traités en parallèle / délai | 2 workers / 120 s | De 1 à 4 documents et de 5 à 600 s par appel du résumeur. Une seule inférence Ollama est admise à la fois au sein du processus API. |
| Profondeur maximale des réductions | 12 niveaux, de 1 à 24 | Borne les réductions hiérarchiques. Une synthèse interactive exige une couverture complète de l’unité dans les appels autorisés. |
| Analyse manuelle récursive | 256 appels, de 8 à 2 048 | Plafond par exécution. Les checkpoints permettent de continuer lors d’une reprise. |
| Classement | 24 candidats, 120 paires, 4 incertains | Le budget compte séparément question, résumé et facettes. Les candidats non examinés conservent leur admissibilité. |
| Seuils de pertinence et d’incertitude | 45 % / 12 % | Couverture lexicale, sans interprétation probabiliste. Une paraphrase sans recouvrement peut manquer. |
| Synthèses jointes au contexte | 0 token, capacité de 0 à 4 096 | Activation explicite. Le cache est consulté en premier. La synthèse à la demande est réservée aux passages sélectionnés dépassant l’enveloppe. |
| Appels de synthèse à la demande | 1, de 0 à 8 | Choisir 0 pour utiliser exclusivement le cache. |
| Documents / historique additionnel / mémoire / sorties d’outils | 0 pour l’allocation adaptative | La réserve de sortie du modèle et les tours protégés restent prioritaires. Le quota d’outils compte le texte effectivement conservé, y compris la mémoire résumée des outils. Le nombre d’appels, le temps et les octets par résultat se règlent séparément. |

Pour un premier essai sur le portable, activer l’indexation extractive et les facettes, fixer par exemple le budget de synthèses jointes à **512 tokens**, puis importer deux documents dont les durées ou versions diffèrent. Poser une question comparative et ouvrir les deux références. Le choix de 512 tokens est une configuration d’essai. Un important corpus peut être intégralement indexé tout en ne fournissant que quelques extraits à chaque réponse. Les limites configurables d’import, la place disque et la fenêtre unitaire de l’encodeur continuent de s’appliquer.

## Découpage et réalisation du lot

| Phase et correspondance | Étapes réalisées | Vérification |
|---|---|---|
| 0 · Référence, I00 / L10 | Lire les chemins réels du client, du chat, des outils, des migrations et des index. Compiler le commit initial. Mesurer les profils de référence sur le corpus fixe. | 222 tests .NET sur la base, évaluation avant changement. |
| 1 · Sources, I01–I02 / L03 | Reprendre les extracteurs de documents et l’AST C#. Identifier chaque passage par source, SHA et coordonnées. Conserver les dates d’import et les dates de fichier disponibles. | Imports d’archives imbriquées, exclusions, positions d’origine et fichiers partiels. |
| 2 · Annotations, I03–I04 / L03, L06 | Extraire ou annoter via le modèle local choisi. Valider les 13 champs et les références. Construire les synthèses passage → section → document et des groupes de messages distincts. | JSON invalide, repli, reprise sélective, séparation utilisateur/assistant, retour au texte original. |
| 3 · Index publiés, I05–I07 / L03 | Pondérer séparément les termes originaux et dérivés dans BM25. Conserver les espaces vectoriels existants et leur versionnement. Publier une racine après ses descendants. Indexer les termes des groupes d’historique dans PostgreSQL. | Lecture des hiérarchies publiées, interdiction des descendants étrangers, test PostgreSQL et pgvector. |
| 4 · Exécution, I08–I10 / L06, L09 | Réserver les appels avant l’inférence, borner la concurrence, céder la priorité au chat. Sauvegarder les unités, reprendre, écarter et purger les sources exclues globalement. Afficher l’état dans Avalonia. | Budget d’un appel, travail concurrent sur deux documents, retrait d’un document pendant l’inférence, relecture et suppression en cascade. |
| 5 · Préparation et rappel, R01–R04 / L01–L03, L06 | Préserver la question exacte et ses contraintes. Former des facettes et deux variantes. Réunir les résultats lexicaux, structurels, temporels et vectoriels selon la portée. Conserver les rangs et scores des variantes examinées. | Référence implicite, identifiant C#, question datée, source privée, deux facettes sur deux sources. |
| 6 · Pertinence et ordre, R05–R06 / L04, L08 | Évaluer question, résumé et facettes séparément, distinguer la pertinence de la relation à une affirmation explicite. Appliquer ensuite le reranker ONNX existant s’il est configuré. | Matrice de confusion sur 20 paires annotées et recette de repli du reranker. |
| 7 · Fenêtre, R07–R08 / L05–L07 | Sélectionner suivant le budget restant, la couverture et les épinglages. Compter séparément les catégories. Ajouter une synthèse bornée avec référence à l’original. Garder les dates et rôles. | Preview HTTP, réserves, historique protégé, première preuve, compaction des outils et idempotence. |
| 8 · Réponse, R09–R10 / L07–L09 | Conserver le modèle choisi, la vérification des affirmations et l’exploration bornée. Afficher le registre coloré avec libellés, motif de sélection et ouverture des originaux. | Compilation Avalonia, tests headless, réponses HTTP diffusées, diagnostics et rejeu. |
| 9 · Validation, I10 / L10 | Comparer les profils historiques et enrichis, mesurer les erreurs, archiver le protocole et fournir la commande complète. | Mesures déterministes ci-dessous. Le profilage sur le portable et la fidélité des synthèses génératives restent ouverts. |

## Compte rendu

**Écart de départ.** Le commit de référence comportait déjà les dix fonctions L01–L10, dont BM25, l’indexation des embeddings, le rappel temporel, le reranker ONNX optionnel et l’audit des citations. Une analyse récursive pouvait néanmoins refuser un lot entier si le nombre de fichiers dépassait le nombre d’appels. Les synthèses documentaires et historiques hiérarchiques n’étaient pas publiées indépendamment pour la recherche. Ces deux chemins ont été traités dans ce lot.

**Stockage.** La migration Chat `20261120100000_KnowledgeIndex` ajoute `chat.knowledge_units`, ses index et les colonnes de suivi de `chat.embedding_jobs`. Elle conserve les conversations, pièces jointes et vecteurs existants. Les clés incluent portée, profil, versions et identité de modèle. Le parcours de lecture exige une racine publiée. Un repli réindexé peut créer une nouvelle génération en laissant l’ancienne lisible pour une requête en cours. Une exclusion limitée à une conversation respecte l’accès des autres conversations du projet. Une exclusion globale purge les dérivés de connaissance au prochain passage du worker. Aucune nouvelle dépendance applicative ni licence de poids ajoutée.

**Mesures hors ligne.** Même SDK, corpus versionné, mêmes budgets et mêmes seuils de comparaison. Les tokens du tableau sont une estimation cumulée des extraits documentaires sur toutes les questions. Sur les cas positifs des deux corpus, Recall@3, nDCG@3 et MRR valent 1,00 pour les profils comparés.

| Corpus / profil | Questions | Négatifs correctement vides | Tokens documentaires |
|---|---:|---:|---:|
| Historique, chemin de référence | 10 | 2/4 | 3 385 |
| Historique, quatre options combinées avant ce lot | 10 | 4/4 | 2 319 |
| Historique, six options combinées après ce lot | 10 | 4/4 | 2 092 |
| Documents voisins et passages isolés, référence | 6 | 1/2 | 7 338 |
| Même corpus, six options combinées | 6 | 2/2 | 3 293 |

La question demandant à la fois une limite de code et un passage de manuel retrouve les deux sources attendues. La première campagne de développement a révélé une régression de routage sur une demande datée. Le correctif réoriente vers les documents une question datée sur le corpus et conserve le rappel conversationnel en cas de référence aux échanges. Les deux corpus sont des fixtures synthétiques modestes. Sur la campagne archivée de dix questions, le p50/p95 CPU est de 0,18/27,84 ms avec les quatre options de la branche initiale et de 0,91/46,26 ms avec les six options du candidat. Le p95 de ce petit échantillon est le maximum observé. Les temps incluent le premier passage à froid dans le code de recherche et ne représentent pas la latence d’une réponse Ollama. [Mesures avant changement](validation/indexation-independante-reference.json)

Le JSON du candidat contient les résultats par cas, les ablations, les allocations observées et les latences CPU avec le premier passage à froid. [Résultats détaillés](validation/indexation-independante-v2.json)

**Classification.** Les lignes indiquent les classes attendues et les colonnes les classes prédites.

| Classe attendue | Pertinent | Incertain | Hors sujet |
|---|---:|---:|---:|
| Pertinent | 9 | 0 | 1 |
| Incertain | 1 | 4 | 0 |
| Hors sujet | 0 | 0 | 5 |

18 classes correctes sur 20. La paraphrase « Quelle commune héberge le siège ? » face à « Le bâtiment central se situe à Rouen » est écartée par la règle lexicale. La mention d’une version non précisée est admise avec trop de confiance. Le signal indépendant de relation est correct sur les 20 exemples, dont 18 attendent « inconnu ». Le jeu distinct d’audit des affirmations donne 16/16 et détecte ses quatre contradictions. Ces mesures documentent le comportement de ces fixtures. Elles ne suffisent pas à calibrer le filtre sur les documents personnels. [Paires annotées](../evaluation/context-v4/relevance.json)

**Migration et retour.** Pour suspendre les nouvelles fonctions, désactiver les options de la conversation et mettre les indexations du projet en pause. Pour une base existante importante, la procédure de sauvegarde du dépôt couvre PostgreSQL et les originaux. Sur une base jetable, la migration a été appliquée, annulée et réappliquée. Les empreintes d’une conversation et de son message de contrôle sont restées identiques aux trois étapes. La migration descendante vers `20261120010000_TemporalMemory` retire la table de dérivés et les nouvelles colonnes de suivi. Tester un retour de schéma sur une copie avant de l’envisager sur les données réelles. [Sauvegarde et restauration](context-budget-indexing.md)

## Mesurer sur le portable

Lancer l’API et Ollama, puis utiliser le nom exact du modèle principal installé. Cette recette utilise le corpus synthétique du dépôt et des conversations de test créées puis retirées par le script. Chaque dossier de sortie doit être neuf. Exemple pour fish.

```fish
read -P "Nom exact du modèle de chat affiché par ollama list : " modele
set campagne (mktemp -d -p /tmp alycia-mesure-XXXXXXXX)
python3 scripts/evaluate-context.py run --model "$modele" --label reference --options evaluation/context-v3/candidate.json --output "$campagne/reference" --hardware "MSI Pulse 17 · RTX 4070 Laptop 8 Go" --repeats 3
python3 scripts/evaluate-context.py run --model "$modele" --label connaissances --options evaluation/context-v4/candidate.json --output "$campagne/connaissances" --hardware "MSI Pulse 17 · RTX 4070 Laptop 8 Go" --repeats 3
python3 scripts/evaluate-context.py compare "$campagne/reference/run.json" "$campagne/connaissances/run.json" > "$campagne/comparaison.json"
echo "$campagne"
```

Le profil fourni utilise l’extraction sans modèle auxiliaire. Pour mesurer Qwen, copier le profil candidat et renseigner `enrichment.model` avec le modèle génératif local à évaluer. Pour comparer exclusivement deux encodeurs locaux, conserver le même profil et remplacer seulement `--embedding-model` avec `--retrieval hybrid`. Le rapport enregistre les identités exposées, les temps d’import et d’indexation, les sources traitées par seconde, la préparation et la génération, ainsi qu’un exemple de synthèse relié à sa feuille et à l’original. Les champs indisponibles restent vides. La fidélité des synthèses et la pertinence des citations demandent une lecture humaine.

**Contrôles exécutés.** La recette complète a compilé la solution sans avertissement ni erreur, validé le format et la cohérence EF, exécuté 225 tests .NET (44 client, 170 chat, 11 espaces de travail), 13 tests Python, 32 contrôles HTTP et la campagne hors ligne avec pgvector activé sur l’instance de test. Après l’ajout de deux cas de réduction, la solution a été recompilée et les 12 tests .NET directement concernés ont réussi, dont ces deux nouveaux cas. Le parcours HTTP des connaissances a aussi été rejoué. Cela représente 227 tests .NET distincts. La recette couvre la reprise d’un seul grand fichier et le comportement d’une synthèse à la demande lorsque l’enveloppe permet ou empêche de couvrir la source entière.

**Périmètre de validation.** Les essais de livraison utilisent .NET SDK 10.0.404, PostgreSQL 17.11, pgvector 0.8.0 et un fournisseur Ollama simulé. La compilation inclut le client Avalonia. Les tests de l’interface utilisent le mode headless. Les poids de Qwen, des deux encodeurs et du reranker multilingue de production, les mesures RTX 4070, le rendu dans la session KDE et le démarrage Compose sur le portable restent à valider. Le démarrage du conteneur de test par Docker est fourni pour la machine de destination. La recette a ici utilisé une instance PostgreSQL jetable fournie au script. Le modèle NLI alternatif, les index HNSW et l’encodage de toutes les synthèses demeurent des expérimentations conditionnées à leurs mesures.

## Correctif des tests avec authentification PostgreSQL

Le journal CachyOS du 20 novembre montrait 225 réussites et deux échecs de connexion SCRAM. Les tests de mémoire temporelle et de stockage des outils construisaient la connexion de leur base isolée depuis celle du compte administrateur déjà ouverte. Npgsql en avait retiré le mot de passe. Ils utilisent désormais la chaîne fournie initialement par l’environnement.

Les deux erreurs ont été reproduites avec le code précédent sur PostgreSQL 17.11 configuré avec authentification TCP SCRAM obligatoire. Les deux tests corrigés réussissent sur cette même configuration, avec .NET SDK 10.0.404 et pgvector disponible. Les validations précédentes sur une instance sans mot de passe avaient masqué ce défaut.

Pour récupérer le correctif et poursuivre la recette complète sous fish ou Bash, exécuter à la racine du dépôt.

```bash
git pull --ff-only origin feature/local-client-v4-context-quality-20261117 && bash scripts/check-context-v2.sh --pgvector
```
