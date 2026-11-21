# V3 — sources de projet, outils et MCP

Branche : `feature/local-client-v1-20261116`. Base : `33ba908`.
Reconstruction du 17 novembre 2026 après perte de l’environnement local précédent.
Les quatre lots sont réalisés et leurs contrôles ont été exécutés à nouveau.
Le [compte rendu](Local-Client-V3-Report.md) donne les résultats et les limites ;
le [guide](Local-Client-V3-Guide.md) détaille la mise à jour et la recette sur le poste.

| Lot | Étapes réalisées | Contrôle |
|---|---|---|
| A — contexte | 1. Répartir les extraits ; 2. privilégier le code pertinent ; 3. garder les imports de projets disponibles ; 4. rendre la sélection visible | Code présent malgré une longue documentation, persistance et exclusions |
| B — outils | 1. Lire les originaux ; 2. inventaire/recherche/lecture paginés ; 3. boucle native Ollama bornée ; 4. budget, annulation et traces | Cible après 200 000 octets, protection de l’historique, limites, export/rejeu |
| C — MCP | 1. Serveur HTTP par conversation ; 2. initialisation/catalogue/appels ; 3. client MCP utilisé par l’agent ; 4. accès local et exclusions | Recette HTTP, erreurs de protocole, isolation et révocation |
| D — analyse et livraison | 1. Parcours systématique des textes entiers ; 2. couverture explicite ; 3. interface et réglages ; 4. recette, guide et publication | Manifeste, code entier, client headless, compilation et contrôles de qualité |

Les outils sont en lecture seule sur les sources importées de la conversation.
L’exploration par le modèle reste sélective. L’analyse systématique mesure la
couverture du périmètre importé, sans garantir la compréhension du code par le modèle.
