# Finition du découplage au 22 novembre 2026

**Décision de livraison — PARTIEL.** La base de la PR #50 est `7b04de23ee1854cfdbe2c4f8d91e3adf204f4f53`. Le code de la reprise est `8876b17d32717a2dd25ecd0cc5b71430dc48cd7d`, arbre Git `40318e47fd527b5f7f1778c9728fbc0e56d2a6ce`. La PR empilée #51 reste en brouillon. La suite .NET et la garde sont exécutées sur cette révision. Les recettes HTTP, RAG, EF et plateformes ont exercé la révision `11f3d609654cbca3b53f5405c2585547ad0e9610`. Un diff Git vérifie que le code produit et les benchmarks sont identiques. La phase D20 est validée. Les neuf phases suivantes conservent des obligations ouvertes.

## Bilan par phase

| Phase | État | Résultat et condition de clôture |
|---|---|---|
| [D20](D20.md) | VALIDÉ | Référence de départ vérifiée et conservée. Les 327 cas D20 sont présents dans la recette. |
| [D21](D21.md) | PARTIEL | 18 commits de reprise. Projections, instantanés, périmètres et annulations corrigés. G5 fermé. Le résultat MCP interne indique encore IsError et du texte pour plusieurs refus. La conformité des limites de chaque port de tour reste incomplète. |
| [D22](D22.md) | PARTIEL | Identité, profil, notes et changement de projet en transaction. La frontière Conversations conserve un dépôt qui agrège historique, outils et recherche. |
| [D23](D23.md) | PARTIEL | 6 scénarios HTTP des originaux réussis. Le port documentaire paginé a une suite commune. Indexing utilise encore le stockage de fichiers concret. |
| [D24](D24.md) | PARTIEL | Éligibilité, reprises et classement exercés sur PostgreSQL/pgvector. Le dernier contrôle des droits et la publication du cache restent dans deux transactions. |
| [D25](D25.md) | PARTIEL | Historique et rappel projetés. Context appelle directement StructuredMemory, TemporalMemory et la sélection de mémoire. |
| [D26](D26.md) | PARTIEL | Planification et vérification dans Retrieval. Les outils et le contexte utilisent encore des implémentations de résumé ou de recherche voisines. |
| [D27](D27.md) | PARTIEL | Composition et transport exercés. Chat.Application et Chat.Infrastructure hébergent encore plusieurs propriétaires métier. |
| [D28](D28.md) | PARTIEL | 23 contrôles isolés réussis. Deux assemblies échouent en isolation stricte et 40 ports comportementaux restent sans preuve commune complète. |
| [D29](D29.md) | PARTIEL | Recette de la révision publiée et retour arrière documentés. G3, les autres critères ouverts et la signature GitHub empêchent la clôture. |

## Audit G1 à G12

| Critère | État | Preuve et condition de clôture |
|---|---|---|
| G1 | PARTIEL | Garde compilée et usages inter-propriétaires inventoriés. Context appelle StructuredMemory et ProjectRetrieval. Indexing appelle FileStorage. |
| G2 | VALIDÉ | 13 tables applicatives, deux journaux de migrations, 34 contraintes comparées. Garde EF/SQL du code produit et tests de portée PostgreSQL. Portée de la preuve limitée aux tables et accès recensés dans ce monorepo. |
| G3 | PARTIEL | Commit du tour, révision, replay, rollback, fork et appartenance exercés sur PostgreSQL. BackgroundEmbeddingIndex vérifie le droit puis PostgresEmbeddingStore.WriteAsync écrit sur une autre connexion. La révocation et la publication ne sont pas sérialisées. |
| G4 | PARTIEL | Réglages spécialisés des sept domaines, tests des mutations en attente, annulations du fournisseur et identité binaire du JSON v1. Garanties de portée, de copie et de refus à compléter pour l’ensemble des ports. ProjectToolResult porte encore le booléen IsError et un texte pour plusieurs causes de refus. |
| G5 | VALIDÉ | Graphe MSBuild transitif et graphe Roslyn des propriétaires métier acycliques. 22 couples résolus, dont 14 métier. Deux fixtures négatives couvrent les cycles dans Chat et entre projets. Les liens techniques du mapping EF figurent au graphe complet. Les appels concrets restants relèvent de G1. |
| G6 | PARTIEL | 23 compilations ou suites isolées distinctes réussies, en répertoires sans les voisins. Échec du contrôle strict de Chat.Application et Chat.Infrastructure. |
| G7 | PARTIEL | 15 suites communes attribuées à des ports sur 55 ports comportementaux. 6 exclusions justifiées. Une suite commune et un double réutilisable restent à démontrer pour 40 ports. |
| G8 | VALIDÉ | API Release publiée sans assembly ni dépendance Testing. Refus de production rejoué et DevHost exercé. Périmètre du binaire API et des scénarios de démarrage contrôlés. |
| G9 | VALIDÉ | 370 tests .NET, dont les 327 IDs D20, tous réussis. 35 HTTP, 47 chemins OpenAPI, JSON v1 de 2 074 octets, 92 lignes RAG. Résultat pour le corpus et les scénarios de référence. |
| G10 | PARTIEL | Le registre transition.tsv contient uniquement son en-tête. Les usages EF/SQL et dynamiques sont contrôlés. ALLOWED et NARROW_EDGES historiques autorisent encore des dépendances de type non contractuelles. |
| G11 | PARTIEL | Debug, Release, publications API/Boréale, tests Avalonia headless, PostgreSQL/pgvector, Ollama CPU et ONNX avec poids. Docker, GPU, KDE/Wayland et Windows/PowerShell natifs restent non testés. Évaluation de performance multi-plateforme ouverte. |
| G12 | PARTIEL | Branche et PR empilées, arbres comparés, récupération épinglée et procédure de signature testée sur deux commits locaux. Signature sur la machine de l’intégrateur et reconnaissance de chaque commit par GitHub en attente. |

## Sortie des sept étapes de D21

| Étape | État | Vérification ou écart |
|---|---|---|
| D21.1 Propriété des contrats | PARTIEL | Les 61 interfaces ont une déclaration et des implémentations inventoriées. Le dépôt propriétaire expose encore dans une même interface les lectures de contexte, d’historique d’outil et de planification. |
| D21.2 Paramètres spécialisés | VALIDÉ pour les sept domaines nommés | Les versions de fenêtre, de génération, de modèle d’indexation, de budget, de rappel et de portée sont projetées à l’entrée. Le codec v1 conserve son identité binaire. |
| D21.3 Entités et DTO EF | PARTIEL | La garde refuse les entrées mutables et les entités suivies pour les consommateurs couverts. La preuve d’isolation complète des deux assemblies mixtes reste ouverte. |
| D21.4 Mutations en vol | PARTIEL | Les défauts reproduits dans le routage, les fournisseurs, les requêtes PostgreSQL et le brief sont corrigés. L’ensemble des ports n’a pas été éprouvé avec un appelant capable de muter ses tableaux pendant l’attente. |
| D21.5 Refus, annulation et portée | PARTIEL | Les pages documentaires et les replis optionnels disposent de témoins exécutés. Certains refus MCP se distinguent encore uniquement par leur texte. |
| D21.6 Cycles logiques | VALIDÉ | DAG de projet et DAG des propriétaires métier contrôlés sur les symboles compilés. |
| D21.7 Contrats autorisés | VALIDÉ dans la matrice compilée | Les règles figées contrôlent les assemblies isolées et chacun des propriétaires des projets Chat. Les fixtures négatives couvrent alias, entités suivies, concrets et transitivité. |

## Changements prouvés dans D21

Conversations projette l’historique et les sources au lieu de transmettre les agrégats suivis. Context, Documents, Indexing, Inference, Memory, Retrieval et Tools consomment des paramètres spécialisés. Les limites, les listes de sources, les éléments de routage, les requêtes au modèle et les filtres PostgreSQL appartiennent à la demande en cours. L’analyse documentaire reproduit la clé historique `D5F4B70F56EE8584A1D4781B3C9EEA0C940CAF7AAE3D6E91F0643D81DF473CF0` quand le modèle ramène la fenêtre de 8 192 à 4 096. Le test utilise PostgreSQL et une réponse de métadonnées de modèle contrôlée.

Les témoins en échec ont montré un lot reçu avec deux textes au lieu d’un, une tentative de lecture par le thread ONNX d’une collection appartenant à l’appelant, et un vecteur PostgreSQL enregistré à `[0, 1]` après validation de `[1, 0]`. Le repli SQL sans pgvector utilisait deux connexions pour une seule recherche. La version livrée réutilise la première. Le double d’index distingue une conversation inconnue d’une conversation voisine existante et compte uniquement les fichiers admissibles. Les replis d’inférence, d’indexation et d’ouverture d’une transaction propagent le jeton annulé. La génération interrompt la livraison du delta. Un témoin séparé a montré la mutation du brief pendant l’inspection du modèle. Copié à l’entrée, il conserve maintenant ses libellés et ses contraintes en cas de repli.

La garde initiale portant exclusivement sur les deux assemblies Chat manquait deux cycles. La garde livrée examine aussi les types compilés des modules séparés et les entités de domaine. Les liaisons aller et retour de Configuration et Conversations, puis de Retrieval et Indexing, ont été supprimées. Les références techniques de `ChatDbContext` vers les entités EF restent visibles dans le graphe de données. Le contrôle des dépendances de projet et les règles d’accès aux tables les examinent séparément.

Le registre des 61 interfaces précise leur déclaration, la portée et la pagination ou la visibilité temporelle. La colonne de conformité indique où les mêmes assertions ont effectivement tourné sur le double et l’adaptateur. Plusieurs ports n’ont encore aucune preuve exhaustive de refus. En particulier, le contrat interne des outils expose `ProjectToolResult.IsError` et du texte. Les causes de refus de portée et d’arguments ne disposent pas toutes d’une représentation typée indépendante du texte. Le dépôt Conversations réunit aussi des interfaces de lecture requises par trois consommateurs. La vérification de leurs combinaisons d’annulation, de révision périmée et de révocation reste ouverte. La garde vérifie les namespaces autorisés par assembly et par propriétaire partageant Chat.Application ou Chat.Infrastructure. Un alias Tools vers un contrat Workspaces interdit échoue sur un projet réellement compilé. [Sémantique des ports](D21-semantique-ports.tsv) · [Matrice de conformité](D29-ports.tsv)

## Recette de la révision

| Contrôle | Résultat |
|---|---|
| .NET 10, PostgreSQL 17 et pgvector | 370 réussis, 0 échec, 0 ignoré, dont 327 identifiants D20 présents |
| Garde et tests Python | 28 tests réussis, graphe de projets transitif et 14 relations métier acycliques |
| HTTP | 35 scénarios de référence, 6 scénarios d’originaux, 2 de DevHost et 2 avec Ollama local |
| OpenAPI | 47 chemins, SHA-256 `d4ff9cb794dca7ccd775a9923d3732c1bf08cbd747c808d4359771426cd53b49` |
| Configuration v1 | 2 074 octets, SHA-256 `b1d22617099059db98ec9a44cefa109927067d1a1ed25182f601e77135a2751b` |
| RAG | 92 lignes canoniques dans l’ordre, SHA-256 `0811f60b9dd1b5f42b9249e9876172c3a6b716f311fcc403da21e0f5407a4990` |
| Données | Deux modèles EF sans changement de migration, 34 contraintes du catalogue comparées à D20 |
| Déploiement vérifié | Debug, Release, API et Boréale publiées, refus du Mock en Production, Avalonia headless, Ollama et ONNX sur CPU |
| Performance CPU synthétique | Deux séries de 24 répétitions par taille, 64, 512 et 2 048 fragments, scores et ordre contrôlés à l’identique |
| Isolation stricte | Code 1. 23 contrôles distincts réussis. Deux assemblies Chat requièrent encore des modules voisins |

À 2 048 fragments, la médiane du classement en cache passe de 13,94 à 12,44 ms. Son p95 passe de 26,77 à 18,99 ms. Le p95 BM25 en cache est plus variable, à 22,63 puis 8,50 ms, contre 14,25 puis 12,41 ms dans les mesures D20 archivées. Les allocations BM25 en cache restent à environ 5,45 Mo par appel. Les premières constructions à froid sont en moyenne plus lentes de 45 % à 64 fragments et de 57 % à 512. À 2 048 fragments, la mesure baisse de 17 %. [Mesures brutes et variations](D29-microbenchmarks-reprise.tsv)

La paire diagnostique rejouée sur le même exécuteur a donné 64 fragments à 55,9 puis 37,1 ms, 512 fragments à 186,0 puis 91,4 ms, 2048 fragments à 370,4 puis 378,5 ms (candidate D20 puis reprise). Les objets Git du constructeur lexical, du programme de charge et des algorithmes de référence sont identiques. Le profil à 512 fragments de D20 varie aussi par rapport à sa propre mesure archivée. Ces échantillons justifient de garder la performance à froid ouverte sur une machine de référence.

Le JSON de preuves conserve les résultats par assembly et l’identité de l’arbre. La comparaison RAG porte sur les cas canoniques hors ligne. Les essais avec Ollama réel sont identifiés séparément. [Résultats chiffrés](D29-preuves-reprise.json) · [Identifiants des tests](D29-tests.tsv) · [Graphe des projets](D29-projets.tsv) · [Usages résolus](D29-usages.tsv)

## Signature, récupération et retour arrière

L’intégrateur signera sur sa machine. La préparation a été répétée dans un dépôt temporaire avec deux signatures SSH vérifiées localement et deux refus sur arbre ou base discordants. GitHub doit encore déclarer chaque commit de la nouvelle branche `verified`. Le contrôle de chaque arbre permet de rattacher la série signée aux résultats de cette recette. Une signature de ce candidat conserve son statut architectural partiel.

Les commandes de récupération, compilation, lancement API et Boréale, tests, signature et retour arrière sont dans les annexes. La colonne de revert du relevé identifie chaque modification. Pour annuler toute la reprise sur une branche dérivée, appliquer ces reverts du plus récent au plus ancien. [Commandes](D29-commandes.md) · [Commits de reprise](D21-reprise-commits.tsv) · [États des phases](D29-phases.tsv)
