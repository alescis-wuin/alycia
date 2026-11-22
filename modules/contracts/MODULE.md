# Contrats publics

Types de transport HTTP, NDJSON et MCP, sérialisation v1, ports et projections inter-domaines.

## Projets exportés

| Assembly | Répertoire | Dépendances Alycia directes |
|---|---|---|
| `Alycia.Contracts` | src | — |
| `Alycia.Contracts.Fixtures` | testing | — |
| `Alycia.Contracts.Tests` | tests | Alycia.Contracts.Fixtures, Alycia.Contracts |

## Persistance et contrats

Aucune table. La surface publique et les sérialisations sont versionnées ensemble.

Les packages tiers et leurs empreintes sont fixés par chaque `packages.lock.json`. Les ports fonctionnels figurent dans [docs/ports.tsv](docs/ports.tsv). Les dépendances vers un autre domaine dans une suite propriétaire passent par les contrats ou le paquet local de doubles. Les conformités qui réunissent plusieurs adaptateurs réels sont exécutées par le maître.

La solution autonome est `Contracts.slnx`. Chaque projet sous `testing/` est réservé à la recette ou au DevHost. L’API de production consomme les implémentations sous `src/`.
