# Workspaces

Identité, catalogue, dépôts et profils des espaces. Les lectures et modifications respectent le périmètre du propriétaire.

## Projets exportés

| Assembly | Répertoire | Dépendances Alycia directes |
|---|---|---|
| `Alycia.Modules.Workspaces.Application` | src | Alycia.Modules.Workspaces.Domain |
| `Alycia.Modules.Workspaces.Domain` | src | — |
| `Alycia.Modules.Workspaces.Infrastructure` | src | Alycia.Modules.Workspaces.Application, Alycia.Platform.Persistence [0.1.0], Alycia.Contracts [0.1.0] |
| `Alycia.Modules.Workspaces.Mocks` | testing | Alycia.Modules.Workspaces.Application, Alycia.Contracts [0.1.0] |
| `Alycia.Modules.Workspaces.Tests` | tests | Alycia.Modules.Workspaces.Infrastructure, Alycia.Modules.Workspaces.Application, Alycia.Modules.Workspaces.Mocks |

## Persistance et contrats

workspaces.workspaces et chat.workspace_profiles. WorkspacesDbContext et sa chaîne EF sont ici. Le maître coordonne la création du workspace avec son profil.

Les packages tiers et leurs empreintes sont fixés par chaque `packages.lock.json`. Les ports fonctionnels figurent dans [docs/ports.tsv](docs/ports.tsv). Les dépendances vers un autre domaine dans une suite propriétaire passent par les contrats ou le paquet local de doubles. Les conformités qui réunissent plusieurs adaptateurs réels sont exécutées par le maître.

La solution autonome est `Workspaces.slnx`. Chaque projet sous `testing/` est réservé à la recette ou au DevHost. L’API de production consomme les implémentations sous `src/`.
