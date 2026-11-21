# Alycia V2 — compte rendu du 16 novembre 2026

Livraison sur la branche existante `feature/local-client-v1-20261116`, à partir
de la V1 `ec56cc29af111def6de76b2df2da9f30b45292ba` déclarée fonctionnelle par
l’utilisateur. Aucun changement ni fusion dans `main`, `develop` ou les autres
branches. Le [guide V2](Local-Client-V2-Guide.md) explique la mise à jour et la recette.

## Lots réalisés successivement

| Lot | Étapes livrées | Commit GitHub |
|---|---|---|
| Q0 — cadrage | Base figée, plan, règles de fin de flux, budgets, métadonnées et limites | [`3ced747`](https://github.com/alescis-wuin/alycia/commit/3ced747df07a950503c7152fa02b0b480e0757b3) |
| Q1 — génération | Mode jusqu’au signal d’arrêt, réserve distincte du plafond, délai d’inactivité, refus des fins incomplètes | [`10cb145`](https://github.com/alescis-wuin/alycia/commit/10cb145b6c21cfe8b52af258b5acf0fba17d48a9) |
| Q2 — mémoire | Réduction récursive, protection des deux derniers échanges, cache persistant, préparation/inspection/réinitialisation | [`7be6d01`](https://github.com/alescis-wuin/alycia/commit/7be6d015e25026c87b4782bc629a3d77deeae68f) |
| Q3 — documents | Archives imbriquées, dates de source, SHA-256, arbre, résumés par fichier puis réduction, provenance et péremption | [`e173272`](https://github.com/alescis-wuin/alycia/commit/e17327233926ac8e075745ed0947983256d0e208) |
| Q4 — interface | Navigation simplifiée, Markdown natif progressif, copie par bloc, défilement, accès aux mémoires/analyses | [`904fdae`](https://github.com/alescis-wuin/alycia/commit/904fdaeb8ba1226e7c43a8d6ec7d67841551678e) |
| Q5 — recette et livraison | Recette HTTP étendue, inventaire complet ou filtré au choix, contrôles, guide et résultats | Commit portant ce compte rendu |

Les étapes et critères sont détaillés dans le [plan V2](Local-Client-V2-Plan.md).
Le nom historique de la branche est conservé pour permettre une mise à jour directe.

## Changements fonctionnels

**Conversation.** Le système, les notes persistantes, les quatre derniers messages
terminés et la demande courante sont protégés. Les échanges antérieurs sont
résumés à mesure qu’ils dépassent le budget. Les résultats intermédiaires sont
réduits récursivement si nécessaire ; le nombre d’appels, les niveaux et la taille
des entrées/sorties sont bornés. Aucun historique original n’est remplacé.

Le cache conserve texte, couverture, empreinte chaînée et modèle. L’aperçu n’appelle
pas le modèle ; la préparation explicite peut enregistrer une mémoire avant l’envoi.
L’envoi ordinaire enregistre nouvelle mémoire et nouvel échange ensemble après
réussite. Un dépassement des seuls éléments protégés provoque un refus explicite.

**Génération.** `num_predict=-1` est utilisé par défaut pour la réponse. Le modèle
doit signaler `done_reason=stop`. La fin naturelle peut correspondre à EOS ou à une
séquence d’arrêt configurée par le modèle : aucune chaîne EOS n’est recherchée
dans le texte. Une fin par longueur, une connexion interrompue ou un délai expiré
n’est pas transformé en réussite. Les résumés conservent un plafond de génération.

**Documents.** Un lot peut compter jusqu’à 500 entrées et contenir quatre niveaux
d’archives. Les chemins dangereux, liens, doublons internes et dépassements de
limites sont refusés. Le parcours de dossier propose l’inventaire complet ou les
sources utiles filtrées. L’inventaire contient les métadonnées des fichiers
importés ; les contenus exclus ne sont pas fournis au modèle.

L’analyse lit l’arbre complet par fragments, résume les textes extractibles, puis
réduit les résultats. Elle produit une synthèse, `tree.txt` et un manifeste JSON.
L’empreinte du périmètre détecte les changements de sources/exclusions ; une
analyse périmée n’est plus injectée dans le contexte. Les analyses sont conservées
séparément des sources et peuvent être supprimées sans supprimer celles-ci.

**Interface.** Actions courantes visibles, actions secondaires regroupées,
historique repliable, réponse dans le fil et suivi du défilement contrôlé. Le
Markdown est analysé hors du fil graphique ; les blocs stables sont réutilisés
et le rafraîchissement du flux est regroupé. Les contenus chargés depuis
l’historique apparaissent sans attendre le minuteur du flux. Les réglages invalides
restent dans le formulaire avec leur erreur ; point et virgule décimale sont acceptés.

## Architecture et migrations

- Une dépendance de rendu : **Markdig 1.6.0**, avec contrôles Avalonia natifs,
  sans navigateur embarqué, HTML exécutable ou chargement d’images distantes.
- `RecursiveSummarizer` est partagé par mémoire et analyses ; les règles propres
  à chaque usage restent dans `RecursiveContext` et `DocumentAnalysisService`.
- Migration `20261116205445_RecursiveConversationMemory` : mémoire de conversation.
- Migration `20261116210142_DocumentAnalysesAndSourceMetadata` : dates/provenance
  des sources et analyses de documents. Les cinq migrations Chat ont été appliquées
  dans les bases PostgreSQL isolées de recette.
- Nouvelles routes de préparation/réinitialisation mémoire, inventaire et analyses ;
  les opérations longues transmettent leur progression en NDJSON et acceptent
  l’annulation. Révision et transaction protègent l’enregistrement des résultats.

## Vérifications effectivement exécutées

Environnement : Linux x64, SDK .NET 10.0.404, runtime 10.0.15, PostgreSQL de test
**16.2**. La configuration Compose cible PostgreSQL **17**, non lancé ici.

| Vérification | Résultat |
|---|---|
| Compilation de la solution et du client modifié | Réussie ; aucun avertissement de compilation restant |
| Client Avalonia | 43 tests réussis, dont rendu natif, formulaires, largeur réduite, liens et métadonnées multipart |
| Module Chat | 63 tests réussis, avec les tests PostgreSQL activés |
| Module Workspaces | 11 tests réussis |
| Total .NET sur les suites concernées | **117 réussis, 0 échec, 0 ignoré** |
| Recette HTTP V2 | **8 groupes réussis** ; vraie API et PostgreSQL, Ollama simulé |
| Sauvegarde — tests Python | **4 réussis**, Docker simulé |
| EF | Aucun changement de modèle sans migration |
| Qualité | Format/lint des fichiers modifiés et compilation Python réussis |
| Inspection graphique | Rendu Avalonia/Skia examiné à 1 240 × 880 et 960 × 700 |

La suite complète a d’abord réussi à 116 tests. Après l’ajout du choix d’inventaire
et du cas multipart avec dates, seuls les contrôles client concernés ont été
relancés : 43 tests au lieu de 42. Les modules Chat et Workspaces n’ont pas changé.
Les résultats structurés figurent dans
[`validation/Local-Client-V2-Checks.json`](validation/Local-Client-V2-Checks.json).

La recette HTTP confirme notamment : conservation brute du système/des notes/des
quatre messages/de la demande ; budget final ; cache sans nouvel appel ; historique
intact après préparation ; mémoire persistée après réponse ; réinitialisation non
destructive ; archives imbriquées ; SHA-256 et dates ; arbre complet du lot ;
contenu exclu absent des appels ; analyse périmée refusée ; suppression de tous les
descendants d’une archive. Ses **2 générations continues et 46 appels de résumé**
proviennent d’un modèle simulé, et ne sont pas une mesure de performance d’Ollama.

Les tests couvrent aussi annulation, refus de non-convergence, budget impossible,
Unicode, flux sans confirmation finale et renouvellement du délai d’inactivité.
Un diagnostic non bloquant du lanceur de tests concernant
`VSTEST_ARTIFACTS_DIRECTORY` reste présent ; il ne correspond ni à un test ignoré
ni à un avertissement de compilation.

## Limites et essais encore nécessaires sur le poste

1. **Vrai Ollama et fidélité des synthèses.** Aucun modèle ni GPU n’était disponible
   dans l’environnement de validation. Les résumés sont nécessairement avec perte
   et peuvent omettre ou déformer une décision ; comparer des cas réels aux originaux.
2. **Budget approximatif.** L’estimateur UTF-8 ne remplace pas le tokenizer du modèle.
   Une grande dernière réponse peut à elle seule rendre le bloc protégé trop grand.
3. **Arrêt non exclusivement naturel en toute circonstance.** Annulation, inactivité
   et garde-fous de ressources restent possibles. Le délai n’est plus un temps
   maximal total, mais le client ne peut garantir une génération infinie.
4. **Périmètre importé et extraction.** Les limites de taille/nombre/profondeur
   restent en vigueur. En mode sources utiles, les chemins écartés ne sont pas
   inventoriés. Les dossiers vides et liens ne sont pas importés. Un fichier
   binaire ou extrait partiellement n’est pas prétendu intégralement compris.
5. **Desktop et déploiement.** Vérifier les dialogues natifs, presse-papiers et flux
   prolongé sur KDE/Wayland, puis Windows si utilisé. Docker, PostgreSQL 17 et la
   restauration Docker réelle n’ont pas été exécutés ici.
6. **Exports.** La sauvegarde complète contient les données dérivées ; le JSON
   conversationnel et les copies ne transfèrent pas les analyses. Les reconstruire.

## Intégrations suivantes proposées, non implémentées dans cette livraison

| Priorité | Intégration | Critère concret |
|---|---|---|
| 1 | Recette de fidélité des résumés avec votre modèle | Jeu de décisions/contraintes connues, taux de conservation, coût et durée mesurés |
| 2 | Tokenizer adapté et diagnostic de la mémoire du modèle | Comparer estimation locale aux comptes Ollama ; proposer une fenêtre compatible |
| 3 | Index incrémental et recherche locale avec citations | Ne recalculer que les fichiers modifiés ; citer chemin, empreinte et extrait |
| 4 | Traçabilité par génération | Archiver version du modèle, paramètres, prompt effectivement envoyé et sources |
| 5 | Atelier de développement assisté | Proposer un diff dans un worktree, exécuter des tests bornés, demander validation avant application |

Le client constitue un socle de discussion et d’analyse locale. Il ne lance pas
encore de commandes générées et ne modifie pas automatiquement des projets.
