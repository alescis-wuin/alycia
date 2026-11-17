# V3 — sources de projet, outils et MCP

Branche : `feature/local-client-v1-20261116`. Base : `33ba908`.
Reconstruction du 17 novembre 2026 après perte de l’environnement local précédent.
Les contrôles seront exécutés à nouveau ; les anciens résultats ne valent pas validation de cette reconstruction.

| Lot | Étapes |
|---|---|
| A — contexte | Répartir les extraits ; privilégier le code pertinent ; garder les imports de projets disponibles ; rendre la sélection visible |
| B — outils | Lire les originaux ; fournir inventaire/recherche/lecture paginés ; boucle native Ollama bornée ; budget, annulation et traces |
| C — MCP | Serveur HTTP par conversation ; initialisation/catalogue/appels ; client MCP utilisé par l’agent ; accès local et exclusions |
| D — analyse et livraison | Parcours systématique des textes entiers ; couverture explicite ; interface, réglages, recette et guide ; publication |

Les outils sont en lecture seule sur les sources importées de la conversation.
L’exploration par le modèle reste sélective. L’analyse systématique mesure la
couverture du périmètre importé, sans garantir la compréhension du code par le modèle.
