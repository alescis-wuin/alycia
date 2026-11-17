# Alycia V4 — utilisation et validation locale

Branche : `feature/local-client-v4-context-quality-20261117`, issue de
`feature/local-client-v1-20261116` à `228fde1996b5b0d7d5a04637dd2d9318c8970095`.
Voir le [plan par phases](Local-Client-V4-Plan.md) et le [compte rendu](Local-Client-V4-Report.md).

## Mise à jour de l’installation existante

Fermer le client et terminer les générations. Conserver le même clone, `.env`
et les volumes Compose. PostgreSQL doit fonctionner pour la sauvegarde.
Commandes compatibles avec fish et Bash :

```sh
git status --short
python3 scripts/backup.py create
git fetch origin
git switch --track origin/feature/local-client-v4-context-quality-20261117
bash scripts/check.sh
```

Conserver les modifications locales signalées par Git avant de changer de branche ;
ne pas utiliser `reset --hard`. Si la branche locale existe déjà, utiliser
`git switch feature/local-client-v4-context-quality-20261117`, puis
`git pull --ff-only origin feature/local-client-v4-context-quality-20261117`.

Dans deux terminaux, depuis ce clone :

```sh
make demo-api
```

```sh
make demo-client
```

Le lancement applique les migrations Chat, désormais **huit** : V4 ajoute
`GenerationDiagnostics` et `StructuredMemoryLedger`. Elles ajoutent des colonnes
sans effacer l’historique. La sauvegarde complète conserve messages, mémoire et
imports ; l’export JSON d’une conversation conserve les diagnostics mais reconstruit
la mémoire dérivée et n’exporte pas les originaux joints.

Pour une première installation : [prérequis et lancement V1](Local-Client-V1-Guide.md).
Pour Windows : conserver le clone et les volumes, sauvegarder selon le
[guide V3](Local-Client-V3-Guide.md), effectuer les mêmes commandes Git, puis
`.\scripts\check.ps1` et `.\scripts\demo.ps1 -Action All -Model "NOM_DU_MODELE"`.
Les ajouts V4 n’ont pas été exécutés sur Windows dans cette livraison.

## Réglages conseillés pour commencer les tests

1. Vérifier `ollama list`. Choisir un modèle **génératif** dans les réglages de
   la conversation. Le sélecteur latéral concerne les nouvelles conversations.
2. Conserver la recherche `lexical` : BM25 fonctionne sans modèle supplémentaire.
3. Activer le résumé automatique et la mémoire structurée. Le système, les notes,
   les quatre derniers messages et la demande restent protégés. S’ils dépassent
   seuls le budget, une erreur explicite invite à augmenter la fenêtre.
4. Activer les outils si le modèle annonce la capacité `tools`. Commencer avec
   une fenêtre de 8 192 tokens ; l’augmenter si les éléments protégés ne tiennent
   pas, sans dépasser les capacités réelles du modèle et de la machine.
5. Conserver le comptage automatique. Le compteur BPE est utilisé uniquement
   lorsque les métadonnées installées annoncent le tokenizer Qwen2 compatible.
   Les autres cas utilisent une estimation UTF-8 prudente, indiquée dans l’UI.

Le comptage du texte ne reproduit pas exactement tous les gabarits de conversation.
Une marge de format et une réserve de sécurité restent appliquées. Certains textes
Unicode ou fragments atypiques utilisent également le repli prudent. Les tokens
réellement évalués, quand Ollama les fournit, apparaissent dans les diagnostics.

Les réglages `legacy` et la désactivation de **Mémoire structurée** conservent
respectivement la sélection V3 et son résumé récursif. Ils permettent des essais
comparatifs sans changer de base de données.

## Importer, retrouver et vérifier le code

1. Importer avec **+ Dossier** ou **+ Fichiers / ZIP**.
2. Vérifier les modes **Projet**, **Épinglé**, **Fenêtre**, **Exclu** dans **Documents**.
3. Poser une question ciblée, par exemple :

> Lis ChatService et ConversationContext, puis leurs tests. Explique comment un
> échange échoué est empêché d’entrer dans l’historique. Cite les passages [S…]
> utilisés et distingue ce qui est démontré de ce qui reste à tester.

4. Consulter **Mémoire et contexte** avant l’envoi : budget, nombre de fichiers
   indexés, omissions et extraits réellement sélectionnés.
5. Après la réponse, développer **Sources citées et coût de la réponse** ainsi que
   **Actions et sources consultées**. Les références indiquent fichier, lignes,
   plage de caractères UTF-16 et SHA-256 de l’import. Télécharger l’original depuis
   **Documents** pour vérifier les passages.

L’index lit les originaux, y compris au-delà de l’extrait historique de 200 000 octets.
Les déclarations C# sont découpées avec Roslyn ; leurs références sont syntaxiques,
non un graphe de dépendances résolu par compilation. Les autres langages utilisent
principalement des blocs de lignes. Les en-têtes de types sont identifiés comme
partiels. Les caches sont réutilisés selon version, chemin, empreinte et état d’extraction.

`project_read` accepte `fileId` et, facultativement, `symbol` : nom complet ou
fragment. Un nom ambigu renvoie des candidats. `offset` reste **absolu**, même en
lecture par symbole ; suivre `nextOffset` jusqu’à `null` termine ce périmètre,
pas nécessairement le fichier entier. Ces paramètres sont aussi exposés par MCP.
Les outils restent en lecture seule sur les imports.

Les petits résultats sont conservés tels quels. Sous pression du budget, seuls
les anciens cycles complets d’outils sont résumés ; le dernier cycle reste brut.
Un appel identique déjà réussi reçoit un avertissement sans nouvelle lecture ;
trois répétitions consécutives arrêtent l’opération sans enregistrer un échange partiel.

## Mémoire structurée et corrections

L’historique brut est utilisé tant qu’il tient dans le budget. Ensuite, l’extraction
classe des **citations exactes** en faits déclarés, décisions, contraintes et
questions ouvertes. Chaque citation conserve numéro de message, rôle et empreinte.
Les réponses IA restent des propositions, non des faits validés.

La sélection combine correspondance avec la demande, priorité aux décisions et
contraintes, puis récence. Les variantes d’une même clé restent groupées. Pour
corriger une clé affichée dans **Citations et provenance**, envoyer une ligne :

```text
Correction [base]: utiliser PostgreSQL pour la persistance.
```

La clé doit être celle affichée dans la mémoire. Une correction explicite de
l’utilisateur écarte les variantes antérieures de cette clé ; une correction émise
par l’IA ne le fait pas. Un changement de clé ou une contradiction implicite peut
échapper à ce mécanisme : examiner les citations reste nécessaire.

Un JSON ou une citation invalide produit un extrait original de repli identifié.
Le registre conserve au plus 256 entrées ; les omissions et replis sont comptés.
Les citations non retenues restent récupérables dans les messages originaux.
**Réinitialiser la mémoire** efface les deux mémoires dérivées, sans toucher à
l’historique. **Préparer le résumé** permet de préparer explicitement le contexte.

## Recherche sémantique optionnelle

Dans **Réglages**, choisir `hybrid` et indiquer le nom exact d’un modèle
**d’embeddings local déjà installé**. Le modèle conversationnel reste distinct.
La recherche combine BM25 et similarité vectorielle par fusion de rangs, puis applique
un petit bonus structurel et une déduplication avant sélection sous budget.
Ce reclassement est heuristique ; aucun reranker neuronal supplémentaire n’est lancé.

Le premier passage calcule les vecteurs des sources. Les suivants réutilisent
ceux des passages inchangés ; une nouvelle empreinte de modèle invalide le cache.
L’aperçu peut calculer des embeddings, même s’il ne génère pas de réponse.
Les coûts d’embeddings sont identifiés séparément dans les mesures.

Limites : 8 millions de caractères indexés par conversation ; mode hybride limité
à 2 048 passages. Les omissions sont visibles. Une fenêtre insuffisante du modèle
d’embeddings provoque un refus explicite, sans troncature silencieuse. Les limites
d’import V3 restent applicables. Sur une machine à VRAM limitée, deux modèles
peuvent provoquer des chargements supplémentaires : mesurer avant d’adopter ce mode.

## Contrôle des réponses

Trois profils courts sont ajoutés : **Réponse sourcée**, **Diagnostic ciblé** et
**Plan d’implémentation**. Le contrôle final recherche les identifiants `[S…]`
standardisés dans les passages réellement fournis, y compris les lectures d’outils.
Il signale références inconnues, sources non citées et absence de sources.

Ce contrôle ne démontre **ni la vérité de l’affirmation, ni que la citation la
justifie, ni l’analyse exhaustive du projet**. Il ne relance pas automatiquement
une génération. Les diagnostics conservent le coût cumulé de tous les appels du
traitement ; une métrique manquante reste `?`, sans être remplacée par zéro.
Les coûts d’un aperçu distinct et d’une analyse de dossier distincte restent attachés
à leurs opérations respectives.

## Tests sur la machine

```sh
bash scripts/check.sh
python3 -m unittest discover -s scripts/tests -p '*_tests.py' -v
python3 scripts/evaluate-context.py validate
python3 scripts/verify-local.py --model "NOM_DU_MODELE" --extended --tools
```

La dernière commande nécessite l’API démarrée, Ollama et un modèle capable d’utiliser
les outils. Elle crée puis supprime ses propres conversations de test. Elle peut
nécessiter de nombreuses générations. Sans `--tools`, elle teste aussi les modèles
qui ne proposent pas cette capacité.

Pour la recette PostgreSQL/Ollama simulé, utiliser une connexion de **test** disposant
de `CREATEDB` ; des bases isolées sont créées puis supprimées. La solution doit être
compilée et l’outil EF restauré. Exemple fish/Bash via `env` :

```sh
env ALYCIA_CHAT_TEST_CONNECTION="Host=127.0.0.1;Port=5432;Database=postgres;Username=ROLE_TEST;Password=MOT_DE_PASSE_TEST" bash scripts/check.sh --integration
env ALYCIA_CHAT_TEST_CONNECTION="Host=127.0.0.1;Port=5432;Database=postgres;Username=ROLE_TEST;Password=MOT_DE_PASSE_TEST" python3 scripts/tests/http-integration.py
```

Ne pas confondre la recette simulée avec une mesure de qualité du modèle.

## Comparaison à budget constant

Le corpus figé contient huit sources V3 et 36 questions : 24 de développement,
12 de validation. Aucun score de justesse n’est déduit automatiquement des mots trouvés.
Commencer par les questions de développement ; garder la validation pour les choix stabilisés.

Comparer d’abord deux configurations V4 dans la même installation :

```sh
python3 scripts/evaluate-context.py run --model "NOM_DU_MODELE" --label v4-legacy --retrieval legacy --counter conservative --memory recursive --context 8192 --output-tokens 1024 --tools --calls 12 --repeats 3 --output artifacts/evaluation/v4-legacy
python3 scripts/evaluate-context.py run --model "NOM_DU_MODELE" --label v4-lexical --retrieval lexical --counter conservative --memory structured --context 8192 --output-tokens 1024 --tools --calls 12 --repeats 3 --output artifacts/evaluation/v4-lexical
python3 scripts/evaluate-context.py compare artifacts/evaluation/v4-legacy/run.json artifacts/evaluation/v4-lexical/run.json
```

Ce contrôle isole surtout la sélection de sources : les deux configurations utilisent
le moteur V4. Pour comparer **V3 contre V4**, utiliser une installation V3 séparée
avec ses propres volumes/ports, piloter son URL avec le script V4 via `--url`, et
omettre les options V4 non prises en charge. Ne pas rétrograder la base migrée pour
faire ce test. Une identité de modèle inconnue, notamment avec V3, exige une vérification
manuelle du digest et de la quantification.

Le protocole fixe température 0, graine, fenêtre, réserve, sortie et plafonds d’appels.
Il enregistre options appliquées, réponses, extraits, erreurs, traces, mesures des
aperçus et des générations. Additionner ces deux coûts lorsqu’un aperçu calcule des
embeddings. Une même fenêtre ne garantit ni le même nombre de tokens effectivement
utilisés ni le même coût cumulé : comparer également ces mesures et la latence.
Une graine identique ne garantit pas un résultat bit à bit identique sur tous les matériels.

Le corpus est composé de questions indépendantes : il ne mesure pas à lui seul
la mémoire sur une longue conversation. Compléter par une conversation identique
avec plusieurs décisions, une correction explicite, deux échanges récents puis une
question sur une ancienne contrainte. Vérifier les citations, omissions, erreurs et
coûts à fenêtre identique, sans modifier la référence après lecture des résultats.
