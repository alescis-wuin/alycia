# Alycia V4 — compte rendu du 17 novembre 2026

Correctif ultérieur : [interruption après des appels d’outils](Local-Client-V4-Hotfix-Tools.md).
Les résultats ci-dessous décrivent la livraison V4 initiale ; la validation ciblée
du correctif est consignée séparément.

Les sept phases ont été réalisées successivement sur
`feature/local-client-v4-context-quality-20261117`, dérivée du commit V3
`228fde1996b5b0d7d5a04637dd2d9318c8970095`. Le [guide](Local-Client-V4-Guide.md)
contient les commandes de mise à jour, les usages et le protocole de comparaison.

## Phases livrées

| Phase | Résultat | Commit GitHub |
|---|---|---|
| A — évaluation | 8 sources figées, 36 questions dont 12 de validation ; exécution API, indicateurs à revoir humainement, contrôle des budgets et identités | [9b32458](https://github.com/alescis-wuin/alycia/commit/9b3245837f32409af7d3466d83fade9e6e96c8b3) |
| B — mesures | Compteur BPE Qwen2 compatible, repli explicite, mesure des résumés/outils/générations, identité de modèle et diagnostics persistants | [ac537b8](https://github.com/alescis-wuin/alycia/commit/ac537b82806e94f191159a1c80d4d9030ee866dc) |
| C — structure | Découpage syntaxique C#, symboles, références, lignes et empreintes ; cache incrémental, inspection de couverture | [54e48b0](https://github.com/alescis-wuin/alycia/commit/54e48b0b93d10da9c9b7d20c7eb7cee7bf3f92a7) |
| D — recherche | BM25, embeddings locaux optionnels, fusion de rangs, bonus structurel, déduplication et sélection sous budget | [8a0dbac](https://github.com/alescis-wuin/alycia/commit/8a0dbac9ff1bd222b2a77a9564e1e9928c40edd3) |
| E — mémoire | Citations exactes classées, provenance des messages, variantes et corrections explicites, mémoire pertinente et bornée | [a23efba](https://github.com/alescis-wuin/alycia/commit/a23efbab2e526fd54e7863bd3f754e8db15ec6b1) |
| F — outils | Lecture par symbole, ambiguïtés explicites, arrêt des répétitions, conservation des résultats jusqu’à pression du budget | [2635ee7](https://github.com/alescis-wuin/alycia/commit/2635ee7f94e154aa5283bba817947eb220a97c5a) |
| G — réponses et livraison | Références contrôlées, diagnostics dans l’historique, trois profils courts, tests d’intégration et documentation | Commit portant ce compte rendu |

Chaque phase reprend la précédente sur la même nouvelle branche. Aucun changement
n’a été fusionné dans la branche V3 ou dans la branche principale.

## Comportements vérifiés

- Du code situé après les 200 000 premiers octets est retrouvé par l’index,
  sélectionné pour le contexte, recherché, lu par symbole et analysé intégralement.
- Les exclusions et l’isolation des conversations restent appliquées aux sources.
- Les derniers échanges, le système, les notes et la demande restent protégés
  lorsque la mémoire automatique est active. Un budget impossible est signalé.
- Une citation de mémoire inventée est rejetée. Les variantes restent groupées ;
  seule une correction explicite de l’utilisateur écarte les anciennes variantes.
- L’historique original n’est pas écrasé. Une génération échouée ou annulée ne
  persiste pas de nouvelle mémoire ni d’échange incomplet.
- La voie hybride HTTP vérifie le modèle local, refuse la troncature implicite,
  met les vecteurs en cache et comptabilise le coût des aperçus.
- Les petits cycles d’outils ne déclenchent plus de résumé systématique. Un appel
  identique avec des arguments réordonnés ne provoque pas de nouvelle lecture.
- Les références de sources sont reliées aux passages fournis et leurs plages
  exactes. Les références inconnues sont signalées, sans prétendre juger la vérité.
- Les diagnostics survivent au rechargement, au rejeu d’une requête et à l’export.

## Validation exécutée

Linux x64, SDK .NET **10.0.404**, runtime **10.0.15**, PostgreSQL **16.2** isolé.
Aucun modèle Ollama réel ni GPU n’a été lancé. Les recettes HTTP utilisent la
vraie API et la vraie base avec un fournisseur Ollama simulé.

| Contrôle final | Résultat |
|---|---|
| Compilation de la solution | 0 avertissement, 0 erreur |
| Tests du client Avalonia | 43 réussis |
| Tests Chat, dont intégration PostgreSQL | 117 réussis |
| Tests Workspaces | 11 réussis |
| Total .NET, suite complète sans double comptage | **171 réussis, 0 échec, 0 ignoré** |
| Tests Python de sauvegarde et d’évaluation | **8 réussis** |
| Recette HTTP, MCP, mémoire, références et embeddings | **16 groupes réussis**, données de recette nettoyées |
| Modèle EF / migrations | Aucun changement sans migration ; 8 migrations Chat |
| Corpus figé | Empreintes des 8 sources et des 36 questions validées |
| Interface | Rendu Avalonia/Skia inspecté en 1 240 × 880 et 960 × 700 ; diagnostics manipulés par le test UI |
| Format et lint | Contrôlés sur les fichiers modifiés |

Les [résultats détaillés](validation/Local-Client-V4-Checks.json) contiennent les
noms des tests, leurs résultats et les empreintes des fichiers de code concernés.
Les tests de phase précédents se recoupent avec cette suite : ils ne sont pas
additionnés au total de 171. Le diagnostic non bloquant du lanceur concernant
`VSTEST_ARTIFACTS_DIRECTORY` reste présent.

Le compteur BPE a été confronté à dix exemples français, code et CJK calculés
indépendamment avec Hugging Face `tokenizers` 0.22.2 et un vocabulaire Qwen2.5.
Le jeu réduit et sa provenance sont conservés dans les tests. Cela ne valide pas
l’ensemble des modèles ni l’exactitude universelle des gabarits de chat.

## Portée et limites

**Aucun gain de qualité chiffré n’est annoncé.** Les changements améliorent les
mécanismes du client ; les poids du modèle ne sont pas modifiés. Les effets réels
sur la justesse, les omissions, la latence et le coût cumulé nécessitent les essais
A/B du guide sur le poste, avec le même modèle, sa quantification et son digest.

- Le budget est une fenêtre maximale, pas une garantie de coût cumulé égal.
  Les résumés, extractions, embeddings et tours d’outils consomment aussi du calcul.
- Le tokenizer pris en charge est limité au format Qwen2 reconnu dans les
  métadonnées installées. Les autres cas restent estimés prudemment.
- L’index C# est syntaxique. Pas de compilation du projet importé, ni de résolution
  sémantique complète des appels ; les autres langages utilisent des blocs textuels.
- BM25 et les embeddings ne garantissent pas le rappel de tous les éléments utiles.
  Le reclassement structurel est heuristique, sans reranker neuronal.
- L’index est borné à 8 millions de caractères ; l’hybride à 2 048 passages. Les
  fichiers exclus ou trop volumineux figurent dans les omissions. L’analyse
  systématique V3 reste disponible pour un parcours global des sources admissibles.
- La mémoire est avec perte : 256 citations maximum, sélection sous budget,
  replis explicites et contradictions implicites non résolues automatiquement.
  Les originaux sont conservés. L’export de conversation reconstruit la mémoire.
- Une référence reconnue atteste un passage fourni, pas que ce passage démontre
  l’affirmation. Aucun juge automatique ni boucle de réécriture n’est activé.
- Les outils sont en lecture seule sur les imports. Pas d’exécution de terminal,
  d’écriture arbitraire ni de gestionnaire de serveurs MCP tiers ajouté.
- Docker, PostgreSQL 17, Windows, le bureau KDE natif et les performances de la
  RTX 4070 n’ont pas été exécutés dans cet environnement. La recette native reste
  à faire sur la machine. Les réglages existants et leurs volumes doivent être conservés.

## Références techniques

L’implémentation utilise les champs documentés de l’API Ollama et l’analyse
syntaxique Roslyn. Les inspirations de recherche ne constituent pas des résultats
mesurés pour Alycia :

- [Ollama : sorties structurées](https://docs.ollama.com/capabilities/structured-outputs)
- [Ollama : mesures d’utilisation](https://docs.ollama.com/api/usage)
- [Roslyn : analyse syntaxique](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/get-started/syntax-analysis)
