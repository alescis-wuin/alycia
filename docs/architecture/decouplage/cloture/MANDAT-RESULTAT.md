# Alycia · Résultat du mandat de clôture

**Décision PARTIEL.** G1 à G10 satisfont la recette sur le candidat applicatif. G11 reste ouvert pour Docker en exécution, le GPU et les postes natifs. G12 reste ouvert pour la signature reconnue par GitHub et la PR. Aucune promotion vers une branche de référence.

| Référence | Valeur |
|---|---|
| Base signée | `58182f2a7492b04f1f0b4c1518017a675519b376` |
| Arbre de base | `0ee1762c69f9e425d1c97f1765fc26ae98302eb8` |
| Commit applicatif signé localement et testé | `a778092475eb0dc28682a5e9284b56a0bc379ad6` |
| Arbre applicatif testé | `75a97fd3b98f77ebe2f90779f67dda956d086b8e` |
| Tête de la livraison | Le commit suivant ajoute exclusivement ce dossier. Le manifeste extérieur et `git rev-parse HEAD HEAD^{tree}` donnent son SHA et son arbre. |
| Branche de clôture réservée | `refactor/alycia-cloture-decouplage-20261122`, distante à la base signée en l’absence de signature reconnue. |

**400 tests .NET réussis**, dont les 370 identifiants exacts de D29 et les 327 de D20. Les 31 tests Python, les 30 contrôles d’isolation et les 35 parcours HTTP historiques passent. La matrice issue de Roslyn et des TRX recense 65 interfaces, dont 59 ports comportementaux exercés avec leur double et leur adaptateur réel. Les six exclusions disposent chacune d’un contrôle exécuté.

## Changements intégrés

- Conversations conserve les agrégats, le turn, les révisions et les écritures atomiques. Un modèle de lecture distinct expose aux consommateurs les messages et le contexte autorisés.
- Context assemble le prompt et ses budgets. Memory gère les règles de mémoire structurée, les faits temporels et leurs corrections. Retrieval possède le classement, la planification des requêtes et l’audit des citations. Tools possède l’exécution MCP et la politique d’accès aux outils.
- Documents possède les originaux, les projections d’autorisation, les overrides et les opérations de fork. Indexing utilise ses contrats et une primitive unique de publication transactionnelle. Workspaces possède les identités, profils et notes. Configuration possède les paramètres persistés. Les migrations historiques restent dans le monorepo sous un projet technique. Les tables et leurs contraintes sont identiques à la référence.
- Les scripts principaux contrôlent les références MSBuild, les symboles compilés Roslyn, la propriété des accès SQL/EF et la conformité des fournisseurs. La capture d’architecture utilise des répertoires de compilation distincts pour que ses reconstructions préservent les exécutables d’une recette en cours.
- Le contrôle des 370 identifiants a détecté six cas xUnit paramétrés dont l’identité dépend du type CLR historique du mode de conversation. La façade de commande conserve ce type public et traduit explicitement ses trois modes vers le contrat Context. Les six identifiants d’origine ont été récupérés et exécutés.
- Un test a également révélé la livraison d’un delta Ollama déjà tamponné après annulation. La vérification du token avant chaque remise de ligne et de delta corrige ce comportement. Les gardes de suivi EF détachent les écritures échouées dans les dépôts concernés.

## Lecture des pièces

`GATES.tsv` et `GATES.json` associent un résultat, une commande, du code et des preuves à chacun des douze critères. `OWNERSHIP.tsv` recense tables, types, adaptateurs et interfaces. `DEPENDENCIES.tsv` juxtapose les graphes de la base et du candidat. `PORTS.tsv` donne la fixture et la commande pour chaque port. `TESTS.json`, `COMPATIBILITY.json` et `recette/` rassemblent les résultats machine et les sources brutes. Les limites de plateforme, les mesures et la récupération du code figurent dans `PLATFORMS.md`, `PERFORMANCE.md` et `GIT-LIVRAISON.md`.

Le SHA-256 du JSON v1, sur 2 074 octets, est `b1d22617099059db98ec9a44cefa109927067d1a1ed25182f601e77135a2751b`. L’empreinte imprimée dans le mandat comporte deux caractères supplémentaires. La comparaison a porté sur les octets de la fixture signée et la sortie du sérialiseur. Les octets OpenAPI exacts sont dans `recette/openapi.raw`. Le détail des 47 chemins OpenAPI et des 92 lignes canoniques RAG figure dans `COMPATIBILITY.json`.
