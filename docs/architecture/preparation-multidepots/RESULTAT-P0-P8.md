# Alycia · Clôture P0–P8

Les douze modules exportés exécutent **234 cas propriétaires**. Le maître exécute ces mêmes cas et les **168 cas d’intégration ou d’interface**. Les 402 cas .NET et les 38 tests Python de la recette passent. La commande unique `python3 scripts/modules.py check-master` a terminé jusqu’au contrôle HTTP 200 dans Docker Compose. [Journal complet](preuves/master-definitif.log) · [Comparaison des identités par domaine](preuves/extraction-vs-maitre.tsv)

## Révisions et périmètre

| Référence | Valeur |
|---|---|
| Base signée `develop` | `fadd9aa41148ac3f147b25a73fb6d023928242c6` |
| Premier candidat audité | `a898c60887c8c60bcc572abb262bf449d7105573` |
| Commit applicatif utilisé pour la recette finale | [`89f2ba67439c5c78c2817e277c261c64a8a413b5`](https://github.com/alescis-wuin/alycia/commit/89f2ba67439c5c78c2817e277c261c64a8a413b5) |
| Arbre Git complet de ce commit | `6cd494b4e756f947f58bf22da04dd8deeb91733a` |
| Arbre en excluant uniquement `docs/architecture/preparation-multidepots/` | `abbf68f692fb3a82374936f2d07e8bf01c82979a` |
| Livraison | [PR #54](https://github.com/alescis-wuin/alycia/pull/54), de `prepare/extraction-multidepots-p0-p8` vers `develop`. La description donne la tête et l’arbre du commit final de preuves. |

La recette a été exécutée dans un checkout propre du commit applicatif GitHub. Les identités historiques, les empreintes attendues des paquets et le catalogue PostgreSQL de référence appartiennent déjà à ce commit. Le dernier commit ajoute et actualise les résultats dans le répertoire de recette. Les rapports historiques G1–G12 restent consultables dans leur version d’origine.

## Bilan P0–P8

| Phase | Résultat vérifié |
|---|---|
| P0 · Référence | La base signée, ses 400 cas VSTest et les deux cas introduits par le candidat ont chacun une correspondance. [Base](BASELINE.json) · [Cas individuels](CAS-VSTEST.tsv) |
| P1 · Propriété | Les 890 chemins signés ont une destination. L’inventaire couvre 662 déclarations publiques Roslyn et les opérations SQL/EF. [Chemins](DEPLACEMENTS.tsv) · [Types](TYPES.tsv) · [Accès aux données](preuves/D29-propriete.tsv) |
| P2 · Structure | 12 répertoires et 12 solutions autonomes, 43 projets de domaine et 7 projets au maître. Les 50 projets de la solution sont examinés. [Inventaire physique](OWNERSHIP.tsv) |
| P3 · Contrats | Sept paquets 0.1.0 reconstruits et comparés octet pour octet hors checkout. Surface publique des contrats comparée à la référence. [Paquets et SHA-256](PACKAGES.tsv) |
| P4 · Tests et doubles | 95 méthodes supplémentaires changent de propriétaire. Les 59 conformités passent et les six exclusions ont une justification exercée. La garde vérifie le domaine physique des doubles comportementaux. [Ports](PORTS.tsv) · [Méthodes](TEST-METHODES.tsv) |
| P5 · Dépendances | Les gardes examinent les références sources et binaires résolues, les sources MSBuild évaluées, les verrous et les dépendances des publications. Le feed réservé à Alycia est local. [Graphe](DEPENDENCIES.tsv) · [Contrôle de production](preuves/mock-production.log) |
| P6 · Persistance | 25 fichiers EF identiques aux blobs signés, 12 migrations Chat au maître et une Workspaces chez son propriétaire. Deux modèles sans changement en attente. Le schéma PostgreSQL présente 13 tables applicatives, deux historiques, 34 contraintes, 117 colonnes et 31 index dans les schémas inspectés. [Migrations](MIGRATIONS.tsv) · [Contraintes](preuves/schema.tsv) · [Colonnes et index](preuves/schema-details.tsv) |
| P7 · Assemblage | SDK 10.0.404 provisionné et vérifié dans un répertoire utilisateur. Entrées Bash, fish et PowerShell documentées. Debug et Release compilés. API et Boréale publiées. [Commandes](COMMANDES.md) · [Environnement et DLL](preuves/environnement.json) |
| P8 · Extraction et maître | 12 copies extérieures, cache NuGet neuf pour la campagne, 234 tests propriétaires. Maître complet jusqu’à Docker, 402 tests .NET, 38 Python, zéro échec et zéro cas ignoré dans les suites de recette. [Extraction](preuves/extraction-definitive.log) · [Maître](preuves/master-definitif.log) |

## Résultat de la revue et corrections

Le candidat `a898c608` exécutait 83 cas dans les modules. Une modification de la version du prompt de mémoire échappait à la suite Memory isolée. Le contrôle de schéma comparait les contraintes mais laissait passer une colonne et un index supplémentaires. Seize propriétaires de ports divergeaient du domaine de l’adaptateur et de son double. La première campagne du maître avait été reprise après une interruption.

| Finition | Correction et preuve |
|---|---|
| F1 | Les 177 méthodes et 256 cas initiaux de Chat ont une décision individuelle. 151 cas passent dans un module. 105 cas de Chat et 63 de Boréale restent au maître. Les théories conservent leurs jeux de paramètres. Deux libellés tronqués identiques sont distingués par les arguments xUnit sérialisés et les GUID VSTest. [Arguments et GUID](TEST-PARAMETRES.tsv) · [Valeurs observées dans l’adaptateur](preuves/arguments-xunit.json). Depuis la base signée, 189 des 400 identités ont changé d’assembly. [Décisions](TESTS-MAITRE.tsv) · [400 correspondances signées](TESTS.tsv) · [402 cas du candidat](CAS-VSTEST.tsv) |
| F2 | 16 rattachements corrigés. Les consommateurs accèdent aux doubles distribués par leurs paquets verrouillés ou à leurs fixtures locales. Les 59 conformités et 6 exclusions sont reliées à leur méthode exécutée. [Matrice](PORTS.tsv) |
| F3 | Le SDK privé x64 a été utilisé sous Bash et fish. Une archive altérée a été rejetée avant installation. Les deux empreintes Linux sont épinglées. La procédure PowerShell et les contraintes de licence sont documentées. [Bash](preuves/sdk-bash.log) · [fish](preuves/sdk-fish.log) · [Rejet SHA-512](preuves/sdk-corrompu.log) · [Guide](ASSEMBLAGE.md) |
| F4 | Une invocation intégrale a publié les applications, contrôlé la production et le schéma, exécuté les suites et validé les services Docker avec PostgreSQL. [Journal](preuves/master-definitif.log) · [Healthcheck](preuves/docker.log) |
| F5 | Chaque solution restaure, compile en Release et exécute ses tests hors checkout. Les identités VSTest des douze copies sont comparées individuellement à celles du maître. [Résultat par domaine](preuves/extraction-vs-maitre.tsv) |
| F6 | 35 scénarios HTTP historiques, six contrôles des originaux et deux du DevHost Mock, 47 chemins OpenAPI, JSON v1 de 2 074 octets et 92 résultats RAG ordonnés conformes. Publication vectorielle, révocation, refus de commit, rollback, perte d’accusé de COMMIT et idempotence sont exercés avec PostgreSQL et pgvector. [Compatibilité](preuves/compatibility.json) · [TRX](preuves/trx.zip) |
| F7 | Inventaires, captures Roslyn, catalogues accompagnés de leurs empreintes, sorties de compilation, résultats et commandes associés à la révision testée. [Empreintes des preuves](preuves/SHA256SUMS) |
| F8 | GitHub indique `verified: false`, raison `unsigned`, pour le commit applicatif. La base est `verified: true`. La procédure du mainteneur rejoue chaque commit avec sa signature en vérifiant son arbre et prépare une branche de remplacement. [Script](../../../scripts/architecture/preparer-signature.sh) · [Commandes](COMMANDES.md) |

Deux essais négatifs ont été réalisés sur des copies jetables. La suite Memory autonome échoue désormais sur la version de prompt modifiée. Le catalogue PostgreSQL détecte la colonne et l’index ajoutés à la base d’essai. [Témoin Memory](preuves/temoin-memoire.log) · [Témoin SQL](preuves/temoin-schema.log)

## Suites autonomes

| Domaine | Cas réussis | Preuves |
|---|---:|---|
| Configuration | 3 | [Journal](preuves/extractions/configuration.log) · [Identités](preuves/extractions/configuration.cases) |
| Context | 23 | [Journal](preuves/extractions/context.log) · [Identités](preuves/extractions/context.cases) |
| Contracts | 12 | [Journal](preuves/extractions/contracts.log) · [Identités](preuves/extractions/contracts.cases) |
| Conversations | 31 | [Journal](preuves/extractions/conversations.log) · [Identités](preuves/extractions/conversations.cases) |
| Documents | 29 | [Journal](preuves/extractions/documents.log) · [Identités](preuves/extractions/documents.cases) |
| Indexing | 12 | [Journal](preuves/extractions/indexing.log) · [Identités](preuves/extractions/indexing.cases) |
| Inference | 59 | [Journal](preuves/extractions/inference.log) · [Identités](preuves/extractions/inference.cases) |
| Memory | 7 | [Journal](preuves/extractions/memory.log) · [Identités](preuves/extractions/memory.cases) |
| Platform | 1 | [Journal](preuves/extractions/platform.log) · [Identités](preuves/extractions/platform.cases) |
| Retrieval | 31 | [Journal](preuves/extractions/retrieval.log) · [Identités](preuves/extractions/retrieval.cases) |
| Tools | 13 | [Journal](preuves/extractions/tools.log) · [Identités](preuves/extractions/tools.cases) |
| Workspaces | 13 | [Journal](preuves/extractions/workspaces.log) · [Identités](preuves/extractions/workspaces.cases) |

**Total des domaines**  234. **Cas exécutés uniquement au maître**  105 intégrations Chat et 63 cas du client Avalonia. Les totaux des campagnes d’extraction et du maître se recouvrent. Le champ `baselineTestsPresent` de la comparaison historique HTTP/RAG se rapporte au jalon D00. La correspondance des 400 cas de la base signée est établie séparément dans [TESTS.tsv](TESTS.tsv).

## Conditions et limites

Le test ONNX utilise le modèle Horizon sur CPU avec poids et tokenizer vérifiés. La recette HTTP pilote un fournisseur compatible Ollama contrôlé. Docker Compose utilise le Dockerfile livré et un moteur dans une VM Alpine. L’image SDK du banc emploie le certificat de confiance du proxy de recette. PostgreSQL 17.11 et pgvector 0.8.0 ont exercé les transactions sur des bases jetables.

Le portage natif Windows est différé. Le SDK Linux arm64 est épinglé et son installation native reste à vérifier. GPU et KDE/Wayland n’ont pas été rejoués. La validation des extractions utilise des répertoires autonomes. Les douze dépôts distants, les gitlinks, la CI/CD et la publication NuGet restent hors du périmètre livré. Toute publication future demande une décision explicite sur les droits de redistribution. La signature reconnue par GitHub reste une intervention du mainteneur. [Guide d’assemblage](ASSEMBLAGE.md)
