# V4 — qualité à budget de contexte constant

Branche : `feature/local-client-v4-context-quality-20261117`, dérivée de la V3
`228fde1996b5b0d7d5a04637dd2d9318c8970095`. Publication autorisée par le propriétaire.

Une phase correspond à un lot livrable, testé puis commité avant le suivant.
Les fonctionnalités précédentes restent disponibles ; la V3 demeure une référence
de comparaison. Les gains qualitatifs avec un vrai modèle ne seront pas déduits
des seuls tests logiciels.

| Phase | Étapes | Validation attendue |
|---|---|---|
| A — évaluation | 1. Figer corpus/questions/références ; 2. séparer réglage et validation ; 3. exécuter via l’API ; 4. comparer qualité et coût | Empreintes, nettoyage, comparabilité et absence de résultats simulés présentés comme qualité réelle |
| B — budget et mesures | 1. Compter tous les appels ; 2. tokenisation compatible pour les modèles pris en charge ; 3. repli prudent explicite ; 4. afficher budget et coût cumulé | Unicode, modèle inconnu, compatibilité, appels de résumé et d’outils comptabilisés |
| C — structure | 1. Indexer symboles et unités de code ; 2. conserver lignes/empreintes ; 3. relier références et tests ; 4. réutiliser les sources inchangées | Frontières, original complet, invalidation et isolation |
| D — recherche | 1. BM25 ; 2. recherche sémantique locale optionnelle ; 3. fusion/reclassement/déduplication ; 4. sélection sous budget | Pertinence, diversité et plafonds ; comparaison de la voie sémantique sans supposer un gain |
| E — mémoire | 1. Structurer faits/décisions/contraintes/questions ; 2. référencer les messages ; 3. gérer corrections ; 4. récupérer la mémoire pertinente | Provenance, contradictions, conservation du bloc protégé et compatibilité historique |
| F — exploration | 1. Lire par symbole ; 2. détecter les appels répétés ; 3. garder les résultats utiles ; 4. compacter uniquement sous pression | Moins de résumés inutiles, arrêt des boucles et budget protégé |
| G — réponses et livraison | 1. Références de sources vérifiables ; 2. contrôle des références ; 3. profils courts ; 4. UI, recette et guide | Référence inexistante signalée, absence de preuve distincte d’une erreur factuelle, contrôles finaux |

Les outils restent en lecture seule sur les imports. Une référence valide n’atteste
pas à elle seule la vérité d’une affirmation. L’estimation du format de conversation
et les modèles non pris en charge restent identifiés explicitement.
