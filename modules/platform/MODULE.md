# Persistance technique

RelationalSession prête à un adaptateur sa connexion et sa transaction. Le détenteur initial conserve le droit de commit.

## Projets exportés

| Assembly | Répertoire | Dépendances Alycia directes |
|---|---|---|
| `Alycia.Platform.Persistence` | src | — |
| `Alycia.Platform.Fixtures` | testing | — |
| `Alycia.Platform.Tests` | tests | Alycia.Platform.Fixtures, Alycia.Platform.Persistence |

## Persistance et contrats

Aucune table ni migration. La session relationnelle ne possède aucune donnée métier.

Les packages tiers et leurs empreintes sont fixés par chaque `packages.lock.json`. Les ports fonctionnels figurent dans [docs/ports.tsv](docs/ports.tsv). Les dépendances vers un autre domaine dans une suite propriétaire passent par les contrats ou le paquet local de doubles. Les conformités qui réunissent plusieurs adaptateurs réels sont exécutées par le maître.

La solution autonome est `Platform.slnx`. Chaque projet sous `testing/` est réservé à la recette ou au DevHost. L’API de production consomme les implémentations sous `src/`.
