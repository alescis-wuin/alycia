# Alycia — V2 : interface, continuité et résumés récursifs

Base : `ec56cc29af111def6de76b2df2da9f30b45292ba`, validée fonctionnelle par l'utilisateur.
Branche conservée : `feature/local-client-v1-20261116`. Aucun changement de branche principale.

| Phase / lot | Étapes successives | Critère de sortie |
|---|---|---|
| Q0 — cadrage | Examiner la V1 ; préciser fin de flux, budget et métadonnées ; versionner ce plan | Contraintes explicites et base figée |
| Q1 — génération continue | Séparer réserve de contexte et limite de génération ; utiliser `num_predict=-1` ; renouveler le délai sur activité ; vérifier le motif de fin | Aucun arrêt normal imposé à 1 024 tokens, annulation et erreurs distinguées du signal d'arrêt |
| Q2 — mémoire récursive | Réducteur commun borné ; conserver système, notes, deux derniers échanges et demande ; résumer progressivement les échanges plus anciens ; cache persistant et inspection/réinitialisation | Budget respecté sans troncature des éléments protégés ; historique original conservé |
| Q3 — dossiers et archives | Métadonnées de source et SHA-256 ; ZIP/TAR imbriqués bornés ; `tree.txt` déterministe ; résumés des contenus puis réduction hiérarchique ; provenance et téléchargement | Inventaire complet du périmètre importé, dates manquantes explicites, synthèse traçable et annulable |
| Q4 — interface et Markdown | Simplifier les actions ; afficher la réponse dans le fil ; rendu natif Markdown progressif ; copie par bloc ; suivi du défilement ; accès aux résumés | Lecture continue, retour d'état clair, aucune exécution HTML ni chargement distant implicite |
| Q5 — livraison | Tests ciblés par lot ; migration/build/lint et suite ; recette HTTP ; inspection visuelle ; guide et compte rendu | Branche publiée, résultats exécutés et limites matérielles distingués |

## Règles de fonctionnement

- Ollama signale la fin par `done` et `done_reason`. Le token EOS n'est normalement
  pas exposé dans le texte ; aucune recherche d'une chaîne arbitraire dans la réponse.
  Une coupure, un délai d'inactivité, une annulation ou une limite de ressources
  reste une interruption explicite, jamais une réussite artificielle.
- La réserve de sortie sert au calcul du contexte, même en génération sans limite
  fixe de tokens. Elle ne garantit pas que le modèle peut produire indéfiniment.
- Système, notes, quatre derniers messages et demande courante sont protégés.
  S'ils dépassent seuls le budget, l'envoi échoue avec une explication plutôt que
  les tronquer. Le résumé est une mémoire dérivée, distincte de l'historique original.
- Les résumés utilisent le modèle local choisi. Ils sont imparfaits ; provenance,
  couverture et réinitialisation doivent être visibles. Les réductions ont un
  nombre d'appels borné et doivent effectivement diminuer le texte.
- L'inventaire porte sur le périmètre réellement importé. Les exclusions usuelles
  et limites sont signalées. SHA-256 est calculé sur les octets reçus ; une date
  indisponible reste inconnue. Les dates ZIP sont souvent sans fuseau et sans
  date de création ; aucune conversion prétendument certaine vers UTC.
- Un gros arbre est fourni au modèle par fragments, avec les sources associées,
  puis réduit. Le fichier `tree.txt` complet reste récupérable : il ne peut pas
  être ajouté sans limite à chaque prompt.
- Markdown : titres, emphase, listes, citations, liens, tableaux et code ; pas
  d'exécution de HTML/JavaScript ni de récupération automatique d'images distantes.

## Idées complémentaires après cette livraison

Journal des prompts et versions de modèle par génération ; tokenizer adapté au
modèle ; recherche locale avec citations ; index incrémental des sources et
détection des résumés périmés ; variantes de réponses en arbre ; prévisualisation
et approbation de diffs avant toute modification de code ; benchmarks reproductibles
de fidélité des résumés et de mémoire sur la RTX 4070 Laptop.

## Références

- [Ollama Chat](https://docs.ollama.com/api/chat)
- [Paramètres Ollama](https://docs.ollama.com/modelfile)
- [Markdig](https://github.com/xoofx/markdig)
