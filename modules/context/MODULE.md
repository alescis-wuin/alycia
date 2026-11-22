# Contexte

Construction du contexte de tour, fenêtres et résumés récursifs dans les budgets fournis.

## Projets exportés

| Assembly | Répertoire | Dépendances Alycia directes |
|---|---|---|
| `Alycia.Modules.Context` | src | Alycia.Contracts [0.1.0] |
| `Alycia.Modules.Context.Mocks` | testing | Alycia.Contracts [0.1.0] |
| `Alycia.Modules.Context.Tests` | tests | Alycia.Modules.Context, Alycia.Modules.Context.Mocks, Alycia.Modules.Inference.Mocks [0.1.0] |

## Persistance et contrats

Aucune table propre. Les lectures de conversation et la génération passent par les ports.

Les packages tiers et leurs empreintes sont fixés par chaque `packages.lock.json`. Les ports fonctionnels figurent dans [docs/ports.tsv](docs/ports.tsv). Les dépendances vers un autre domaine dans une suite propriétaire passent par les contrats ou le paquet local de doubles. Les conformités qui réunissent plusieurs adaptateurs réels sont exécutées par le maître.

La solution autonome est `Context.slnx`. Chaque projet sous `testing/` est réservé à la recette ou au DevHost. L’API de production consomme les implémentations sous `src/`.
