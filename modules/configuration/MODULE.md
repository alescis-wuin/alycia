# Configuration

Profils de prompts et paramètres d’import persistants. La forme JSON v1 est définie dans Contracts.

## Projets exportés

| Assembly | Répertoire | Dépendances Alycia directes |
|---|---|---|
| `Alycia.Modules.Configuration` | src | Alycia.Contracts [0.1.0] |
| `Alycia.Modules.Configuration.Mocks` | testing | Alycia.Contracts [0.1.0] |
| `Alycia.Modules.Configuration.Tests` | tests | Alycia.Modules.Configuration |

## Persistance et contrats

chat.prompt_profiles et chat.import_settings. Migrations Chat au maître.

Les packages tiers et leurs empreintes sont fixés par chaque `packages.lock.json`. Les ports fonctionnels figurent dans [docs/ports.tsv](docs/ports.tsv). Les dépendances vers un autre domaine dans une suite propriétaire passent par les contrats ou le paquet local de doubles. Les conformités qui réunissent plusieurs adaptateurs réels sont exécutées par le maître.

La solution autonome est `Configuration.slnx`. Chaque projet sous `testing/` est réservé à la recette ou au DevHost. L’API de production consomme les implémentations sous `src/`.
