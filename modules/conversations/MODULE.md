# Conversations

Agrégats du chat, dépôt du tour, orchestration des lectures, gestion des révisions et des RequestId.

## Projets exportés

| Assembly | Répertoire | Dépendances Alycia directes |
|---|---|---|
| `Alycia.Modules.Chat.Application` | src | Alycia.Modules.Chat.Domain, Alycia.Contracts [0.1.0] |
| `Alycia.Modules.Chat.Domain` | src | — |
| `Alycia.Modules.Chat.Infrastructure` | src | Alycia.Modules.Chat.Application, Alycia.Platform.Persistence [0.1.0] |
| `Alycia.Modules.Conversations.Mocks` | testing | Alycia.Contracts [0.1.0] |
| `Alycia.Modules.Conversations.RepositoryFixtures` | testing | Alycia.Modules.Chat.Application |
| `Alycia.Modules.Conversations.Tests` | tests | Alycia.Modules.Chat.Infrastructure, Alycia.Modules.Conversations.Mocks |

## Persistance et contrats

chat.conversations et chat.messages. Le modèle historique partagé et les migrations Chat sont assemblés par le maître.

Les packages tiers et leurs empreintes sont fixés par chaque `packages.lock.json`. Les ports fonctionnels figurent dans [docs/ports.tsv](docs/ports.tsv). Les dépendances vers un autre domaine dans une suite propriétaire passent par les contrats ou le paquet local de doubles. Les conformités qui réunissent plusieurs adaptateurs réels sont exécutées par le maître.

La solution autonome est `Conversations.slnx`. Chaque projet sous `testing/` est réservé à la recette ou au DevHost. L’API de production consomme les implémentations sous `src/`.
