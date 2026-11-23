# Documents

Import, extraction structurée, originaux et contrôle de visibilité des pièces jointes.

## Projets exportés

| Assembly | Répertoire | Dépendances Alycia directes |
|---|---|---|
| `Alycia.Modules.Documents` | src | Alycia.Contracts [0.1.0] |
| `Alycia.Modules.Documents.Infrastructure` | src | Alycia.Modules.Documents, Alycia.Platform.Persistence [0.1.0], Alycia.Contracts [0.1.0] |
| `Alycia.Modules.Documents.Mocks` | testing | Alycia.Contracts [0.1.0] |
| `Alycia.Modules.Documents.Tests` | tests | Alycia.Modules.Documents.Infrastructure, Alycia.Modules.Documents |

## Persistance et contrats

chat.attachments, chat.document_analyses et chat.workspace_file_overrides. Migrations Chat au maître. Les verrous de publication sont exercés avec Indexing dans la recette maître.

Les packages tiers et leurs empreintes sont fixés par chaque `packages.lock.json`. Les ports fonctionnels figurent dans [docs/ports.tsv](docs/ports.tsv). Les dépendances vers un autre domaine dans une suite propriétaire passent par les contrats ou le paquet local de doubles. Les conformités qui réunissent plusieurs adaptateurs réels sont exécutées par le maître.

La solution autonome est `Documents.slnx`. Chaque projet sous `testing/` est réservé à la recette ou au DevHost. L’API de production consomme les implémentations sous `src/`.
