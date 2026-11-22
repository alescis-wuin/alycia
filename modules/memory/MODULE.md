# Mémoire

Mémoire contextuelle et temporelle, écriture et lecture bornée des faits, éligibilité historique.

## Projets exportés

| Assembly | Répertoire | Dépendances Alycia directes |
|---|---|---|
| `Alycia.Modules.Memory` | src | Alycia.Contracts [0.1.0] |
| `Alycia.Modules.Memory.Mocks` | testing | Alycia.Contracts [0.1.0] |
| `Alycia.Modules.Memory.Tests` | tests | Alycia.Modules.Memory, Alycia.Modules.Memory.Mocks |

## Persistance et contrats

chat.temporal_memory. Migrations Chat au maître. Les lectures historiques de conversation sont fournies par les adaptateurs propriétaires.

Les packages tiers et leurs empreintes sont fixés par chaque `packages.lock.json`. Les ports fonctionnels figurent dans [docs/ports.tsv](docs/ports.tsv). Les dépendances vers un autre domaine dans une suite propriétaire passent par les contrats ou le paquet local de doubles. Les conformités qui réunissent plusieurs adaptateurs réels sont exécutées par le maître.

La solution autonome est `Memory.slnx`. Chaque projet sous `testing/` est réservé à la recette ou au DevHost. L’API de production consomme les implémentations sous `src/`.
