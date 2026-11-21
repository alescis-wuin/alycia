# Programme contexte et preuves

Source : document utilisateur ALYCIA Compte rendu et roadmap du 17 novembre 2026.
Départ réel : `0b31f13` (correctif outils postérieur à la référence `58418f9`).
Branche autorisée : `feature/local-client-v4-context-quality-20261117`.
La demande explicite autorise les cinq phases successives sur cette même branche.

| Phase | Étapes et dépendances | Contrôles et compatibilité |
| --- | --- | --- |
| 01 | Geler context-v1 ; ajouter context-v2 ; mesurer classement et coûts ; durcir comparaison ; scénarios longs et grille humaine | Empreintes, dénominateurs, erreurs bloquantes, diagnostics additifs |
| 02 | Filtrer les scores nuls ; diversifier ; réunir synthèse et preuves ; ajouter recherche, symboles, références syntaxiques et lecture groupée ; tracer couverture | Budget, exclusion, conversation, pagination, MCP existant |
| 03 | Persister les unités compatibles ; reprendre après interruption ; exposer état/progression ; extraire PDF et Office avec coordonnées | Clés SHA/modèle/options/version, écritures atomiques, limites, anciens rapports |
| 04 | Rechercher et lire les messages originaux ; conserver rôles et corrections ; évaluer les affirmations sans certifier la vérité | Historique borné/paginé, isolation, citations inconnues, export existant |
| 05 | Réutiliser statistiques BM25 ; éviter réécriture vectorielle ; profiler ; rendre diagnostics et dialogues adaptatifs | Invalidation, corruption, concurrence bornée, benchmark, absence de nouveaux services |

Chaque phase conserve ses tests et son rapport. Les essais Ollama, GPU, PostgreSQL
et interface sont identifiés séparément ; une absence d'environnement ne vaut
jamais validation. Aucun gain qualitatif n'est déduit des seules regex.
Les mesures réelles de modèle nécessitent la recette sur la machine de l'opérateur.

Retour arrière : conserver les originaux et la base ; utiliser un checkout isolé
du commit initial ou un revert explicite des commits choisis. Ne pas utiliser
`reset --hard`, ne pas supprimer les volumes et ne pas appliquer de migration descendante.
