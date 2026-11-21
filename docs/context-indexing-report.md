# Compte rendu — budgets et indexation

18 novembre 2026 — branche `feature/local-client-v4-context-quality-20261117`.

## Analyse et réalisation

| Phase | Étapes réalisées |
|---|---|
| 1. Budgets | Séparation entre nombre d’appels, sortie par appel, textes conservés et fenêtre totale ; allocation adaptative ; exposition des réglages dans Avalonia ; plafonnement à la fenêtre annoncée par le modèle ; historique protégé configurable. |
| 2. Indexation | Remplacement du cache vectoriel JSON par PostgreSQL ; suppression du quota de 2 048 passages ; suppression du plafond global d’index par défaut ; lots persistés ; reprise après interruption ; isolation par source, version, modèle et conversation. |
| 3. Contexte et historique | Recherche hybride sur les vecteurs disponibles ; recherche lexicale maintenue pendant l’indexation ; requêtes seules encodées pendant le chat ; recherche sémantique de l’historique via les outils ; maintien des exclusions et de la provenance. |
| 4. Client et imports | Progression distincte de la génération ; pause/reprise ; réglages du modèle et du découpage pour les projets ; quotas d’import et de stockage modifiables dans les paramètres de l’application, sans suppression des données existantes. |
| 5. pgvector et livraison | Chemin SQL de recherche exacte avec pgvector optionnel ; recette d’image PostgreSQL 17 Alpine ; migration additive ; guide de mise à jour et d’utilisation. |

La mémoire structurée, les citations, les documents partagés et les permissions par conversation existaient déjà : ils sont réutilisés. Les embeddings des messages servent à `history_search` ; l’application n’injecte pas automatiquement tout l’historique vectorisé dans chaque réponse.

## Vérifications effectivement exécutées

| Contrôle | Résultat |
|---|---|
| `scripts/check.sh --integration` | Réussi : restauration, compilation de la solution, lint, cohérence du modèle EF et 205 tests .NET, aucun ignoré. |
| Répartition .NET | 150 tests Chat, 44 tests Avalonia, 11 tests Workspaces. |
| Tests Python existants | 13 réussis. |
| PostgreSQL 17.7 isolé | Migration, persistance, reprise après erreur et interruption, plus de 2 048 passages, source de plus de 8 millions de caractères, historique isolé, exclusions, quotas d’import et suppression en cascade vérifiés. |
| pgvector 0.8.7 natif | Compilation et scénario réel de recherche vectorielle réussis ; 18 tests ciblés réussis lors de cette vérification. |
| Recette HTTP `--case boreale` | 3 scénarios réussis : portée des sources, révision du projet pendant une réponse, conservation de l’historique. |
| Recette HTTP `--case indexing` | 2 scénarios réussis : indexation asynchrone/progression/pause/coût de requête, puis persistance et application des quotas d’import. |

La recette HTTP complète a initialement révélé un défaut de son scénario Boréale : ses appels MCP omettaient les en-têtes requis et recevaient HTTP 406. Le scénario a été corrigé pour envoyer les en-têtes MCP attendus, puis la recette Boréale ciblée a réussi. Les protections du serveur n’ont pas été assouplies.

## Limites et suites

- Les tests de génération et d’embeddings utilisent un fournisseur Ollama simulé. Aucune mesure de débit, de VRAM ou de qualité sémantique n’a été réalisée sur la machine de l’utilisateur.
- Le client a été compilé et testé avec Avalonia Headless ; le fonctionnement visuel complet sous KDE/Wayland reste à vérifier sur le poste.
- Le Dockerfile optionnel pgvector n’a pas été construit ici. Le chemin pgvector a été vérifié avec une compilation native de l’extension sur une base isolée.
- La recherche vectorielle est exacte, sans HNSW/IVFFlat. Les très grands corpus nécessitent encore des mesures de consommation mémoire et de latence, puis éventuellement un index approximatif et un reranker.
- Les limites par fichier, lot, format et fenêtre du modèle restent bornées. L’absence de quota global d’embeddings ne signifie pas une mémoire ou un disque infinis.
- Les anciens vecteurs d’autres modèles restent conservés jusqu’à suppression des sources ; une politique de purge explicite pourra être ajoutée.
- Les anciennes conversations conservent leurs réglages, dont une éventuelle limite de 24 appels. Le guide explique comment la modifier.

Le [guide d’exécution et d’utilisation](context-budget-indexing.md) fournit les commandes et les réglages. Aucune migration ni modification n’a été exécutée sur la base de données de l’utilisateur.
