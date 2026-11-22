# Recette P0–P8 · extraction multi-dépôts Alycia

**PRÊT POUR EXTRACTION SOUS LINUX.** Docker Compose a construit l’image API, démarré PostgreSQL et l’API sur un projet jetable, obtenu HTTP 200 sur `/healthz`, puis supprimé ses conteneurs, son réseau et ses volumes.

## Référence Git

| Élément | Valeur |
|---|---|
| Base `develop` | `fadd9aa41148ac3f147b25a73fb6d023928242c6` |
| Arbre de base | `c928d8a19f2764bf54ddad0deb93ca8a4c023935` |
| Commit d’implémentation déposé sur GitHub | `ee93008a1241efd42e773d743c1cd1166a099bc0` |
| Arbre d’implémentation et de preuves | `6562dab224f3aa76d453b08fc41834bbb0443e27` |
| Arbre applicatif hors dossier de preuves | `e6b7b68a4029c3cb1c12979274ef5640b59969fa` |
| Branche et PR | `prepare/extraction-multidepots-p0-p8` vers `develop`. Une seule livraison. Tête finale et arbre intégral consignés dans la description de la PR. |

La tête de livraison ajoute le présent rapport et ses preuves par-dessus le commit d’implémentation. La description de la PR fige les deux SHA finaux. Ils peuvent être relus depuis la branche publiée.

```sh
git fetch origin prepare/extraction-multidepots-p0-p8
git rev-parse FETCH_HEAD 'FETCH_HEAD^{tree}'
```

La base est le commit de fusion de la PR #53. GitHub a retourné `verified: true` et `reason: valid` pour ce commit. La branche était protégée et la vue consultée indiquait une liste vide de contrôles de statut requis. [BASELINE.json](BASELINE.json) contient les champs observés, les 400 identités relues dans les TRX et les empreintes des preuves antérieures. La PR #53 qualifie Linux pour G11. GitHub indique `unsigned` pour la tête d’implémentation. La procédure de signature par le mainteneur est dans le guide. Les journaux G11/G12 originaux décrivent une campagne antérieure et restent intacts. Windows natif est différé.

## Bilan du lot

| Phase | Réalisation et vérification |
|---|---|
| P0 | Départ confirmé par `git ls-remote`, `git rev-parse` et l’API GitHub. 23 projets de production et 12 suites initiaux, plus le benchmark et le collecteur Roslyn. Inventaire des projets et des entrées de compatibilité rattaché au commit signé de départ. |
| P1 | Les 890 chemins de base ont une destination. La matrice courante couvre les fichiers suivis. Le collecteur a identifié 633 déclarations publiques de types, les ports, les adaptateurs et les doubles. Les tables et les accès SQL/EF sont rattachés à leur propriétaire. |
| P2 | Douze dossiers exportables, 43 projets répartis dans les solutions de domaine. Le maître conserve 7 projets de composition, de migration, de test et de benchmark. Roslyn inspecte les 50 projets de la solution. Son propre projet est inventorié à part. |
| P3 | Sept paquets locaux versionnés `0.1.0`. Contracts et Platform servent aux consommateurs de production, cinq paquets de doubles servent aux suites de domaines. Chaque paquet a été reconstruit hors checkout et comparé octet pour octet. La surface CLR des contrats, relevée sur la DLL compilée depuis la base signée, est vérifiée par le test propriétaire. |
| P4 | Les doubles centraux sont redistribués entre les dix domaines métier. Contracts et Platform possèdent leurs fixtures techniques. Les ports privés de conversations et de workspaces utilisent leurs types de domaine dans leur propre dépôt. 59 conformités réussies, six exclusions exercées par leurs tests. Onze classes historiques ont changé d’assembly, soit 38 cas VSTest. La correspondance est bijective pour les 400 cas. |
| P5 | Les contrôles statiques et Roslyn passent. Chaque copie contrôle ses sources MSBuild, les chemins des DLL résolues, leur identité, le verrou NuGet et les octets du feed fourni. Les tests négatifs couvrent notamment les cycles, les types concrets étrangers, les accès SQL/EF, les références inter-domaines, les packages ouverts et les mocks en production. |
| P6 | 25 fichiers de migration, designers et snapshots égaux aux blobs de base. Les 12 IDs Chat et l’ID Workspaces ont été relus après application à une base neuve. Zéro changement de modèle en attente pour les deux contextes. Catalogue identique, 13 tables applicatives, 2 historiques, 34 contraintes. Les tests de concurrence, de révocation, de rollback et de perte de réponse à COMMIT ont tourné avec PostgreSQL et pgvector réels. |
| P7 | Bootstrap local, scripts autonomes, Make, Bash et PowerShell, assemblage maître et guide de retour disponibles. API et Boréale publiées en Release. Le DevHost Mock fonctionne. L’API publiée a refusé les combinaisons interdites et son manifeste comme ses DLL sont exempts de dépendances de test. |
| P8 | Douze copies distinctes en dehors du checkout, restauration verrouillée, build Release, 83 tests propriétaires aboutis. La recette maître compte 402 tests .NET, dont les 400 de référence, et 38 tests Python. Zéro échec ou cas ignoré dans ces suites. Compatibilités HTTP, OpenAPI, JSON et RAG confirmées. Image Docker construite, services démarrés et arrêtés, `/healthz` HTTP 200. |

## Extractions contrôlées

| Domaine | Projets | Tests de la copie isolée |
|---|---:|---:|
| configuration | 3 | 2 |
| context | 3 | 3 |
| contracts | 3 | 2 |
| conversations | 6 | 21 |
| documents | 4 | 13 |
| indexing | 3 | 1 |
| inference | 3 | 15 |
| memory | 3 | 1 |
| platform | 3 | 1 |
| retrieval | 4 | 9 |
| tools | 3 | 2 |
| workspaces | 5 | 13 |

La suite maître reprend ces 83 cas et ajoute les scénarios d’intégration et de présentation. Le champ `baselineTestsPresent: 316` de `compatibility.json` concerne la première référence D00. Le contrôle de la PR #53 utilise sa propre archive TRX et la table `TESTS.tsv` pour retrouver les 400 cas dans les 402 exécutés. Les totaux maître et modules mesurent deux exécutions distinctes. Le PDF binaire utilisé pour les coordonnées sur deux pages accompagne Documents avec exactement les mêmes octets que la fixture signée. Les doubles de lecture des conversations et des documents coopèrent à travers `ITurnDocuments`.

## Recette fonctionnelle

| Contrôle | Résultat |
|---|---|
| HTTP historique | 35 scénarios dans l’ordre de référence, six contrôles additionnels sur les originaux et deux en mode Mock |
| OpenAPI | 47 chemins. SHA-256 `d4ff9cb794dca7ccd775a9923d3732c1bf08cbd747c808d4359771426cd53b49` |
| JSON v1 sérialisé | 2 074 octets. SHA-256 `b1d22617099059db98ec9a44cefa109927067d1a1ed25182f601e77135a2751b` |
| RAG | 92 résultats ordonnés, aucun seuil en échec. SHA-256 canonique `0811f60b9dd1b5f42b9249e9876172c3a6b716f311fcc403da21e0f5407a4990` |
| Catalogue PostgreSQL | SHA-256 `8effd5b93f0ac18907d9d958e550f4c69a444a3f52bbb69b66fbe20965609333` |
| Outils et sécurité | NDJSON, rejeu de tour, accès aux originaux, MCP, refus du Mock en production et barrières documentaires exercés par les tests de transport et d’intégration |
| Environnement | Debian 13 x86-64, SDK .NET 10.0.404, Python 3.12.14, PostgreSQL 17.11, pgvector 0.8.0, ONNX CPU avec les empreintes signées |

## Critères de clôture

| Critère P8 | Statut et preuve |
|---|---|
| Autonomie des douze domaines | Réussi. [Journaux de chaque copie](preuves/extractions/) |
| Paquets locaux | Réussi. [Identités, versions et SHA-256](PACKAGES.tsv), [bootstrap](preuves/bootstrap.log) |
| Graphe métier | Réussi. [Dépendances](DEPENDENCIES.tsv), [gardes négatives](preuves/python-extraction.log) |
| Mocks et propriété des ports | Réussi. [Conformités et exclusions](PORTS.tsv) |
| Comportements historiques | Réussi. [Correspondance VSTest](TESTS.tsv), [résultats TRX](preuves/trx.zip) |
| Compatibilité de transport et de recherche | Réussi. [Comparaisons exécutées](preuves/compatibility.json) |
| Persistance et atomicité | Réussi. [Catalogue PostgreSQL](preuves/schema.tsv), [migrations](MIGRATIONS.tsv) et scénarios transactionnels dans les TRX |
| Assemblage du maître | Réussi. [Build Release](preuves/build-Release.log), [refus en production](preuves/mock-production.log), [Docker](preuves/docker.log) |
| Périmètre Git et publication | Respecté. Un dépôt, une branche de livraison, une PR, aucun nouveau workflow ni gitlink. Feed NuGet local. |
| Reproductibilité | Réussi. [Commandes](COMMANDES.md), [environnement](preuves/environnement.json) et [empreintes](preuves/SHA256SUMS) |

## Commandes et preuves

Les commandes sont à exécuter à la racine. Le [guide d’assemblage](ASSEMBLAGE.md) donne les variables de recette PostgreSQL et ONNX, le lancement, le futur changement de version, la signature et le retour arrière.

```sh
python3 scripts/modules.py bootstrap-packages
python3 scripts/modules.py check-module inference
python3 scripts/modules.py check-extraction
python3 scripts/modules.py check-master
bash scripts/quality.sh lint
(cd docs/architecture/preparation-multidepots/preuves && sha256sum -c SHA256SUMS)
```

Les extractions ont été exécutées avec un cache NuGet temporaire neuf, alimenté par les paquets de ce lot et NuGet.org. Le traitement `check-master` a repris par commandes individuelles après un redémarrage de l’hôte pendant la recette HTTP. Chaque étape dispose de ses résultats. Les prérequis et les commandes exécutées figurent dans [COMMANDES.md](COMMANDES.md). Les résultats PostgreSQL et ONNX proviennent de vrais adaptateurs. Le serveur compatible Ollama utilisé pour la recette HTTP est contrôlé. Le build Docker a utilisé le SDK Microsoft 10.0.404 avec le certificat racine du proxy de recette pour les connexions TLS. Le Dockerfile et ses contrôles de hashes ont été exécutés tels que livrés. La campagne reporte Windows natif conformément au mandat. GPU et KDE n’ont pas été rejoués. La branche conserve le dépôt Git existant. Le différentiel des workflows et des gitlinks est vide, et les sept paquets restent dans le feed local.

- [Projets initiaux](BASELINE-PROJETS.tsv), [déplacements](DEPLACEMENTS.tsv), [propriétaires actuels](OWNERSHIP.tsv) et [types publics](TYPES.tsv) et [propriété des tables et opérations SQL/EF](preuves/D29-propriete.tsv).
- [Dépendances déclarées, résolues et compilées](DEPENDENCIES.tsv), [ports et suites communes](PORTS.tsv), [packages et SHA-256](PACKAGES.tsv) et [migrations immuables](MIGRATIONS.tsv).
- [Classes déplacées](TEST-CLASSES.tsv), [correspondance des 400 cas réellement passés](TESTS.tsv), [14 TRX](preuves/trx.zip), [capture Roslyn](preuves/capture.zip) et [journaux des douze copies](preuves/extractions/).
- [Comparaison de compatibilité](preuves/compatibility.json), [catalogue SQL](preuves/schema.tsv), [versions et empreintes de publication](preuves/environnement.json), [preuve Docker](preuves/docker.log) et [SHA-256 des preuves](preuves/SHA256SUMS).
