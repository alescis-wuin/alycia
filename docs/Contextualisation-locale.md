# Contextualisation locale d’Alycia

## Base et portée

Livraison du 20 novembre 2026, en dix phases successives. Avant modification, la branche `feature/local-client-v4-context-quality-20261117` contenait les six autres branches. Son sommet `d24ec4192b87fe1897f1c27e5a2042dfaa9e18a4` avait 43 commits d’avance sur `develop` et 21 sur la branche `feature/local-client-v1-20261116`. La comparaison porte sur l’ascendance Git. Le client Boréale, l’API ASP.NET Core, le stockage PostgreSQL et le fournisseur Ollama existants restent les points d’entrée.

Les réglages sont ceux de la conversation, dans **Réglages → Avancé**. Les nouveaux comportements sont désactivés à la création et à la lecture des anciennes conversations. L’opérateur fournit les modèles. L’application ne les installe pas au démarrage.

## Phases, étapes et validation

| Ordre | Amélioration | Étapes réalisées | Contrôle et limite |
|---|---|---|---|
| 1 · L10 | Évaluation reproductible | Figer les questions, les passages et les jugements → mesurer le commit initial → comparer chaque option seule puis leur combinaison. | `evaluation/context-v3/`, `OfflineEvaluation.cs`, `scripts/evaluate-context.py`. Empreintes, rangs, cas en régression et latences par configuration. |
| 2 · L04 | Reclassement neuronal CPU | Partir des candidats autorisés → tokeniser chaque paire et exécuter ONNX → conserver les références et restaurer le classement initial en cas d’indisponibilité. | `OnnxPassageReranker.cs`, `ProjectRetrievalService.cs`. Graphe ONNX exécuté, paire longue ramenée à 384 tokens, égalités, cache, annulation et délai testés. Pertinence des poids Horizon non mesurée. |
| 3 · L01 | Routage | Détecter demande directe, historique, lecture ou exploration → appliquer portée, sources épinglées et autorisation d’outils → transmettre décision et motif à l’aperçu, à la préparation et à l’envoi. | `QueryRouting.cs`, `ChatService.cs`, `ChatEndpoints.*`. Cas FR/EN. Demander la lecture d’un fichier absent ou exclu produit le même état. |
| 4 · L05 | Composition des preuves | Réserver question, sortie, marge et outils → sélectionner les originaux par pertinence, coût et diversité → rattacher références, voisins admissibles et motifs d’omission. | `EvidenceComposer.cs`, `ProjectContext.cs`, `FileContext.cs`. Tests de dédoublonnage, épinglage, budgets et traçabilité. Une source imposée qui ne tient pas donne un signal explicite. |
| 5 · L02 | Reformulation et décomposition | Conserver la question originale et les identifiants → résoudre les références au tour utilisateur précédent et former des sous-requêtes bornées → fusionner les rangs et signaler les ambiguïtés. | `QueryPlanning.cs`, `ProjectRetrievalService.cs`. Cas d’anaphore, code et intentions multiples. La taille du contexte peut augmenter lorsque cette option fonctionne seule. |
| 6 · L03 | Indexation hiérarchique | Extraire chemins de sections et symboles → isoler le texte d’indexation de l’extrait original → versionner les entrées vectorielles et reprendre les travaux interrompus. | `CodeStructure.cs`, `ProjectIndexService.cs`, `BackgroundEmbeddingIndex.cs`. Vérification des lignes originales et de la reprise en base. Comparaison de deux modèles d’embedding réels en attente. |
| 7 · L06 | Mémoire sémantique et temporelle | Projeter les messages par pages en arrière-plan → garder rôle, date, empreinte et corrections → rappeler les versions autorisées par recherche lexicale et vectorielle si disponible. | `TemporalMemory.cs`, `TemporalMemoryStore.cs`, `PostgresEmbeddingStore.cs`. PostgreSQL et pgvector testés. Les souvenirs gardent un lien vers les messages originaux. Les propositions de l’assistant restent identifiées comme telles. |
| 8 · L07 | Recherche itérative | Définir les sous-questions → chercher puis lire les originaux avec les outils existants → enregistrer les références, les lacunes et le motif d’arrêt. | `ResearchPlan.cs`, `ProjectAgent.cs`. Plafond d’appels, pagination, répétitions et permissions testés. Le budget de texte porte sur les résultats d’outils encore présents dans le contexte. Gain de couverture sur un vrai projet non mesuré. |
| 9 · L08 | Vérification des affirmations | Rattacher l’affirmation aux sources fournies → vérifier citations, nombres, polarité et rôle → consigner les diagnostics et, sur demande, effectuer une seconde recherche bornée. | `SemanticClaimAudit.cs`, `CitationAudit.cs`, `ChatService.cs`. Quatre états, seize affirmations annotées et contrôle HTTP du texte diffusé, enregistré et rejoué. Les paraphrases complexes dépassent les règles déterministes. |
| 10 · L09 | Inférence adaptative | Estimer les besoins de chaque appel → transmettre une fenêtre effective bornée à Ollama → prioriser la génération et consigner les durées et la mémoire résidente exposées par le serveur. | `InferenceWindow.cs`, `OllamaChatModel.Budget.cs`, `EmbeddingWorker.cs`. Charge HTTP et limites de fenêtre testées. Latence, débit, réutilisation de préfixes et effet de la quantification KV sur RTX 4070 non mesurés. |

Les diagnostics d’une réponse réunissent la stratégie, les références retenues, le reclassement, la couverture de recherche, les affirmations vérifiées et les mesures disponibles. L’aperçu, la préparation et l’envoi utilisent le même calcul de réserve pour les outils et leurs messages protégés.

## Mesures avant et après

Les six questions documentaires et les quatre questions sans passage attendu partagent le même corpus, `k = 3`, le même budget documentaire et le même estimateur. Le résultat conservé par question permet d’examiner les erreurs ou les extraits ajoutés. La référence au commit initial donnait également 3 385 tokens et deux cas vides sur quatre.

| Configuration | Rappel@3 | nDCG@3 | MRR | Cas sans passage attendu correctement vides | Tokens estimés des extraits, total | p50 / p95 recherche, ms |
|---|---:|---:|---:|---:|---:|---:|
| Référence, options désactivées | 1,00 | 1,00 | 1,00 | 2/4 | 3 385 | 0,15 / 25,67 |
| Routage seul | 1,00 | 1,00 | 1,00 | 4/4 | 2 353 | 0,17 / 22,78 |
| Reformulation seule | 1,00 | 1,00 | 1,00 | 2/4 | 3 849 | 0,25 / 31,43 |
| Sélection des preuves seule | 1,00 | 1,00 | 1,00 | 2/4 | 2 753 | 0,24 / 31,10 |
| Index hiérarchique seul | 1,00 | 1,00 | 1,00 | 2/4 | 3 385 | 0,15 / 34,33 |
| Quatre options combinées | 1,00 | 1,00 | 1,00 | 4/4 | 2 319 | 0,24 / 26,37 |

La combinaison économise 1 066 tokens estimés d’extraits (31,5 %). Les scores de classement de référence étaient déjà au plafond sur les six questions positives. Un passage par configuration inclut le démarrage à froid du moteur de recherche. Ces temps CPU ne permettent pas d’estimer le premier token d’une réponse Ollama. La campagne s’arrête en échec si le rappel, le nDCG ou le MRR moyens baissent. Le rapport conserve aussi les régressions par question.

La vérification des affirmations a reproduit les 16 étiquettes annotées. Les quatre contradictions ont été détectées, avec zéro fausse alerte de contradiction sur les douze autres cas. La fidélité générale des réponses et le taux de fausses alertes en dehors de ces exemples restent à mesurer. [Rapport JSON et résultats individuels](validation/contextualisation-offline.json)

## Réglages et données

- **Routage et recherche** — `routingMode` accepte `off`, `auto`, `direct`, `memory`, `retrieval` et `explore`. `queryRewriteEnabled` et `maxSearchQueries` pilotent les requêtes dérivées (3 par défaut, de 1 à 8). Les recherches et outils restent limités aux sources autorisées.
- **Reclassement** — `rerankEnabled`, `rerankCandidateLimit` (24, de 1 à 64) et `rerankTimeoutMs` (1 500 ms, de 50 à 30 000). Pour un déploiement Compose, définir `ALYCIA_RERANK_MODEL_PATH` sur un répertoire absolu contenant `tokenizer.json` et `onnx/model_quantized.onnx` du modèle `Horizon-Labs/multilingual-reranker-small`. Le montage est en lecture seule. Pour une API ou un benchmark lancé avec `dotnet`, utiliser `ALYCIA_RERANK_DIRECTORY`. Redémarrer le processus après remplacement des poids pour recharger la session. Les empreintes du tokenizer et du graphe identifient la version réellement chargée.
- **Index et preuves** — `hierarchicalIndexEnabled`, `embeddingDocumentPrefix`, `embeddingQueryPrefix` et `evidenceSelectionEnabled`. Le mode `hybrid` utilise le modèle d’embeddings local choisi dans la conversation. L’indexation asynchrone expose progression et état. Les coordonnées des citations proviennent du texte source. Les anciens espaces vectoriels restent lisibles avec leurs paramètres initiaux.
- **Mémoire et exploration** — `temporalMemoryEnabled`, `agentPlanningEnabled` et `agentPlanMaxCalls` (8 par défaut, de 1 à 64). La migration additive `20261120010000_TemporalMemory` crée une projection indexée, liée à la conversation par suppression en cascade. Deux déclarations utilisateur datées peuvent coexister. Une correction explicite indique la version remplacée. Exemple de message indexable : « Correction [région] : Bretagne ». Une demande « au 2026-01-02 » fixe la date du rappel.
- **Export et effacement de la projection** — `GET /api/chat/conversations/{id}/memory/temporal?after=0&limit=50`, puis reprendre avec `nextAfter`. `DELETE /api/chat/conversations/{id}/memory/temporal` efface la projection de cette conversation. Les messages demeurent disponibles pour la reconstruire si la fonction est active.
- **Vérification et inférence** — `claimVerificationEnabled`, `verificationSearchEnabled` et `adaptiveInferenceEnabled`. La seconde recherche consulte jusqu’à trois passages autorisés et les distingue des preuves effectivement reçues lors de la génération. Le mode de génération jusqu’à l’arrêt garde la fenêtre configurée. Flash Attention, cache KV et modèle restent des choix de l’opérateur.
- **Dépendances** — `Microsoft.ML.OnnxRuntime` 1.23.2 (MIT) et `Tokenizers.HuggingFace` 3.23.1 (Apache-2.0) exécutent le graphe et son tokenizer localement. Aucun service supplémentaire pour la mémoire. Les poids Horizon (Apache-2.0) s’installent séparément. [Fiche du modèle et fichiers](https://huggingface.co/Horizon-Labs/multilingual-reranker-small)
- **Retour au comportement précédent** — rétablir `routingMode=off` et désactiver les nouvelles options de la conversation. Les messages, pièces jointes et tours déjà validés restent consultables. Avant migration d’une installation existante, conserver sa sauvegarde habituelle de PostgreSQL et du volume de fichiers.

## Lancer et tester

Sous Linux, prévoir Git, .NET SDK 10, Python 3.11 ou supérieur, Make, Docker avec Compose et Ollama local avec un modèle de génération déjà installé. Depuis la racine du projet :

```bash
ollama list
make demo-api
```

Dans un second terminal à la même racine :

```bash
make demo-client
```

Sous Windows, avec Docker Desktop et .NET 10, lancer `pwsh -File scripts/demo.ps1 -Action All` depuis la racine. Si l’instance Ollama gérée par Compose est vide, l’installation d’un modèle est explicite avec `pwsh -File scripts/demo.ps1 -Action All -PullModel -Model qwen2.5:1.5b`. Vérifications Windows : `pwsh -File scripts/check.ps1`.

```bash
bash scripts/check.sh
python3 -m unittest discover -s scripts/tests -p '*_tests.py'
dotnet build benchmarks/Alycia.ContextBenchmarks
python3 scripts/evaluate-context.py offline --output artifacts/contextualisation-locale.json
```

Pour inclure PostgreSQL, définir `ALYCIA_CHAT_TEST_CONNECTION` avec une connexion à une instance de test dont l’utilisateur a le droit `CREATEDB`. Lancer `bash scripts/check.sh --integration` puis `python3 scripts/tests/http-integration.py`. Pour exercer le chemin pgvector, installer l’extension sur l’instance et définir `ALYCIA_TEST_PGVECTOR=1` avant les tests. Les contrôles HTTP démarrent une API, un fournisseur Ollama simulé et une base isolée. Chaque campagne d’évaluation attend un nouveau chemin de sortie.

Avec les poids Horizon déjà installés, exécuter `python3 scripts/evaluate-context.py offline --rerank --output artifacts/contextualisation-onnx.json` après avoir défini `ALYCIA_RERANK_DIRECTORY`. La campagne échoue si les scores neuronaux ne sont pas effectivement utilisés.

Pour comparer deux modèles d’embedding déjà présents sur la machine, remplacer les trois noms ci-dessous par les identifiants exacts de `ollama list`. L’API de démonstration doit être démarrée. Chaque campagne utilise des conversations dédiées à l’évaluation.

```bash
python3 scripts/evaluate-context.py run --model MODELE_CHAT --embedding-model EMBEDDING_A --retrieval hybrid --label embedding-a --output artifacts/embedding-a --hardware "RTX 4070 Laptop 8 Go" --repeats 3
python3 scripts/evaluate-context.py run --model MODELE_CHAT --embedding-model EMBEDDING_B --retrieval hybrid --label embedding-b --output artifacts/embedding-b --hardware "RTX 4070 Laptop 8 Go" --repeats 3
python3 scripts/evaluate-context.py compare artifacts/embedding-a/run.json artifacts/embedding-b/run.json --single-factor
```

Le mode `run` consigne le modèle, ses paramètres, les temps et les coûts accessibles. Pour comparer une autre option, garder corpus, modèles, seed et budgets identiques, puis fournir un JSON par configuration via `--options`. Le modèle d’IA local sert à la génération. Les jugements humains sur les réponses complètes doivent être renseignés séparément.

## Vérifications de livraison

- Référence avant changement : 205 tests .NET réussis. Candidat : vérification du format, compilation, lint, cohérence du modèle EF avec les migrations et 222 tests .NET réussis (44 client, 167 chat, 11 espaces de travail). Aucun test ignoré, PostgreSQL 17.12 isolé avec pgvector, .NET SDK 10.0.404.
- 13 tests Python réussis. La recette API compte 28 contrôles, dont le périmètre des sources, la reprise de l’index, la contradiction diffusée et persistée et l’idempotence de `RequestId`. L’interface Avalonia a été exercée en mode headless.
- Analyse syntaxique PowerShell et tests des scripts Windows sous PowerShell 7, validation de la configuration Compose Linux et Windows. Le bureau Windows et le démon Docker n’ont pas été exécutés dans cet environnement.
- Aucun poids de génération, de reclassement ou d’embedding réel n’a été utilisé pendant cette recette. L’effet du reranker sur le classement, la qualité conversationnelle de bout en bout et les mesures RAM/VRAM, débit, TTFT et latence sur le portable de destination restent ouverts.
