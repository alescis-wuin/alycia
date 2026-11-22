# D01 — Inventaire du code compilé

Entrée `c3f846b`, produit identique à `bc85154`. Le collecteur s’exécute en tant qu’analyseur Roslyn pendant un Rebuild de la solution et du benchmark. Les 13 compilations ont réussi. [D01-ownership.tsv](D01-ownership.tsv) contient 3 307 déclarations écrites dans le dépôt avec leur identifiant Roslyn, fichier, ligne, accessibilité, assembly et propriétaire proposé. Les propriétés positionnelles des records sont incluses. [D01-access.tsv](D01-access.tsv) inventorie 510 lectures ou écritures de réglage (C), 99 références à un DbSet (E), 49 appels de composition (L), 42 références explicites à une table dans une commande SQL (S) et 12 points dynamiques (Y). Une ligne L décrit un site d’enregistrement, une fabrique peut choisir une instance déjà enregistrée.

| Passage actuel | Dépendance et raison du changement |
|---|---|
| ChatService | Construit plusieurs implémentations dans son constructeur de commodité. Le chemin de production utilise déjà plusieurs ports. Le snapshot précède le contrôle de rejeu. |
| ConversationContextPreparation | Preview lit, PrepareMemory applique et sauvegarde à l’intérieur du préparateur. La prise de bail doit rester valable jusqu’à la fin de la préparation. |
| ProjectCatalog et ProjectProfiles | SQL sur Workspaces depuis Chat et lectures des deux propriétaires sur la même connexion. Le contrôle de nom reste concurrent. |
| GenerationGate et OllamaInferenceLease | Bail par identifiant, indication globale de priorité et permis physique sont trois états différents. Les annotations ont une admission supplémentaire. |
| TemporalMemoryStore | Lit directement messages et faits, appelle la recherche vectorielle et KnowledgeStore. L’intervalle temporel et l’appartenance s’appliquent avant classement. |
| BackgroundEmbeddingIndex | Lit les DbSet Chat, profils et pièces jointes, prépare des lots et publie des données dérivées. |
| ProjectRetrievalService | Passe déjà par IProjectIndex et IIndexedEmbeddings. Le classement et les valeurs de réglage restent dans les assemblies Chat. |
| ProjectToolService | Construit des adaptateurs concrets à partir du DbContext de la requête. La vue du tour et l’autorisation serveur sont nécessaires aux lectures. |
| ChatRegistration | Fournisseurs HTTP et scopes EF, cache lexical singleton, worker hébergé, mémoire et outils scoped. Plusieurs contrats partagent la même instance scoped. |

Le graphe C7 de 75 couples est dans D00 ; le graphe des projets et leurs dépendances directes figurent dans baseline.json. Les cycles logiques incluent Context ↔ Memory et Conversations ↔ Context, même si les projets se compilent dans un DAG. Les accès de propriété, transactions et priorités de migration sont détaillés dans [D01-database-ownership.md](D01-database-ownership.md). Les activations EF, la sérialisation dynamique, le chargement ONNX et le XAML demandent en complément les essais d’intégration. L’inventaire statique ne démontre pas le propriétaire effectif de toute chaîne calculée lors de l’exécution.

## Reproduction

```bash
out=$(mktemp -d)
collector="$PWD/scripts/architecture/capture"
dotnet build "$collector/Inventory.csproj"
args=(-t:Rebuild -m:1 -p:UseSharedCompilation=false "-p:CustomAfterMicrosoftCommonTargets=$collector/Capture.targets" "-p:AlyciaArchitectureAnalyzer=$collector/bin/Debug/net10.0/Inventory.dll" "-p:AlyciaArchitectureRoot=$PWD" "-p:AlyciaArchitectureOutput=$out")
dotnet build Alycia.slnx "${args[@]}"
dotnet build benchmarks/Alycia.ContextBenchmarks --no-dependencies "${args[@]}"
python3 scripts/architecture/ownership.py "$out" "$out/report"
```

La sortie Git de D01 est le commit qui introduit ce fichier. Les appels indirects par réflexion, les types exclusivement synthétisés et les chemins Docker ne sont pas des déclarations C# produit. La traçabilité exhaustive de tous les appels dynamiques reste partielle.
