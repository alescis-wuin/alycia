# Rapport de phase 01

État : **PARTIEL pour la validation expérimentale**. Développement du dispositif
livré sur la branche autorisée, depuis `0b31f13`.

| Étapes | État | Preuve ou limite |
| --- | --- | --- |
| 01.1 inventaire | Fait | Identité et compteurs existants ; classement et temps ajoutés |
| 01.2 corpus | Fait | context-v1 intact ; context-v2 70 fichiers et 16 scénarios |
| 01.3 instrumentation | Fait | RetrievalReport sérialisable, traces sans contenu brut |
| 01.4 métriques | Partiel | Rappel/précision/nDCG par fichier ; passage exact non mesuré |
| 01.5 historique | Partiel | Runner multi-tours et erreur explicite ; exécution Ollama non disponible |
| 01.6 comparaison | Partiel | Protocole durci, diff des réponses ; aucune campagne réelle ici |
| 01.7 critères | Fait | Invariants bloquants et grille humaine documentés |
| 01.8 livraison | Fait | Scripts, corpus, tests, guide et résultats versionnés |

Vérifications exécutées : dix tests Python réussis, deux manifests vérifiés,
compilation complète réussie (.NET 10.0.100), lint corrigé sur les fixtures puis réussi,
schéma EF sans dérive, 178 tests .NET réussis et quatre tests PostgreSQL ignorés.
Pas de changement de classement à cette phase, pas de migration DB.

Les essais simulés ne constituent pas une baseline de qualité du modèle.
Ollama, PostgreSQL, Windows, KDE et RTX 4070 : **NON EXÉCUTÉ** à ce checkpoint.
L'absence de ces environnements n'empêche pas le développement des lots suivants,
mais interdit de déclarer un gain réel ou une validation de production.

Commit de livraison : `4700bfef005e7010106817fb36c3b8afd9033462`.

Consolidation ultérieure : la phase 05 a exécuté les quatre tests PostgreSQL et
la recette HTTP/API/MCP réelle (fournisseur simulé). Les résultats ci-dessus
décrivent le checkpoint initial ; voir `phase-05-results.json` dans `docs/validation`
pour la validation cumulée. La campagne qualitative Ollama reste non exécutée.
