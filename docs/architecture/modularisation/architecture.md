# Implemented module boundaries

The [baseline](baseline.md) records the reference at `309d610`. The deployment still consists of a local Avalonia client, an ASP.NET Core process and PostgreSQL. The application runs its existing background index worker inside the API process. Ollama is contacted through the existing HTTP clients.

## Projects after the refactor

| Product project | Direct project references |
|---|---|
| `Alycia.Api` | `Alycia.Modules.Workspaces.Infrastructure`, `Alycia.Modules.Chat.Infrastructure` |
| `Alycia.Client.Avalonia` | `Alycia.Contracts` |
| `Alycia.Contracts` | None |
| `Alycia.Modules.Chat.Application` | `Alycia.Contracts`, `Alycia.Modules.Chat.Domain` |
| `Alycia.Modules.Chat.Domain` | None |
| `Alycia.Modules.Chat.Infrastructure` | `Alycia.Modules.Chat.Application`, `Alycia.Modules.Workspaces.Domain` |
| `Alycia.Modules.Workspaces.Application` | `Alycia.Modules.Workspaces.Domain` |
| `Alycia.Modules.Workspaces.Domain` | None |
| `Alycia.Modules.Workspaces.Infrastructure` | `Alycia.Modules.Workspaces.Application` |

The solution also contains the Avalonia, Chat and Workspaces test projects. The ContextBenchmarks runner stays outside the solution. The single new assembly, Alycia.Contracts, replaces the former client reference to Chat.Application (and its transitive Domain dependency). The persistence bridge references Workspaces.Domain to use its canonical workspace constructor. The dependency guard verifies all 13 projects, product/test solution membership, imports and the client's transitive graph.

## Product project references

Arrows point from each consumer to its direct dependency.

```mermaid
flowchart TD
    API[Alycia.Api] --> CI[Chat.Infrastructure]
    API --> WI[Workspaces.Infrastructure]
    CI --> CA[Chat.Application]
    CI --> WD[Workspaces.Domain]
    WI --> WA[Workspaces.Application]
    WA --> WD
    CA --> CD[Chat.Domain]
    CA --> CT[Alycia.Contracts]
    UI[Avalonia] --> CT
```

## Operational boundaries

| Owner | Entry point | Storage, resource or caller |
|---|---|
| Conversations | `ChatService`, `IChatRepository`, `ConversationHistory` | A turn owns its per-conversation lease, snapshot, request identity, memory application and final atomic append. |
| Workspaces and projects | Workspaces application, `IProjectCatalog`, `ProjectFiles` | Canonical name in workspaces.workspaces, revision in chat.workspace_profiles. The Chat adapter uses a shared transaction when both are written. |
| Ingestion | `DocumentImportBatch`, `ArchiveContents`, `DocumentContent`, `FileImport` | Source streams are validated and exhausted before persistent imports; SQL metadata and immutable originals retain their owner and SHA. |
| Index | `IProjectIndex`, `IIndexMaintenance`, `IEmbeddingSimilarityReader`, `IndexCatalog` | The hosted worker resolves a fresh service scope for each execution cycle. Persistent jobs, embeddings and knowledge are versioned by owner. |
| Retrieval | `IProjectRetrieval`, `PassageRanking`, `IPassageReranker` | An eligible corpus is selected before offline ranking, source selection and citation. |
| Context and memory | `ITurnContext`, `IConversationMemory`, `ITemporalMemory`, `ITemporalMemoryIndex` | Preview is read-only. Send and explicit preparation apply pending changes under the owner’s revision/lease. |
| Inference | `IChatModel`, `ILocalEmbeddings`, `ITextTokenCounter` | Ollama adapters share the existing resource reservation; waiting and active inference have distinct cancellation/deadline semantics. |
| Tools | `ITurnTools`, `IProjectTools`, `ISnapshotProjectTools` | Local MCP dispatches to the scoped executor. A project-attached turn can use a snapshot, preserving exclusions and quotas. |
| HTTP | `ChatRegistration`, Chat endpoints, `ConversationResponses` | API maps DTOs, statuses and NDJSON deltas. HTTP security and OpenAPI gates remain in the host. |
| Boréale | [Presentation map](presentation.md) | Presentation state uses the typed HTTP client and wire contracts. |

The [storage inventory](baseline.md) and [ownership decisions](ownership.md) apply to the refactored tree. The client and the contract assembly have no dependency on a server layer. Historic migrations and the two EF contexts remain in their original projects.

## Verifying a change

Run `python3 scripts/check-modules.py` from a checkout. Follow the [parallel contribution guide](CONTRIBUTING-AGENTS.md) to assign a domain, freeze its contract, test it and integrate changes. The [execution report](rapport-execution.md) links the actual test and benchmark results.

## Frontières internes consolidées

La garde de `scripts/check.sh` vérifie aussi les liens entre propriétaires de Chat.Application et Chat.Infrastructure. Elle utilise Python et le compilateur Roslyn livré avec le SDK .NET 10. Le petit projet de lecture de symboles se trouve sous `scripts/architecture`. Il n’entre dans aucune compilation ou publication du produit et ne restaure aucun paquet tiers.

`scripts/architecture/module_rules.py` associe les dossiers aux propriétaires, attribue les anciens fichiers racine et les ports historiquement cohabités, puis déclare les dépendances directes permises. Le compilateur identifie le type référencé, y compris par un alias ou dans le même namespace. Le contexte peut ainsi demander `ITurnTools` sans accéder à `TurnTools`. Les lecteurs, valeurs et exceptions existants admis entre propriétaires sont nommés. Les adaptateurs de persistance peuvent utiliser leur contexte EF partagé. Les migrations et sources générées sont exclues de l’inventaire métier et vérifiées par les contrôles EF et la compilation. Les tests sont libres d’utiliser les adaptateurs et restent soumis au contrôle des références de projets.

| Exception existante | Propriétaire responsable et motif |
|---|---|
| Configuration, conversations et projets lisent l’import, les originaux ou les sources | Chaque service appelant coordonne la révision ou les choix du propriétaire de la pièce jointe |
| Documents et projets utilisent EfChatRepository | Les révisions et la lecture du propriétaire utilisent le dépôt commun |
| L’index lit ProjectSourceReader, CodeStructure, StructuredDocuments et les profils | Indexation extrait le contenu autorisé et reprend le travail avec la bonne révision |
| La mémoire lit KnowledgeStore | Mémoire temporelle utilise les unités déjà indexées |
| Les outils consultent ProjectIndexService et la lecture des sources | Outils exécute la lecture bornée à partir du snapshot autorisé |
| Les endpoints traduisent les exceptions EF | L’API traduit l’erreur en réponse HTTP. Program et ChatRegistration possèdent l’initialisation et la composition EF |

`python3 scripts/check-modules.py --matrix` donne, pour chaque lien logique direct, les types utilisés. Deux domaines peuvent se référencer par des ports et des valeurs distincts. Le graphe des références de projets et la résolution des services s’examinent séparément. Cette inspection statique des sources produit complète les vérifications de compilation et de comportement.
