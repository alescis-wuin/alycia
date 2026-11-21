# Rapport de phase 04

Départ : `09203f1`. Aucun changement SQL, les messages originaux ne sont pas modifiés.

| Étapes | Réalisation et limites |
| --- | --- |
| 04.1 | Index BM25 construit à la demande sur les originaux, borné à 4096 messages et 4 millions de caractères par scan |
| 04.2 | history_search et history_read via le catalogue MCP commun ; paires user/assistant, séquence, date, empreinte et offsets |
| 04.3 | Fenêtre protégée et registre existants conservés ; historique récupérable au-delà de 256 entrées |
| 04.4 | Normalisation des séparateurs de clés seulement ; variantes conservées, corrections utilisateur seules prioritaires |
| 04.5 | Identité du modèle et version de prompt intégrées au registre ; mémoire incompatible reconstruite depuis les originaux |
| 04.6 | Audit heuristique des blocs d'affirmations contre les textes effectivement fournis ; citations exactes, recouvrement lexical, incertitudes et absence de preuve |
| 04.7 | Consultation ciblée proposée dans les consignes et diagnostics si fondement absent ; pas de réécriture automatique de la réponse |
| 04.8 | Provenance historique et états d'audit dans les diagnostics ; tests sur 600 et 4200 messages synthétiques |

history_search pagine les résultats avec offset/nextOffset pour un scan fixé par
before, puis poursuit les scans avec nextBefore. Un scan sans correspondance peut
encore avoir une suite. history_read conserve les deux rôles et utilise userOffset
et assistantOffset pour la continuation ; les sorties restent sous le budget global.
Les références historiques ont la forme `[Hséquence:empreinte]`.

Les dérivés historiques ne sont pas persistés : pas de cache de messages périmé,
et l'export/import existant des originaux permet leur reconstruction. Le scan peut
être coûteux sur une très longue conversation ; ce coût reste borné et paginable.
Exclure un import n'efface pas rétroactivement ce que l'utilisateur avait écrit
dans ses messages ; les deux types de sources restent distincts.

L'audit traite au plus 64 blocs, indique les omissions et ne s'exécute qu'après une
génération réussie. `direct_quote` signifie texte retrouvé littéralement, jamais fait
vrai. `indirect_lexical` ne valide ni négation ni causalité. `uncertain` requiert une
revue, et `not_demonstrated` appelle une consultation ciblée. Les passages bruts
servant au contrôle ne sont pas ajoutés aux diagnostics persistants.

Aucun juge cloud, nouvel appel de modèle, mémoire entre conversations ou résolution
sémantique automatique des contradictions. Les synonymes non explicitement reliés
restent séparés. État **PARTIEL** pour l'évaluation qualitative Ollama et la recette
PostgreSQL ; résultats déterministes dans `docs/validation/phase-04-results.json`.

Commit de livraison : `cdb5feef53ef0651bc3e8847506ce03edef9fe3e`.

Consolidation ultérieure : la phase 05 a exécuté les quatre tests PostgreSQL et
la recette HTTP/API/MCP réelle (fournisseur simulé). Les résultats ci-dessus
décrivent le checkpoint initial ; voir `phase-05-results.json` dans `docs/validation`
pour la validation cumulée. La campagne qualitative Ollama reste non exécutée.
