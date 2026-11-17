# Rapport de phase 03

Départ : `614bb0b`. Dérivés locaux additionnels, aucun changement du schéma SQL.

| Étapes | Réalisation et limites |
| --- | --- |
| 03.1 | analysisRunId, périmètre et empreinte figés ; états running, partial, completed, failed, cancelled |
| 03.2 | Résumés atomiques par fichier et inventaire ; clés source, modèle/digest, options, budget et versions |
| 03.3 | Reprise explicite par API et bouton ; unités compatibles réutilisées, digest inconnu interdisant la réutilisation |
| 03.4 | Inventaire, résumés par fichier et réduction récursive existante ; manifeste avec couverture par format |
| 03.5 | PDF par page, limite de 200 pages et 200 000 octets ; pages sans texte signalées implicitement par complétude fausse, sans OCR |
| 03.6 | DOCX blocs et lignes de tableaux, PPTX parties de diapositives, XLSX cellules/formules/valeurs mises en cache |
| 03.7 | C# Roslyn conservé ; autres langages en découpage textuel explicite, sans nouvelle dépendance |
| 03.8 | État, compteur de fichiers, réutilisation et action Reprendre dans Analyses |
| 03.9 | Pas de migration nécessaire ; checkpoints dans le stockage local sauvegardé avec les originaux |

Les Office sont volontairement déclarés partiels : mise en page, en-têtes, notes,
graphiques, ordre réel de présentation et calcul des formules ne sont pas reconstruits.
Les références de feuilles utilisent le chemin XML original et la cellule, par
exemple `xl/worksheets/sheet1.xml!B2`. Aucune formule ni macro n'est exécutée.
Les fichiers DOCX atypiques gardent un repli textuel nommé `fallback`.
Les index sont reconstruits avec la nouvelle version du découpage.

Une reprise refuse un périmètre, une empreinte, un budget ou une identité de modèle
différents. Démarrer alors une nouvelle analyse : les unités inchangées restent
réutilisables. En l'absence de digest, la reprise recalcule les résumés plutôt que
supposer leur compatibilité. L'inventaire et le résumé global peuvent nécessiter
des appels supplémentaires ; les compteurs réels du run restent distincts du cache.
Après un commit SQL suivi d'une perte d'accusé de réception, reprendre le même
identifiant retrouve le rapport déjà enregistré sans créer un doublon.

API : GET `/api/chat/conversations/{id}/analysis-runs` ; POST analyses avec
`resumeRunId`. Maximum 50 métadonnées d'exécution par conversation ; DELETE
`/api/chat/conversations/{id}/analysis-runs/{runId}` libère un point de reprise,
sans supprimer rapport final, unité dérivée ou original. Les erreurs persistantes
de disque peuvent empêcher le dernier état d'être enregistré ; les unités atomiques
antérieures restent disponibles. Un processus interrompu peut laisser l'état running,
également reprenable depuis l'interface.

Tests : arrêt/reprise et isolation du stockage, annulation atomique, corruption,
invalidation SHA/digest, extraction XML hostile, tables/formules, PDF réel à deux
pages. Test PostgreSQL de reprise/idempotence ajouté au scénario existant.
État **PARTIEL** tant que la recette PostgreSQL et l'inférence réelle ne sont pas
exécutées. Aucun gain de qualité ni de temps d'inférence annoncé.

Les identifiants de passage utilisent désormais `coordinates-v2` pour éviter de réutiliser une ancienne identité après changement d’extraction. Les diagnostics déjà enregistrés restent lisibles. Les contrôles ont détecté puis corrigé deux incompatibilités d’extraction : repli DOCX atypique et libellé historique des cellules XLSX, sans changer leurs assertions.

Commit de livraison : `09203f1dcb4717d61168e72f4dde1fef4267d366`.

Consolidation ultérieure : la phase 05 a exécuté les quatre tests PostgreSQL et
la recette HTTP/API/MCP réelle (fournisseur simulé). Les résultats ci-dessus
décrivent le checkpoint initial ; voir `phase-05-results.json` dans `docs/validation`
pour la validation cumulée. La campagne qualitative Ollama reste non exécutée.
