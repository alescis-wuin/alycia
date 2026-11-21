# Alycia — compte rendu des cinq phases

17 novembre 2026. Code développé et livré par phases successives sur
`feature/local-client-v4-context-quality-20261117`. Le nom sans préfixe indiqué
dans la demande correspond à cette branche existante. Aucune autre branche fusionnée.

**État : implémentation livrée, validation expérimentale PARTIELLE.**
Les contrôles locaux, PostgreSQL, API/MCP et interface simulée sont réussis après
corrections. Aucun gain de qualité des réponses Ollama ou de performance GPU n'est
établi. Le [guide d'exécution et d'utilisation](local-execution-guide.md) couvre
votre environnement CachyOS/KDE/fish et la recette restant à effectuer.

## Base et analyse

Document analysé : `ALYCIA_Compte_rendu_et_roadmap(1).docx`, feuille de route datée du
17 novembre 2026, cinq phases et 42 étapes. Sa référence `58418f9` précédait un
correctif déjà présent sur la branche : le point de départ réellement utilisé est
`0b31f13c76a8e8075d798f76c30cc2f3438e9b0e`. Ce correctif a été conservé.
L'instruction courante demandant une même branche a prévalu sur la proposition de
branches dérivées du document.

Le projet conserve son architecture .NET/C#, Avalonia, PostgreSQL/EF et Ollama local.
La V4 possédait déjà mémoire récursive/structurée, BM25/hybride, découpage Roslyn,
imports et analyses globales, trois outils et contrôle des références. Les défauts
traités portent sur le contenu réellement fourni au modèle et sa traçabilité :
scores nuls retenus, synthèse remplaçant les preuves, exploration trop générale,
analyses interrompues non reprenables, décisions anciennes difficiles à relire,
extractions aplaties et recalcul lexical répété.

## Phases et traçabilité

| Phase | Livré | Commit / dossier |
| --- | --- | --- |
| 01 Évaluation | Corpus séparé, scénarios longs, métriques explicites, comparaison durcie, instrumentation | `4700bfe` · [rapport](phase-01-report.md) · [protocole](phase-01-evaluation.md) |
| 02 Recherche | Filtrage, diversité, synthèse + originaux, quatre outils ciblés, traces de couverture | `614bb0b` · [rapport](phase-02-report.md) |
| 03 Analyse | Points de reprise par fichier, cache versionné, reprise UI/API, extraction structurée PDF/Office | `09203f1` · [rapport](phase-03-report.md) |
| 04 Mémoire | Recherche/lecture historique, rôles et corrections, invalidation du registre, audit heuristique | `cdb5fee` · [rapport](phase-04-report.md) |
| 05 Optimisation | Caches, classement efficace, mesure du premier fragment, concurrence Ollama, dialogues et recette consolidée | Commit contenant ce dossier (`git rev-parse HEAD`) · [rapport](phase-05-report.md) |

Les rapports de phases détaillent les 42 étapes et leur état. Les passages restant
partiels sont identifiés : rappel par passage exact, campagnes de réponses longues,
comparaison réelle legacy/lexical/hybrid, examen humain du holdout et performances
matérielles. Les limites de validation des premiers rapports sont des checkpoints
historiques ; PostgreSQL et l'interface ont ensuite été vérifiés pendant la phase 05.

## Ce qui change à l'usage

- La recherche lexicale écarte les fragments sans correspondance et les doublons.
  La diversité évite qu'un seul fichier monopolise les premiers résultats. Les
  sources épinglées restent une exception explicite ; un score sémantique positif
  ne démontre pas à lui seul la pertinence d'un passage.
- Une analyse activée fournit une orientation limitée, tandis que les extraits
  originaux pertinents restent disponibles. Les types, empreintes, plages et
  raisons de sélection sont conservés.
- Le catalogue contient neuf outils de lecture, partagés entre MCP et l'agent :
  inventaire, recherche littérale, lecture, recherche classée, symboles, références
  syntaxiques, lecture groupée, recherche historique et lecture historique.
- Une analyse interrompue reprend les fichiers terminés compatibles. Un changement
  d'empreinte, version, digest, options ou budget invalide les dérivés concernés.
  Les rapports anciens et les originaux ne sont pas remplacés.
- Les PDF conservent des repères de pages ; DOCX des blocs/tableaux ; XLSX les
  cellules, formules et valeurs XML ; PPTX les diapositives. Les extractions
  partielles restent signalées. Les autres langages de code restent textuels.
- Les décisions sorties du registre de 256 entrées peuvent être recherchées dans
  les messages originaux. Les références historiques conservent rôle, séquence,
  date, empreinte et plage ; une proposition de l'assistant n'est pas une validation
  utilisateur.
- L'audit signale citations directes, recouvrement lexical, incertitudes et absence
  de preuve. Il n'est ni un vérificateur de vérité ni un juge indépendant.
- Les statistiques lexicales sont réutilisées, le cache vectoriel n'est plus réécrit
  sans changement, les appels Ollama sont sérialisés dans l'API. Les diagnostics
  présentent temps, caches, preuves et omissions ; les dialogues s'adaptent à leur
  fenêtre propriétaire et restent utilisables au clavier.

## Vérifications exécutées

| Contrôle | Résultat constaté |
| --- | --- |
| Compilation | Solution puis projets concernés après corrections ; zéro avertissement/erreur dans les derniers builds |
| Format/lint | Réussi |
| Modèle EF | Aucune différence nécessitant une migration |
| Tests .NET | **200 tests distincts réussis** : 145 Chat, 44 client, 11 Workspaces |
| PostgreSQL | Les quatre tests d'intégration exécutés sur bases isolées, pas ignorés au bilan final |
| Python | **10 tests réussis** |
| Recette API/MCP | **19 groupes réussis**, API et PostgreSQL réels, fournisseur Ollama simulé |
| Corpus | context-v1 : 8 fichiers / 36 questions ; context-v2 : 70 fichiers / 16 scénarios ; empreintes inchangées |
| Interface | Rendu Avalonia/Skia à 1240×880 et 960×700, dialogue d'analyse compact, Tab et Échap |
| Benchmarks | 24 répétitions sur trois tailles ; égalité des scores et ordre vérifiés ; quatre formats documentaires chronométrés |

Les 200 résultats sont consolidés par identifiant de test à partir des suites et
relances ciblées après correction : ce chiffre ne décrit pas une invocation unique
finale. Aucune erreur non résolue dans ces contrôles. Les détails publics sont dans
[les résultats de phase 05](../validation/phase-05-results.json),
[les cas de test](../validation/phase-05-tests.json) et
[la recette HTTP](../validation/phase-05-http.json).

La recette vérifie notamment migrations, idempotence, pagination, exports,
conversations protégées, exclusions, originaux au-delà du premier extrait importé,
lectures groupées, provenance historique et réutilisation des embeddings. Le test
PostgreSQL d'analyse vérifie annulation après unité terminée, reprise et absence de
rapport en double. Les tests de cache couvrent changement de contenu/digest,
corruption, dimensions, éviction et séparation des conversations.

Incidents corrigés pendant la validation : anciennes assertions de catalogue à
mettre à jour, simulation de fin de génération incomplète, événement clavier envoyé
après fermeture du dialogue et réserve excessive des neuf outils au budget par
défaut. Ce dernier défaut a été corrigé en raccourcissant les consignes communes,
sans augmenter la fenêtre ni affaiblir les contrôles de budget. Le serveur de test
PostgreSQL a dû être maintenu dans le même environnement d'exécution que ses clients.

## Mesures et interprétation

Sur un microbenchmark CPU synthétique de 2048 passages, le classement passe de
**376,61 à 10,19 ms en médiane**, et de **444,09 à 20,21 ms au p95**, par rapport au
classement de phase 04. Le calcul BM25 de référence V4 passe de 275,51 à 6,02 ms en
médiane avec cache. Les requêtes et l'ordre attendu sont vérifiés ; le calcul de
l'empreinte du cache est compris dans les temps. Résultats :
[benchmark brut](../validation/phase-05-benchmark.json).

Il s'agit de préparation CPU sur Debian 13/.NET 10, pas d'une mesure sur votre
portable. Ces chiffres n'établissent pas l'accélération complète d'une réponse.
L'extraction/segmentation a aussi été mesurée sur les quatre petits documents
synthétiques figés : [résultats](../validation/phase-05-documents.json). Aucune
extrapolation à des PDF complexes ou de grands classeurs n'est faite.

La télémétrie mesure l'import/aperçu via le pilote, index et classement via les
diagnostics, embeddings/résumés/génération via les appels, outils via les traces,
et premier fragment textuel observé par appel. Le premier fragment n'est pas le
premier token réseau exact et inclut une éventuelle attente locale. Un compteur
absent reste indisponible ; une estimation UTF-8 reste une estimation.

## Compatibilité et limites restantes

Aucune migration SQL ajoutée, aucune modification des messages originaux. Les
champs de diagnostics sont additifs. Les références de coordonnées et les caches
portent de nouvelles versions ; les anciens diagnostics enregistrés restent
consultables. Le dossier d'unités d'analyse participe à la sauvegarde de fichiers
existante. Aucune sauvegarde utilisateur ni base de production n'a été manipulée.

Limites fonctionnelles explicites :

- Le plan d'exploration est une consigne au modèle, avec traces des lectures ; ce
  n'est pas un ordonnanceur persistant certifiant une analyse exhaustive.
- Le rappel automatique est mesuré au niveau fichier. Un score au niveau passage
  exact et une revue humaine des faits restent à établir.
- Reprise par fichier, pas par fragment interne ; la réduction finale peut être
  recalculée. Sans digest, la réutilisation de résumés est désactivée. Pas de quota
  disque ni de collecte des anciennes versions d'unités ; 50 métadonnées d'exécution
  au maximum, suppression dédiée par API seulement.
- L'historique est scanné par fenêtres bornées/paginées. Seuls les séparateurs de
  clés sont normalisés ; les synonymes et contradictions implicites ne sont pas
  résolus automatiquement.
- L'audit couvre au plus 64 blocs et conserve ses omissions ; il peut manquer une
  négation ou une relation logique malgré un recouvrement lexical élevé.
- Pas d'OCR, de macros/formules exécutées, de graphe sémantique C# complet ni de
  parseur spécialisé ajouté pour les autres langages. La complétude PDF désigne
  l'extraction textuelle des pages traitées, pas la compréhension de leur mise en page.
- La sérialisation Ollama ne gouverne que ce processus d'API. Aucun gain VRAM,
  aucune garantie de chargement simultané ou de taille de modèle n'est annoncé.

**NON EXÉCUTÉ :** campagnes qualitatives avec Ollama réel, comparaison humaine du
holdout, mesures RTX 4070/VRAM, recette KDE/Wayland, Windows, déploiement Compose et
restauration Docker sur votre machine. La sauvegarde/restauration Python est couverte
par des tests simulés ; elle n'a pas été exécutée sur vos volumes.

Le parcours restant consiste à installer cette branche, effectuer la recette avec
votre modèle, puis les campagnes comparables et leur revue humaine décrites dans
[le guide](local-execution-guide.md). La seule disponibilité des nouvelles fonctions
ne permet pas de conclure à une hausse de justesse à contexte égal.
