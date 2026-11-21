# Phase 01 Évaluation reproductible

Le corpus context-v1 reste inchangé : 8 fichiers, 36 questions,
empreinte `3789bbeec4dda426dd5671f98ad6c511e60369238d8cdb5933890e67be1427d4`.
context-v2 ajoute 70 fichiers synthétiques et 16 scénarios répartis entre
projet moyen, recherche difficile, mémoire longue, documents et sécurité.
Les références de validation sont figées avant les phases suivantes.

Les diagnostics persistants ajoutent scores lexicaux, références, plages,
nombre de scores nuls et temps d'index/classement. La trace est plafonnée à
256 décisions et indique le nombre omis. Aucun contenu source brut n'y est ajouté.
Les compteurs Ollama sont exacts lorsqu'ils sont présents ; le budget est estimé
si le tokenizer n'est pas disponible. Absence de statistiques = indisponible.

Les métriques de récupération sont **au niveau fichier**, calculées sur les
extraits effectivement sélectionnés : rappel, précision et nDCG, avec k,
nombre attendu, nombre retourné et nombre pertinent. Le rappel par passage exact
reste à mesurer ; une mention dans une réponse n'est pas une preuve de récupération.
La comparaison fournit réponses, erreurs, usages et métriques côte à côte ; elle
refuse budgets différents, identités différentes, résultats manquants/dupliqués
et runs en erreur. Une identité inconnue rend la comparaison qualitative à revoir.

```bash
python3 scripts/evaluate-context.py validate
python3 scripts/evaluate-context.py validate --suite evaluation/context-v2
python3 -m unittest discover -s scripts/tests -p '*_tests.py' -v
python3 scripts/evaluate-context.py run --suite evaluation/context-v2 --model NOM_MODELE --label lexical --retrieval lexical --context 8192 --output-tokens 1024 --repeats 3 --output artifacts/evaluation/lexical
```

Les deux scénarios historiques créent 142 échanges avant la question finale.
Le script supprime ses conversations à la fin et signale les suppressions échouées.
Un rappel textuel attendu absent produit une erreur, jamais une réussite implicite.
Les patrons présents demandent toujours une revue humaine de la négation et du sens.

Seuils critiques figés : zéro perte d'originaux, zéro accès à une autre conversation
ou source exclue, zéro citation inconnue présentée comme validée, aucune comparaison
acceptée malgré un protocole incompatible. Aucun seuil de gain qualitatif ou de
latence n'est fixé sans baseline Ollama. Pour le holdout : examiner chaque perte
de source et chaque régression de fait ; ne jamais modifier le jeu pour la masquer.

Grille humaine par réponse : faits corrects / attendus, faits manquants / attendus,
affirmations non démontrées / affirmations, contradictions, source et plage pour
chaque fait, motifs du jugement. Revoir également les réponses sans source attendue.

Limites : corpus synthétique restreint, pas de diversité linguistique exhaustive,
scores au niveau fichier, scénarios longs coûteux, pas de baseline GPU mesurée ici.
