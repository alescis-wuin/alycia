# Outils

Catalogue, exécution des outils de lecture et transmission des observations structurées dans le tour.

## Projets exportés

| Assembly | Répertoire | Dépendances Alycia directes |
|---|---|---|
| `Alycia.Modules.Tools` | src | Alycia.Contracts [0.1.0] |
| `Alycia.Modules.Tools.Mocks` | testing | Alycia.Contracts [0.1.0] |
| `Alycia.Modules.Tools.Tests` | tests | Alycia.Modules.Tools, Alycia.Modules.Tools.Mocks, Alycia.Modules.Conversations.Mocks [0.1.0], Alycia.Modules.Documents.Mocks [0.1.0], Alycia.Modules.Indexing.Mocks [0.1.0], Alycia.Modules.Retrieval.Mocks [0.1.0] |

## Persistance et contrats

Aucune table propre. Les sources et leur visibilité sont demandées au propriétaire via les ports. Le transport MCP HTTP du maître est exercé en intégration.

Les packages tiers et leurs empreintes sont fixés par chaque `packages.lock.json`. Les ports fonctionnels figurent dans [docs/ports.tsv](docs/ports.tsv). Les dépendances vers un autre domaine dans une suite propriétaire passent par les contrats ou le paquet local de doubles. Les conformités qui réunissent plusieurs adaptateurs réels sont exécutées par le maître.

La solution autonome est `Tools.slnx`. Chaque projet sous `testing/` est réservé à la recette ou au DevHost. L’API de production consomme les implémentations sous `src/`.
