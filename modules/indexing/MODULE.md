# Indexation

Découverte et reprise des travaux, dérivés, vecteurs et publication conditionnelle selon les permissions courantes.

## Projets exportés

| Assembly | Répertoire | Dépendances Alycia directes |
|---|---|---|
| `Alycia.Modules.Indexing` | src | Alycia.Contracts [0.1.0] |
| `Alycia.Modules.Indexing.Mocks` | testing | Alycia.Contracts [0.1.0] |
| `Alycia.Modules.Indexing.Tests` | tests | Alycia.Modules.Indexing |

## Persistance et contrats

chat.embedding_jobs, chat.embedding_vectors et chat.knowledge_units. Migrations Chat au maître.

Les packages tiers et leurs empreintes sont fixés par chaque `packages.lock.json`. Les ports fonctionnels figurent dans [docs/ports.tsv](docs/ports.tsv). Les dépendances vers un autre domaine dans une suite propriétaire passent par les contrats ou le paquet local de doubles. Les conformités qui réunissent plusieurs adaptateurs réels sont exécutées par le maître.

La solution autonome est `Indexing.slnx`. Chaque projet sous `testing/` est réservé à la recette ou au DevHost. L’API de production consomme les implémentations sous `src/`.
