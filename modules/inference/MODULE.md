# Inférence

Génération, streaming, embeddings et mesure auprès du fournisseur local. Les transports de test se configurent indépendamment.

## Projets exportés

| Assembly | Répertoire | Dépendances Alycia directes |
|---|---|---|
| `Alycia.Modules.Inference` | src | Alycia.Contracts [0.1.0] |
| `Alycia.Modules.Inference.Mocks` | testing | Alycia.Contracts [0.1.0] |
| `Alycia.Modules.Inference.Tests` | tests | Alycia.Modules.Inference, Alycia.Modules.Inference.Mocks, Alycia.Modules.Indexing.Mocks [0.1.0] |

## Persistance et contrats

Aucune table propre. Les modèles et le service Ollama sont externes au module.

Les packages tiers et leurs empreintes sont fixés par chaque `packages.lock.json`. Les ports fonctionnels figurent dans [docs/ports.tsv](docs/ports.tsv). Les dépendances vers un autre domaine dans une suite propriétaire passent par les contrats ou le paquet local de doubles. Les conformités qui réunissent plusieurs adaptateurs réels sont exécutées par le maître.

La solution autonome est `Inference.slnx`. Chaque projet sous `testing/` est réservé à la recette ou au DevHost. L’API de production consomme les implémentations sous `src/`.
