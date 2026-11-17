# Rapport de phase 02

Départ : `4700bfe`. Même branche autorisée. Pas de migration.

| Étapes | Réalisation |
| --- | --- |
| 02.1 | Scores nuls filtrés ; seules les sources explicitement épinglées peuvent revenir sans correspondance |
| 02.2 | Doublons textuels supprimés et pénalité progressive par fichier ; BM25 et fusion hybride conservés |
| 02.3 | Synthèse d'orientation et originaux présents ensemble ; texte d'orientation plafonné au quart du budget |
| 02.4 | Références, identités, SHA, plages, type, complétude, score lexical et raison dans les contrats |
| 02.5 | project_search_passages, project_find_symbols, project_find_references, project_read_many |
| 02.6 | Consigne de plan de lecture ; traces persistantes des passages fournis ; couverture indexée distincte de compréhension |
| 02.7 | Appels, octets et durée observés ; limites, annulation et prévention des répétitions existantes conservées |
| 02.8 | Catalogue commun API/MCP/Ollama ; métadonnées de preuves multiples propagées ; trois outils anciens conservés |
| 02.9 | Tests déterministes zéro résultat, épinglage, contexte composite et budget ; essais PostgreSQL ajoutés |

Le plan est une consigne guidant le modèle, pas une preuve de compréhension ni
un ordonnanceur autonome persistant. Les références de symboles sont syntaxiques
et peuvent inclure des homonymes. Les autres langages restent textuels.
Les scores sémantiques positifs ne prouvent pas la pertinence factuelle.

Les nouvelles recherches passent par le même index, filtré par conversation et
exclusions. La pagination est par résultat pour ces recherches, par fichier pour
l'ancien project_search. read_many accepte quatre lectures au plus ; réduire le
nombre si l'enveloppe JSON et les lectures dépassent le budget global.
Les preuves multiples sont auditées comme celles des lectures simples.

La comparaison avec le comportement V4 est possible avec un checkout séparé du
commit initial ; le réglage legacy conserve aussi la sélection historique.
Les gains de rappel, de qualité et d'appels utiles restent **NON MESURÉS** sur
Ollama. Le corpus de validation n'a pas été modifié après la phase 01.

État : **PARTIEL** pour la recette avec modèle et PostgreSQL. Voir
`docs/validation/phase-02-results.json` pour les contrôles effectivement exécutés.

Compilation complète et lint réussis. Tests concernés : 125 Chat et 44 client réussis, quatre PostgreSQL ignorés. Les 11 tests Workspaces avaient réussi avant la correction du contrat du catalogue.

Commit de livraison : `614bb0b05c57a2e9bc4636ef7583543876640e2e`.

Consolidation ultérieure : la phase 05 a exécuté les quatre tests PostgreSQL et
la recette HTTP/API/MCP réelle (fournisseur simulé). Les résultats ci-dessus
décrivent le checkpoint initial ; voir `phase-05-results.json` dans `docs/validation`
pour la validation cumulée. La campagne qualitative Ollama reste non exécutée.
