# Alycia — compte rendu du client local V1

Audit et implémentation du 16 novembre 2026.

## Résultat et branche retenue

La base la plus avancée était **`prototype/conversation-files`**, commit
`e1c5bc08e7873aa6f43689a91dc09a37da9db75b` : elle contenait les quatre autres
branches observées, avec 27 commits supplémentaires par rapport à `main`,
5 par rapport à `develop` et 2 par rapport à `prototype/local-ollama-chat`.
Cette conclusion repose sur l'ascendance Git et les fonctionnalités du code,
pas seulement sur la date du dernier commit. Le détail est dans le
[plan par phases, lots et étapes](Local-Client-V1-Plan.md).

Les travaux sont sur **`feature/local-client-v1-20261116`**, sans fusion ni
modification de `main`, `testing`, `develop` ou des deux prototypes.
Le socle .NET/Avalonia/PostgreSQL est conservé. La V1 permet de travailler avec
un modèle Ollama local, reprendre ses conversations, contrôler le contexte et
les prompts, fournir des sources et récupérer les réponses pour développer.
Le [guide d'utilisation et de recette](Local-Client-V1-Guide.md) donne les commandes.

## Lots réalisés successivement

| Lot | Résultat livré | Commit de la branche GitHub |
|---|---|---|
| P0 | Comparaison des branches, base figée, audit et plan | `098f859` |
| P1 | Réglages par conversation, notes, profils de prompts, documents épinglés/exclus, aperçu du contexte, migration additive | `a5f1303` |
| P2 | Streaming Ollama/API, annulation, limites, confirmation après persistance, statistiques | `cb115a0` |
| P3 | Recherche paginée, exports JSON/Markdown, import contrôlé, duplication avec documents | `d33ec2f` |
| P4 | Parcours Avalonia, réglages accessibles, bibliothèque de prompts, régénération dans une copie, copie du code, brouillons persistants, filtrage d'import | `39496cd` |
| P5 | Protection de l'hôte HTTP et des flux locaux, contrôles reproductibles, recettes HTTP, sauvegarde/restauration isolée, documentation | Commit final de cette livraison |

## Corrections et améliorations principales

| Constat initial | Comportement livré | Point d'entrée dans le code |
|---|---|---|
| Attente silencieuse pendant toute la génération | Réponse progressive, état de génération et annulation ; le flux final confirme l'enregistrement | `OllamaChatModel.Streaming.cs`, `ChatEndpoints.Streaming.cs` |
| Réponse réseau ambiguë et risque de renvoi manuel | Identifiant de requête conservé avec le brouillon ; rejeu d'un échange déjà enregistré sans nouvelle génération | `ChatService.cs`, `DraftStore.cs` |
| Instruction et paramètres peu accessibles | Configuration persistante par conversation et profils réutilisables | `ConversationConfiguration.cs`, `ChatEndpoints.Configuration.cs` |
| Contexte glissant difficile à comprendre | Aperçu des messages effectivement préparés et du budget ; paires complètes, historique stocké intact | `SlidingContext.cs`, `FileContext.cs` |
| Documents disparaissant avec les anciens échanges | Modes fenêtre, épinglé ou exclu ; priorité documentaire explicite | `FileContext.cs` |
| Chargement inutile de textes documentaires | Projection SQL des métadonnées ; sélection préalable des fichiers candidats au contexte | `EfChatRepository.cs`, `ChatEndpoints.Configuration.cs` |
| Historique difficile à retrouver et transporter | Recherche titre/messages, pagination, archive structurée versionnée et duplication | `ChatEndpoints.History.cs`, `ConversationArchive.cs` |
| Brouillon perdu au changement de conversation ou redémarrage | Brouillons séparés, écriture atomique et permissions Unix privées ; fichier illisible préservé | `DraftStore.cs`, `ChatWindow.axaml.cs` |
| Import de dossiers générés ou de secrets usuels | Exclusions de chemins dans le client et exclusion du contexte pour les entrées sensibles d'archives | `ImportPolicy.cs`, `FileImport.cs` |
| Contenu résiduel possible dans un fichier téléchargé écrasé | Troncature de la destination avant écriture | `ChatWindow.Files.cs` |
| Options Ollama ambiguës | Graine absente omise du JSON ; température et paramètres validés | `OllamaChatModel.cs`, `ChatContracts.cs` |
| Pair local insuffisant face au rebinding DNS | Vérification de l'en-tête Host, rejet d'Origin et maintien des contrôles du pair/jeton | `LocalChatAccess.cs` |
| Proxy système susceptible d'intercepter un flux local | Proxy HTTP désactivé pour client→API et API→Ollama ; redirections toujours désactivées | `ChatApiClient.cs`, `ChatEndpoints.cs` |
| Tests de persistance implicitement sautés | Script de contrôle avec mode intégration exigeant une connexion de test ; recette HTTP isolée | `scripts/check.sh`, `scripts/tests/http-integration.py` |
| Absence de procédure de récupération complète | Dump PostgreSQL + fichiers + SHA-256 ; restauration refusée sur un projet existant | `scripts/backup.py` |

La migration `20261116194633_LocalClientConfiguration` ajoute les réglages et notes
aux conversations, le mode documentaire et la table des profils. Les conversations
existantes conservent les valeurs du serveur jusqu'à leur configuration explicite.
Les messages ne sont ni supprimés ni résumés automatiquement.

Les échanges restent enregistrés en une opération atomique. Une génération
incomplète, annulée ou en conflit de révision ne crée pas un message utilisateur
orphelin. Un verrou en mémoire empêche deux générations simultanées dans la même
conversation sur une instance ; la révision en base protège la persistance entre
instances. Une coupure au moment exact du commit reste un état réseau ambigu :
le rejeu de la même requête permet de retrouver le résultat enregistré.

## Vérifications réellement exécutées

Environnement de validation : Linux x64, SDK .NET **10.0.404**, runtime
**10.0.15**, PostgreSQL **16.2** temporaire. Le déploiement Compose cible
PostgreSQL **17** ; il ne faut pas confondre les deux versions.

| Contrôle | Résultat observé |
|---|---|
| Compilation de `Alycia.slnx` | Réussite, 0 avertissement, 0 erreur |
| Tests client Avalonia, plateforme headless | 35 réussis, 0 sauté |
| Tests du module Chat, PostgreSQL activé | 50 réussis, 0 sauté |
| Tests Workspaces | 11 réussis, 0 sauté |
| Total .NET | **96 réussis, 0 échec, 0 sauté** |
| EF `has-pending-model-changes` | Aucun changement de modèle sans migration |
| Migrations sur base PostgreSQL vierge | Appliquées ; persistance et concurrence vérifiées |
| Recette API HTTP + PostgreSQL réel + Ollama simulé | 5 groupes réussis, données temporaires nettoyées |
| Tests Python de sauvegarde | 4 réussis : intégrité, chemins/liens d'archive, refus d'écrasement, redémarrage après échec |
| Formatage/lint | Conformes ; formatage C# des 40 fichiers concernés et contrôle des fichiers modifiés |
| Scripts Bash et Python | Syntaxe Bash et compilation Python vérifiées |
| Rendu Avalonia | Capture Skia/headless inspectée, interface principale sans chevauchement constaté |

Les cinq groupes HTTP couvrent l'accès local et les modèles ; les réglages et
prompts ; le streaming, le rejeu et les conflits ; les fichiers et leur contexte ;
l'archive, la recherche et la duplication. Des tests ciblés avaient également été
exécutés à la fin des lots, avant cette suite finale.

Les compteurs structurés sont conservés dans
[`validation/Local-Client-V1-Checks.json`](validation/Local-Client-V1-Checks.json).
Les scripts produisent de nouvelles preuves sur le poste de l'utilisateur.
Le runner de tests a émis un diagnostic non bloquant concernant
`VSTEST_ARTIFACTS_DIRECTORY` / l'ancien `Microsoft.NET.Test.Sdk` : les rapports
TRX ont néanmoins été écrits et tous les tests se sont exécutés. Sa mise à jour
relève d'un lot de maintenance de dépendances séparé.

**Non exécuté dans cet environnement :** génération avec un vrai Ollama, mesure
sur GPU NVIDIA, bureau KDE/Wayland, parcours Windows/PowerShell, construction
Docker et restauration Docker de bout en bout. Le script de sauvegarde a été
testé avec commandes Docker simulées ; cela ne remplace pas une restauration
réelle. La capture headless ne valide pas le presse-papiers natif ou le rendu Wayland.

## Limites connues de cette V1

- Client local mono-utilisateur, routes de conversation en Development opt-in.
  La PR d'authentification propriétaire n'a pas été fusionnée. Aucune exposition
  réseau publique ni isolation entre utilisateurs n'est validée.
- Estimation du contexte par octets UTF-8 avec marge : prudente mais non exacte.
  Les capacités du modèle ne sont pas interrogées automatiquement ; une valeur
  autorisée par le formulaire peut dépasser les capacités du modèle ou du poste.
- Les pièces jointes ont un budget limité. Même épinglées, elles peuvent être
  tronquées ou omises. Ni index de projet, ni embeddings, ni RAG ne sont livrés.
- Les exports JSON/Markdown excluent les pièces jointes. Ils sont bornés à
  5 Mio/2 000 messages ; la duplication utilise la même borne de messages.
  La sauvegarde complète couvre les conversations plus importantes.
- Les données, profils, brouillons et sauvegardes restent en clair. Le filtrage
  de fichiers ne détecte pas tous les secrets. Les archives originales peuvent
  en contenir. Les parseurs documentaires ne sont pas isolés dans un bac à sable.
- Le texte est affiché sans rendu Markdown riche ni coloration syntaxique.
  La copie des blocs de code est disponible. Pas de pièces jointes image/audio
  pour un modèle multimodal, de voix ou d'outils exécutables.
- Les statistiques de génération sont remontées au client, mais leur historique
  n'est pas stocké par échange. Le raisonnement séparé sert d'indicateur d'activité,
  il n'est pas conservé ni réinjecté dans le contexte.
- Une seule instance du client est prévue par dossier de brouillons. Les profils
  de prompts n'ont pas d'historique de versions ni de verrou optimiste.
- Une erreur de validation ferme actuellement le dialogue de réglages ; les
  changements non enregistrés doivent être ressaisis. Les très longues discussions
  méritent un travail supplémentaire de virtualisation et d'accessibilité.
- Un échec de base au moment d'un import/fork peut laisser des fichiers orphelins
  plutôt que risquer de supprimer des données dont le commit serait incertain.
  Une réconciliation contrôlée du stockage reste à développer.
- Une restauration crée un environnement isolé et affiche les comptes. Elle ne
  bascule pas le client vers cet environnement et n'est pas encore validée par
  un exercice Docker réel avec contrôle de tous les fichiers restaurés.

## Améliorations suivantes, ordonnées

Ces axes concernent le client avancé et ne sont pas comptés comme lots V1 livrés.
Ils suivent la recette matérielle de cette version ; chacun doit conserver les
tests actuels et apporter une preuve de son bénéfice.

| Priorité | Travail concret, par étapes | Critère de réussite |
|---|---|---|
| 1 — recette du poste | Exécuter la recette réelle Ollama ; tester annulation/reprise ; contrôler VRAM et latences ; sauvegarder/restaurer dans un projet neuf | Historique et fichiers conservés, aucun doublon, mesures documentées sur le matériel |
| 2 — traçabilité | Enregistrer modèle/digest, options, prompt effectif, sources/hachages, statistiques et version du client par génération ; prévoir purge/export | Une réponse peut être expliquée avec les paramètres et documents réellement utilisés |
| 3 — contexte | Lire les capacités `/api/show` ; comparer estimateur/tokenizer ; développer un résumé explicite et révisable | Corpus fixe montrant le rappel utile et les économies sans perdre les contraintes du projet |
| 4 — sources de projet | Sélection de workspace ; index incrémental ; exclusions/limites ; découpage syntaxique ; embeddings locaux et citations | Sources citées retrouvables et à jour, test de rappel et détection de régressions |
| 5 — assistance au code | Lecture contrôlée ; propositions de diff ; approbation ; worktree ; exécution de tests isolée avec permissions et quotas | Aucun changement ou lancement hors périmètre ; échec récupérable et journalisé |
| 6 — ergonomie | Arbre des variantes ; édition d'un ancien message ; Markdown/code ; clavier, contraste et virtualisation | Utilisation fluide avec longue conversation et validation native Linux/Windows |
| 7 — fiabilité des données | Rétention, chiffrement optionnel, reprise d'import, réconciliation des orphelins, restauration automatisée | Exercice de récupération avec comparaison des messages et SHA-256 des fichiers |
| 8 — performances | Mesurer SQL, allocations, contexte et préfill/décode ; index de recherche si nécessaire ; cache des capacités ; file de générations | Gains mesurés à qualité constante, mémoire bornée, annulation toujours réactive |
| 9 — distribution et maintenance | Verrouillage des dépendances/images, audit de vulnérabilités, paquet signé, mise à jour et rollback | Installation et retour arrière reproductibles, migration testée depuis la version précédente |
| 10 — extensions | Authentification/autorisation avant LAN ; autres backends ; multimodalité ; politiques explicites de données | Aucun transfert cloud implicite, tests de capacités et permissions par backend |

La boucle de développement initiale est déjà utilisable : définir la tâche et
ses contraintes dans Alycia, lui fournir les sources pertinentes, contrôler le
contexte, récupérer la proposition, la tester dans Git, puis réinjecter les
résultats. L'exécution autonome d'outils appartient à l'axe 5 et demande un cadre
de permissions et de récupération propre.
