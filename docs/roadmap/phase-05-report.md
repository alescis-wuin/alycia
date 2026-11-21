# Rapport de phase 05

Départ : `cdb5feef53ef0651bc3e8847506ce03edef9fe3e`.
Branche : `feature/local-client-v4-context-quality-20261117`.
Pas de migration SQL, service externe ou dépendance de production supplémentaire.

| Étape | État | Réalisation |
| --- | --- | --- |
| 05.1 Profilage | Partiel | p50/p95 BM25/classement sur trois tailles ; extraction/segmentation de quatre formats ; temps d'import/aperçu et premier fragment instrumentés ; inférence réelle non mesurée |
| 05.2 Cache lexical | Fait | Fréquences, longueurs et statistiques réutilisées ; quatre conversations et huit millions de caractères au maximum ; invalidation par contenu et provenance |
| 05.3 Cache vectoriel | Fait | Format v2 dans vectors-v1.json ; SHA/versions/digest ; aucune réécriture à chaud ; corruption et dimensions incohérentes déclenchent recalcul |
| 05.4 Hybride | Étude limitée | Fusion conservée ; aucun préfiltre lexical supprimant les correspondances sémantiques ; décision de reranking différée faute de campagne réelle |
| 05.5 Concurrence | Fait | Lots d'embeddings existants de 16 conservés ; un lot de recherche vectorielle à la fois ; génération/embeddings sérialisés dans l'API et attente annulable |
| 05.6 Diagnostics | Fait | Temps, cache, décisions regroupées, premier fragment, preuves et audit accessibles dans les détails existants |
| 05.7 Ergonomie | Fait sur environnement disponible | Dialogues ajustés au propriétaire ; scroll, actions persistantes, Tab et Échap ; rendu Skia à 1240×880 et 960×700 |
| 05.8 Consolidation | Partiel pour la qualité | Recette PostgreSQL/API/MCP, contrôles et guides ; comparabilité et legacy préservés ; A/B Ollama non exécuté |

Le classement utilise une file de priorité par fichier. Il conserve l'ordre de la
pénalité de diversité de la phase 04, y compris les doublons et égalités. Les scores
BM25 sont comparés bit à bit à l'algorithme de `0b31f13`; l'ordre est comparé à
`cdb5fee` sur quatre requêtes par taille, dont une sans correspondance.

Mesures CPU synthétiques, 24 répétitions, empreinte complète incluse dans l'accès
au cache. Environnement Debian 13, .NET 10.0.0, neuf processeurs logiques exposés :

| Passages / fichiers | BM25 p50 avant → cache (ms) | Classement p50 phase 04 → phase 05 (ms) | Classement p95 avant → après (ms) |
| --- | --- | --- | --- |
| 64 / 16 | 16,22 → 0,21 | 17,60 → 0,42 | 20,74 → 0,67 |
| 512 / 128 | 66,89 → 1,36 | 72,95 → 2,33 | 85,39 → 6,43 |
| 2048 / 512 | 275,51 → 6,02 | 376,61 → 10,19 | 444,09 → 20,21 |

Ces chiffres ne mesurent ni le temps total d'une réponse ni la qualité de récupération
sur le holdout. Le jeu synthétique de 512 fichiers constitue un test de charge CPU,
indépendant de la limite d'import de 500 entrées par lot. Le premier index construit
inclut le JIT ; les caches chauds nécessitent une requête antérieure compatible.
L'allocation BM25 mesurée à 2048 passages passe d'environ 112,8 Mo à 5,4 Mo par appel ;
ce n'est pas la mémoire résidente totale du processus. Les résultats bruts sont dans
`../validation/phase-05-benchmark.json` et `../validation/phase-05-documents.json`.

La réserve des neuf outils a révélé un dépassement de la fenêtre par défaut avec
le compteur conservateur. Les consignes communes ont été raccourcies sans réduire
les contraintes ni augmenter le contexte. Un test protège ce cas. Les erreurs de
paramètre/budget après début du flux sont désormais explicites ; les journaux
n'enregistrent que type et pile pour le diagnostic, pas le texte des prompts.

Limites : sérialisation limitée à ce processus ; elle ne borne pas la résidence des
modèles Ollama ni les autres clients. La recherche historique reconstruit encore son
index borné à chaque scan. Les caches d'unités d'analyse ne disposent pas encore d'un
quota disque ou d'un nettoyage des versions anciennes ; ils sont reconstructibles,
sauvegardés avec les originaux et supprimés avec leur conversation.

Les contrôles effectivement exécutés et corrections sont consolidés dans
`../validation/phase-05-results.json` et [le compte rendu du programme](program-report.md).
État **PARTIEL** : implémentation et contrôles locaux livrés ; validation qualitative
Ollama, KDE/Wayland, Windows et RTX 4070 **NON EXÉCUTÉE**.

Contrôles consolidés : 200 tests .NET distincts, 10 Python et 19 groupes HTTP réussis ; quatre tests PostgreSQL effectivement exécutés. Les relances ont été limitées aux contrôles touchés par les corrections.
