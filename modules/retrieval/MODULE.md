# Recherche

Sélection et classement des passages, BM25, audit des réponses et reclassement ONNX.

## Projets exportés

| Assembly | Répertoire | Dépendances Alycia directes |
|---|---|---|
| `Alycia.Modules.Retrieval` | src | Alycia.Contracts [0.1.0] |
| `Alycia.Modules.Retrieval.Onnx` | src | Alycia.Contracts [0.1.0] |
| `Alycia.Modules.Retrieval.Mocks` | testing | Alycia.Contracts [0.1.0] |
| `Alycia.Modules.Retrieval.Tests` | tests | Alycia.Modules.Retrieval.Onnx, Alycia.Modules.Retrieval, Alycia.Modules.Retrieval.Mocks, Alycia.Modules.Indexing.Mocks [0.1.0] |

## Persistance et contrats

Aucune table propre. Les données visibles sont fournies via les ports. Les essais ONNX lisent les poids Horizon CPU indiqués dans le README.

Les packages tiers et leurs empreintes sont fixés par chaque `packages.lock.json`. Les ports fonctionnels figurent dans [docs/ports.tsv](docs/ports.tsv). Les dépendances vers un autre domaine dans une suite propriétaire passent par les contrats ou le paquet local de doubles. Les conformités qui réunissent plusieurs adaptateurs réels sont exécutées par le maître.

La solution autonome est `Retrieval.slnx`. Chaque projet sous `testing/` est réservé à la recette ou au DevHost. L’API de production consomme les implémentations sous `src/`.
