# V4 — interruption après des actions, correctif du 17 novembre 2026

Branche : `feature/local-client-v4-context-quality-20261117`.
Base : `58418f9230b526de49b0d4e8e69bf87949fb44f4`.

## Diagnostic

Le message « Ollama a renvoyé une réponse vide ou incomplète » provenait de
la génération non diffusée en continu. Dans la boucle d’outils, ce chemin sert
à résumer les anciens résultats lorsque le contexte se remplit. Le plafond
était de **128 tokens pour une cible de 512 octets**. Une réponse terminée avec
du raisonnement mais sans contenu final faisait échouer toute l’opération.

Ce scénario est reproduit avec le fournisseur simulé. La capture ne permet pas
de connaître la réponse brute ni la raison d’arrêt du modèle utilisé sur la
machine : l’épuisement de la sortie par le raisonnement reste une hypothèse pour
l’incident réel. Le premier refus de lecture n’explique pas, à lui seul, l’arrêt,
puisque les actions suivantes ont réussi.

## Phases réalisées

| Phase | Étapes et résultat |
|---|---|
| 1 — génération auxiliaire | Demander `think:false` pour résumés et extraction de mémoire ; réserver davantage de sortie tout en respectant la fenêtre ; distinguer contenu vide et réponse incomplète ; conserver les métriques d’un appel vide. |
| 2 — poursuite de l’exploration | Sur contenu vide terminé, conserver au plus 512 octets d’extraits identifiés comme partiels ; éviter les nouvelles tentatives de résumé pendant cette demande ; autoriser la relecture après compactage ; conserver les limites d’appels et de contexte. |
| 3 — interface et validation | Afficher et persister l’avertissement, préciser les refus d’outils, restaurer le brouillon après échec ; ajouter la recette sous pression du contexte et les tests de non-régression. |

Le raisonnement des réponses conversationnelles et des appels d’outils conserve
son comportement antérieur. Une demande `think:false` ne garantit pas que tous
les modèles supprimeront leur raisonnement ; le repli couvre aussi une réponse
vide malgré cette option. Le texte de raisonnement ne sert pas de résumé de secours.

Le repli contient des extraits verbatim, pas un résumé sémantique validé. Il peut
omettre des informations anciennes. L’avertissement reste dans les diagnostics
de la réponse enregistrée et dans son export. Les originaux restent accessibles.
Les erreurs réseau, réponses non terminées, budgets impossibles et annulations
ne déclenchent pas cette reprise. Les résumés persistants de conversation et de
dossiers conservent leur validation stricte.

## Validation exécutée

Environnement : Linux x64, SDK .NET 10.0.404, PostgreSQL 16.2 isolé, fournisseur
Ollama simulé. Aucune exécution sur un modèle local réel, GPU, KDE ou Windows.

- Compilation de la solution, puis recompilation du client après son ajustement :
  aucun avertissement de compilation ni erreur.
- **39 tests ciblés** : protocole Ollama, budget, mémoire récursive, appels d’outils
  et reprise ; réponses vides terminées par `stop` et `length`, panne réseau,
  fin absente, annulation, Unicode et relecture après compactage.
- **44 tests du client** : rendu, diagnostics, motif de refus, brouillon restauré
  et conservation de l’identifiant lors d’une nouvelle tentative.
- **5 tests mémoire/documents**, dont analyse récursive d’archives sur PostgreSQL.
- Recette HTTP ciblée réussie : **six tours d’inférence avec outils, un résumé vide**,
  réponse finale sauvegardée, quatre anciens messages inchangés, relecture réussie,
  avertissement exporté, coût de l’appel vide comptabilisé, rejeu sans nouvel appel.
- Format et lint réussis ; contrôle EF sans changement de modèle non migré.

Soit **88 tests .NET distincts réussis, aucun ignoré**, plus la recette HTTP ciblée.
La première exécution HTTP a aussi parcouru les contrôles existants, puis échoué
sur une hypothèse incorrecte du nouveau simulateur : il attendait une question
sans les extraits joints par le contexte. Le simulateur vérifie désormais la
question complète suivie de ces extraits et leur stabilité. Seule cette recette
a été relancée après correction. Les résultats historiques de la suite V4 entière
ne sont pas présentés comme une nouvelle exécution complète.

## Mise à jour et essai

Fermer le client et arrêter l’ancienne API. Depuis le clone existant, conserver
les modifications locales signalées par `git status` et exécuter :

```sh
git status --short
git switch feature/local-client-v4-context-quality-20261117
git pull --ff-only origin feature/local-client-v4-context-quality-20261117
make demo-api
```

Dans un second terminal, depuis le même clone :

```sh
make demo-client
```

Le lancement de l’API reconstruit son image ; mettre seulement Git à jour ne
remplace pas une API déjà démarrée. Aucune nouvelle migration ni dépendance
n’est ajoutée par ce correctif. Ne pas réimporter ni supprimer les conversations.

Renvoyer la demande avec le même modèle et les mêmes fichiers. Vérifier les
**Actions et sources consultées**, puis les diagnostics de la réponse. Si le
repli se déclenche, le libellé **Contexte partiellement conservé** doit apparaître.
Une réponse finale reste indispensable pour confirmer l’enregistrement. En cas
de nouvelle erreur, relever son texte exact, le modèle et `ollama --version`.

La recette déterministe peut être lancée séparément après compilation, avec une
connexion PostgreSQL de test disposant de `CREATEDB` ; elle crée puis supprime
sa propre base :

```sh
env ALYCIA_CHAT_TEST_CONNECTION="Host=127.0.0.1;Port=5432;Database=postgres;Username=ROLE_TEST;Password=MOT_DE_PASSE_TEST" python3 scripts/tests/http-integration.py --case tool-summary
```

Elle simule la panne et ne mesure pas la qualité des réponses du modèle local.
Le [guide V4](Local-Client-V4-Guide.md) détaille les prérequis et les autres tests.
