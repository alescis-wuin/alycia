# Alicia — Roadmap consolidée des Lots et tracks UIX

**Référence :** 13 août 2026
**Branche de continuité :** `feature/conversation-response`
**Checkpoint publié avant cette synchronisation :** `c51f1ef4be80951fd97abac05d9a13d9c213dd18`

## Principe

La roadmap possède deux axes complémentaires.

### Lots racines

Ils font évoluer les capacités métier et techniques :

```text
Conversation → génération → provider → robustesse → provenance → RAG → tools → multimodal → release
```

### Tracks UIX

Ils font évoluer l'expérience transverse :

```text
navigation → surfaces → interaction → visualisation → context/profile UX
```

UIX ne remplace pas les Lots racines. Une capacité UI qui dépend d'un contrat métier doit attendre ce contrat plutôt que le simuler uniquement en Presentation.

## État synthétique

| Lot / Track | État | Checkpoint principal |
|---|---:|---|
| Fondation | terminé | `9d225f8` |
| Lot 01 Conversation core | terminé | `bb99fd7` |
| Lot 02 Persistence | terminé | `ee6b9ea` |
| Lot 03 Lifecycle | terminé | `ca228e5` |
| Presentation 01–03 | terminé | `da06731` → `d4b094d` |
| Lot 04 Local composition | terminé | `a10fafa`, `08e2776` |
| Lot 05 Response port | terminé | `fcc9cf6` |
| Lot 06 Complete non-streaming turn | terminé | `cead4d9` |
| Lot 07 Streaming | terminé | `eb77261` |
| Lot 08 llama.cpp CUDA | terminé | `427e25c` |
| Lot 09 Provider/model configuration | terminé | `cf0a5c5` + stabilisations |
| UIX-01 Stages 1–7 | terminé | `ed4cc29` → `c51f1ef` |
| Lot 10 Robustesse/sécurité/observabilité | terminé | 10.1–10.7 terminés sur cette branche |
| Lot 10.1/10.2 sécurité + ownership | terminé | session locale durcie |
| UIX-01 Stage 8 | terminé | FOLLOWING / DETACHED + UI state |
| UIX-01 Stage 9 | terminé | décomposition Presentation en six ViewModels |
| UIX-01 Stage 10 | terminé | 10A–10D terminés; UIX-01 foundation clôturée |
| UI refinement 10.7A | terminé | Provider/Models visual hierarchy |
| Lot 10B Profiles/revisions/provenance/context | terminé | 10B.1–10B.6 terminés; contexte révisionné + provenance + budget explicite |
| UIX-02 Stage 1 active-branch profile selection | terminé | profils du modèle sauvegardé + binding explicite de la branche active |
| UIX-02 Stage 2 profile editor / WorkingDraft | terminé | création/édition custom + draft persistant explicite + révision immuable |
| UIX-02 Stage 3 WorkingDraft autosave / pre-send gate | terminé | autosave local + choix version précédente/modifiée avant nouvel envoi |
| Lot 11 RAG foundation | planifié | à faire |
| Lot 12 Retrieval/grounded generation | planifié | à faire |
| Lot 13 Tools/MCP/agents | planifié | à faire |
| Lot 14 Attachments/multimodal | planifié | à faire |
| Lot 15 Platform/release | planifié | à faire |

## Lot 10 — Robustesse provider / sécurité / observabilité

### 10.1 Serveur local sécurisé — P0 — terminé

- limiter CORS à localhost ;
- désactiver la Web UI llama.cpp pour le process géré ;
- utiliser une API key aléatoire éphémère ;
- authentifier le client local ;
- tester la non-fuite du secret.

### 10.2 Port et ownership — P0 — terminé

- supprimer l'hypothèse globale du port `8080` ;
- choisir un port loopback disponible par lancement ;
- rattacher endpoint et secret à l'instance runtime ;
- ne pas accepter le `/health` d'un autre process comme preuve d'ownership.

### 10.3 Timeouts et erreurs — P0 — terminé

- readiness bornée par une échéance explicite ;
- délais HTTP contrôlés pour health/ownership, installation, headers de chat et idle de stream ;
- probes d'exécutable bornées et process-exit/crash detection ;
- classification provider-neutral `Missing/Unsupported/Faulted/Network/Model` ;
- messages utilisateur sûrs séparés des diagnostics techniques ;
- récupération `Ready + last failure` pour ne pas confondre erreur modèle/réseau et réinstallation provider.

### 10.4 Retry — P0 — terminé

- aucun replay automatique de `POST /v1/chat/completions` ;
- Retry conversation uniquement sur action explicite de l'utilisateur et sans duplication du message utilisateur persisté ;
- retries automatiques bornés aux frontières HTTP GET idempotentes explicitement identifiées ;
- statuts transitoires 408/429/500/502/503/504 et erreurs réseau bornés à trois tentatives ;
- réponses permanentes non rejouées ;
- cancellation prioritaire sur toute tentative et tout backoff ;
- readiness health polling conservé comme boucle idempotente sous deadline 10.3.

### 10.5 Version / update llama.cpp — P0 — terminé

- release gérée installée distinguée de la version binaire détectée ;
- release validée/pinnée par Alicia : `b10435` au commit source `9e40df63ba151d771d8b247ac4011cf203337e99` ;
- installation et update résolvent le tag officiel puis téléchargent le tarball par SHA immuable ;
- Check update explicite compare release gérée, release validée et dernière release upstream ;
- une release upstream plus récente reste informative tant qu'elle n'est pas validée par Alicia ;
- update explicite uniquement d'une installation gérée plus ancienne vers la release validée ;
- aucun downgrade automatique d'une installation gérée plus récente ;
- activation atomique de `installation.json` après validation CUDA du candidat ;
- ancienne release conservée en cas d'échec/cancellation et jusqu'au nettoyage explicite du Lot 10.6.

### 10.6 Uninstall / cache — P0 — terminé

- inspection explicite de l'espace runtime et du cache modèles avant maintenance destructive ;
- nettoyage séparé des anciennes releases gérées conservées par 10.5, sans toucher à la release active ;
- désinstallation `runtime only` : `installation.json`, `releases/`, `.staging/` et logs, en conservant `models/` ;
- désinstallation `runtime + modèles/cache` comme action distincte ;
- confirmation explicite obligatoire avant chaque suppression, `Escape`/Cancel sans mutation ;
- aucune fermeture implicite du provider : maintenance destructive refusée tant que le serveur tourne ;
- configuration provider, conversations, UI state et legacy settings préservés ;
- suppression confinée au répertoire géré Alicia, sans suivre les symlinks/reparse points ni faire confiance à un chemin arbitraire issu des metadata.

### 10.7A Visual hierarchy / decluttering — UI refinement — terminé

- aucun changement de contrat, commande, état métier ou persistance ;
- Provider recentré sur Runtime comme tâche principale ;
- une seule action fortement accentuée par vue lorsque pertinent ;
- Updates rendues secondaires et lisibles sans dupliquer leur importance ;
- Maintenance et détails techniques placés en divulgation progressive ;
- Danger zone isolée visuellement sans modifier les confirmations 10.6 ;
- Models aligné sur la même grammaire de surfaces et de titres ;
- AutomationProperties, focus visible, live status et libellés textuels conservés.

### 10.7 Observabilité — P0 — terminé

- contrat optionnel provider-neutral pour la dernière observation de génération ;
- provider/model/version et outcome `Completed/Cancelled/Failed` ;
- latence end-to-end et temps jusqu’au premier output mesurés côté Alicia ;
- `stream_options.include_usage=true` pour récupérer input/output/total/cached tokens depuis llama.cpp ;
- prompt/eval timing et tok/s capturés uniquement lorsqu’ils sont réellement fournis par le provider ;
- cancellation distinguée d’un échec et échec réduit à la classification sûre `Missing/Unsupported/Faulted/Network/Model` ;
- journal JSONL local borné/rotatif contenant uniquement les metadata structurées de l’observation ;
- aucun prompt, message, raisonnement, réponse, body HTTP, API key, endpoint, log tail ou diagnostic brut dans ce journal ;
- projection UI volontairement secondaire dans `Provider → Technical details`, pour préserver la hiérarchie visuelle 10.7A.

## UIX-01 — Stages 8–10 de clôture foundation

### Stage 8 — FOLLOWING / DETACHED + UI state — terminé

- state machine scroll ;
- seuil de geste réduit à ~32 px ;
- contrôle explicite ↑ pour suspendre le suivi sans scroller ;
- contrôle explicite ↓ pour reprendre le suivi et revenir en bas ;
- streaming ne force jamais le scroll en DETACHED ;
- restore per conversation ;
- persistence historique expanded/collapsed ;
- auto-collapse narrow ;
- Reduced Motion pour le thinking indicator.

### Stage 9 — Décomposition Presentation — terminé

Le monolithe `MainViewModel` est désormais décomposé en slices Presentation explicites sans modifier les contrats Domain/Application :

```text
ConversationWorkspaceViewModel
ConversationHistoryViewModel
ConversationStreamViewModel
ProviderViewModel
ModelViewModel
GenerationSettingsViewModel
```

### Stage 10 — History / gates / responsive completion — terminé

#### Stage 10A — History identity — terminé

- identité visuelle par conversation dans l’historique ;
- catalogue prédéfini de 8 icônes et 7 couleurs ;
- menus `Change icon` / `Change color` depuis clic droit et `…` ;
- persistance par `ConversationId` dans `ui-state.json` v2 ;
- lecture rétrocompatible du format UI-state v1 ;
- suppression de l’identité avec la conversation.

#### Stage 10B — Configuration gate / onboarding — terminé

- diagnostic provider/modèle projeté en un gate Presentation déterministe ;
- historique et messages consultables même si l’IA n’est pas prête ;
- composer remplacé par le gate tant que le provider n’est pas `Running` ;
- onboarding central et historique vide masqué quand aucune conversation n’existe ;
- CTA unique vers Detect/Install/Load ou vers les workspaces Provider/Models ;
- opérations transitoires représentées par un CTA unique désactivé.

#### Stage 10C — Responsive + delete modal — terminé

- seuil étroit porté à 900 DIP pour protéger la largeur utile de la conversation ;
- historique étroit rendu comme overlay temporaire avec backdrop, sans modifier la préférence persistée du layout large ;
- fermeture automatique de l’overlay après sélection ou création d’une conversation ;
- resserrement progressif des marges/paddings à 980/900/700 DIP ;
- remplacement du bandeau de suppression par une modale racine compacte et bloquante `Cancel | Delete` ;
- `Escape` annule la suppression et aucun clic extérieur ne confirme ni ne ferme implicitement la modale.

#### Stage 10D — Accessibility / motion completion — terminé

- focus initial, cycle Tab/Shift+Tab et restauration du focus pour l’overlay history étroit et la modale de suppression ;
- `Escape` ferme d’abord modale/overlay avant toute action Stop ;
- état Automation explicite pour le workspace global courant et pour sélection/métadonnées/identité des conversations ;
- Reduced Motion projeté jusqu’au shell, avec navigation quasi immédiate et indicateurs indéterminés statiques ;
- sémantique heading/progress/live-status complétée sur les surfaces Conversation/Provider ;
- UIX-01 foundation clôturée, sans nouveau contrat Domain/Application.

## Lot 10B — Generation state / revisions / provenance / context

Introduire avant RAG.

### 10B.1 GenerationSnapshot immuable — terminé

- nouveau contrat Application provider-neutral `GenerationSnapshot` ;
- corrélation stable par `ConversationId` et `MessageId` du message utilisateur déclencheur ;
- instant de capture normalisé en UTC ;
- capture défensive de l'identité provider/modèle, de la taille de contexte configurée et des options de génération Alicia ;
- les valeurs non configurées restent `null` afin de préserver la sémantique provider/model default sans inventer de valeur effective ;
- aucun contenu de message, raisonnement, réponse, télémétrie ou diagnostic provider n'est stocké dans ce snapshot ;
- aucune persistance ni liaison aux révisions dans cette étape atomique.

### 10B.2 GenerationProfile immuable — terminé

- nouvel identifiant Application stable `GenerationProfileId` basé sur UUID ;
- nouveau contrat provider-neutral `GenerationProfile` immuable pour le comportement réutilisable d'une génération ;
- profil custom : nom normalisé, instructions système de base optionnelles, copie défensive des options de génération et suggestions initiales immuables ;
- profil `Default` explicite et réservé, sans instructions ni override Alicia, afin de conserver le comportement natif provider/modèle ;
- aucune donnée provider spécifique, conversation, télémétrie, provenance ou contexte dans le profil ;
- l'association à un modèle, l'identité visuelle, le catalogue/persistence, les WorkingDrafts, les révisions et la sélection restent hors de cette étape atomique.

### 10B.3 Catalogue model-scoped, WorkingDrafts et révisions de profils — terminé

- nouveau scope Application `GenerationProfileModelScope` normalisant l'identité provider + modèle sans l'injecter dans le payload `GenerationProfile` ;
- `GenerationProfileRevisionId` UUID stable et `GenerationProfileRevision` immuable avec parent optionnel, timestamp UTC, payload autosuffisant et hash SHA-256 déterministe ;
- `GenerationProfileWorkingDraft` séparé de l'historique confirmé, persistant son éventuelle révision de base et autorisant la conservation d'un draft devenu stale ;
- `GenerationProfileCatalog` immuable : exactement un profil `Default`, chaînes de révisions linéaires par profil custom, noms confirmés courants uniques par modèle, un draft maximum par profil et refus explicite du commit d'un draft stale ;
- port Application `IGenerationProfileCatalogStore` ;
- adapter Infrastructure `JsonGenerationProfileCatalogStore` versionné et atomique, multi-scope, avec reconstruction des invariants et vérification des payload hashes à la lecture ;
- absence de fichier/scope traitée comme absence de catalogue, sans inventer silencieusement de profil custom ;
- aucune sélection de profil par conversation, aucun changement de génération, aucun wiring Presentation/Desktop dans cette étape atomique.

### 10B.4 Sélection conversation modèle/profil et binding de génération — terminé

- nouveau contrat Application `ConversationGenerationSelection` : `ConversationId` + scope provider/modèle + `GenerationProfileId` stable, sans pinner une révision ;
- port `IConversationGenerationSelectionStore` et adapter JSON Infrastructure versionné/atomique, indépendant du JSON Domain des conversations ;
- résolution au début du tour : profil custom -> dernière révision confirmée ; profil `Default` -> identifiant réel avec `ProfileRevisionId = null`, sans fausse révision ;
- `GenerationSnapshot` étendu avec `ProfileId` et `ProfileRevisionId?` afin de figer la révision réellement résolue pour le tour ;
- options de génération et instructions système du profil résolues avant l'appel provider ; les options `null` conservent leur sémantique provider/modèle default ;
- routing Infrastructure d'une requête bindée par le provider capturé et garde llama.cpp refusant un modèle/contexte différent de celui réellement chargé ;
- absence de sélection persistée = chemin legacy inchangé, afin de ne pas migrer silencieusement les réglages globaux existants ;
- composition Desktop des stores de profils/sélections et du resolver ; aucun dual selector Presentation ajouté dans ce lot backend.

### 10B.5a Révisions immuables de messages et migration de persistence — terminé

- nouveau `MessageRevisionId` Domain UUID ; `MessageId` reste l'identité stable de l'entité logique ;
- `ChatMessage` représente désormais une révision confirmée avec parent optionnel et hash SHA-256 déterministe du payload autosuffisant ;
- les nouveaux messages User et les réponses Assistant finalisées créent explicitement une révision racine ; aucun stream partiel ne crée de révision historique ;
- `Conversation` reste linéaire dans cette sous-étape et rejette les doublons de `MessageId` comme de `MessageRevisionId` ;
- `JsonConversationRepository` passe au schema v3 et persiste/vérifie revision, parent et payload hash ;
- lecture rétrocompatible des schemas 0/v2 avec `MessageRevisionId` déterministe dérivé de `ConversationId + MessageId`, stable entre lectures et matérialisé au prochain save explicite ;
- aucune provenance `GenerationSnapshot`, branche, révision de contexte ou UI de timeline n'est introduite ici.

### 10B.5b Registre durable de GenerationSnapshot et provenance Assistant — terminé

- nouveau `GenerationSnapshotId` Domain UUID permettant à une révision Assistant de référencer la provenance sans dépendance Domain -> Application ;
- `GenerationSnapshot` étendu avec identité, révision du message User déclencheur, liste ordonnée exacte des `MessageRevisionId` d'entrée et hash SHA-256 déterministe ;
- validation `ConversationResponseRequest` garantissant que les révisions du snapshot correspondent exactement aux messages réellement envoyés au provider ;
- le chemin sans sélection de profil capture désormais la configuration du provider global persisté, avec `ProfileId = null` et `ProfileRevisionId = null`, sans profil synthétique ;
- nouveau port `IGenerationSnapshotStore` et adapter Infrastructure `JsonGenerationSnapshotStore` immutable, atomique, un fichier versionné par snapshot, avec vérification du payload hash ;
- `ChatMessage` Assistant peut référencer un `GenerationSnapshotId`; les rôles non-Assistant ne le peuvent pas ;
- `JsonConversationRepository` passe au schema v4 pour cette référence tout en relisant v0/v2/v3 ;
- snapshot persisté seulement après succès provider + validation anti-stale, puis conversation persistée sur une copie détachée ; rollback du snapshot si le save conversation échoue ;
- annulation, erreur provider, stream partiel ou historique stale ne créent ni Assistant final ni snapshot durable ;
- aucune branche, révision de contexte, budget explicite, RAG ou UI de provenance n'est introduite dans cette sous-étape.

### 10B.5c Graphe de branches et édition non destructive — terminé

- nouveau `ConversationBranchId` Domain et métadonnées `ConversationBranch` immuables : parent optionnel, dernier `MessageRevisionId` partagé et queue locale ordonnée ;
- registre global de révisions dans `Conversation` : chaque révision confirmée est stockée une seule fois et possédée par une seule queue de branche ;
- `Conversation.Messages` reste la vue linéaire compatible de la branche active, avec résolution d'un enfant par préfixe parent tronqué au point de divergence + révisions locales ;
- édition d'un ancien message User visible : même `MessageId`, nouveau `MessageRevisionId`, parent de révision exact, branche enfant activée et branche source intégralement préservée ;
- sélection explicite d'une branche sans réécriture des messages ni changement artificiel de `UpdatedAt` ;
- stale-turn protection renforcée sur registre de révisions + graphe + branche active, afin qu'un changement de branche pendant une génération invalide la finalisation ;
- `JsonConversationRepository` schema v5 (`messageRevisions`, `branches`, `activeBranchId`) avec migration stable v0/v2/v3/v4 vers une branche racine déterministe ;
- aucune UI de navigation/édition de branches dans cette sous-étape ; contexte révisionné/budget explicite restent 10B.6.

### 10B.6 Révisions/provenance de contexte et budget explicite — terminé

- nouveau `ConversationContextId` stable et `ConversationContextRevisionId` UUID pour des états de contexte confirmés, autosuffisants et hashés SHA-256 ;
- payload atomique 10B.6 limité aux instructions persistantes et au mode explicite `extend`/`replace` des instructions de profil ; RAG, fichiers/assets, tools et multimodal restent hors périmètre ;
- bindings de contexte ordonnés par branche, ancrés après une révision de message ; une branche enfant épingle la révision de contexte réellement effective à son point exact de divergence ;
- contexte parent/enfant modifiable indépendamment sans mutation rétroactive et restauration stricte des invariants d'héritage/ownership ;
- `ConversationTurnSnapshot` capture aussi registre/bindings de contexte afin qu'une modification pendant génération rende le tour stale ;
- resolver context-aware compatible avec l'interface historique, refus explicite de toute génération qui ignorerait silencieusement un contexte actif ;
- `GenerationSnapshot` v2 capture `ContextRevisionId` et `GenerationContextBudget` ; v1 reste lisible avec son hash historique inchangé ;
- budget provider-neutral : fenêtre configurée, réservation de sortie configurée et maximum d'entrée uniquement si les deux valeurs sont connues ; aucune consommation/tokenisation effective inventée ;
- `JsonConversationRepository` schema v6 et `JsonGenerationSnapshotStore` schema v2, avec lectures rétrocompatibles v0/v2/v3/v4/v5 et snapshot v1 ;
- aucune UI Presentation ajoutée dans cette sous-étape.

### Précondition UIX-02 / UIX-03 - sélection génération branch-scoped - phase 1 terminée

- `ConversationGenerationSelection` accepte une portée `ConversationBranchId` explicite tout en conservant la sélection conversation-scoped historique comme fallback de migration ;
- un port `IConversationBranchGenerationSelectionStore` expose explicitement la capacité branch-aware sans donner de fausse sémantique aux adapters historiques ;
- `JsonConversationGenerationSelectionStore` passe au schema v2 et relit le schema v1 sans inventer de branche ;
- la résolution de génération utilise la branche active lorsque resolver et store branch-aware sont disponibles ;
- un fork par édition copie vers l'enfant la sélection effective du parent observée au moment de créer le fork, avec compensation si la sauvegarde de conversation échoue ;
- les changements ultérieurs modèle/profil sur le parent ne modifient pas la sélection déjà épinglée sur l'enfant ;
- limite assumée : le contrat 10B.4 n'ayant pas de timeline de sélection, cette phase ne reconstruit pas encore la sélection historique exacte qui était active au point ancien de divergence ;
- aucune surface Presentation, aucun changement de `GenerationSnapshot`, aucun RAG, tool ou multimodal dans cette phase.

### UIX-02 Stage 1 - sélection de profil pour la branche active - terminé

- Models charge les profils confirmés du scope provider/modèle actuellement sauvegardé via `IGenerationProfileCatalogStore` ;
- un catalogue absent reste absent tant que l'utilisateur n'agit pas : Presentation projette seulement un `Default` transitoire, puis persiste explicitement le catalogue au premier enregistrement de sélection ;
- la sélection effective de la branche active est chargée via `IConversationBranchGenerationSelectionStore`, avec le fallback conversation-scoped 10B.4/ADR 0039 clairement signalé ;
- `Use profile for this branch` persiste une sélection `(ConversationId, ConversationBranchId, ModelScope, ProfileId)` explicite ;
- un fallback legacy sélectionné peut ainsi être épinglé sur la branche sans modifier le profil ;
- une branche déjà liée à un autre modèle n'est jamais réécrite silencieusement : la différence de scope est affichée et la reconfiguration exige l'action explicite ;
- les drafts provider/modèle non sauvegardés, un stream actif ou l'absence de conversation désactivent la sélection ;
- Desktop injecte les ports Application existants dans Presentation, sans dépendance Presentation -> Infrastructure ;
- aucun éditeur de profil, aucune création de profil custom, aucune timeline/restauration de révision, aucune bibliothèque de modèles, aucun load/unload automatique et aucune UI de branches dans cette étape.

### UIX-02 Stage 2 - éditeur de profils et WorkingDraft explicite - terminé

- un `GenerationProfileEditorViewModel` dédié porte le formulaire d'édition sans remettre cette responsabilité dans le facade `MainViewModel` ;
- `New profile` crée une identité stable mais ne confirme rien tant que l'utilisateur n'enregistre ni draft ni révision ;
- `Save draft locally` persiste un `GenerationProfileWorkingDraft` via le store Application existant ; un nouveau profil uniquement drafté reste absent de la projection des profils confirmés ;
- `Save revision` commit le draft via `GenerationProfileCatalog.CommitWorkingDraft` et crée une nouvelle révision immuable ;
- les champs optionnels conservent `null` pour les defaults provider/modèle, y compris un mode reasoning explicitement tri-state `default/off/on` ;
- un WorkingDraft existant est restauré à l'ouverture de l'éditeur ; un draft stale reste visible mais ne peut pas être commit ni rebasé silencieusement ;
- `Discard draft` retire le draft persistant et recharge la dernière révision confirmée ;
- le profil `Default` reste non éditable ; la mutation de profil est désactivée pendant une génération active ;
- la sélection de branche continue de viser le `GenerationProfileId` stable et reste une action distincte de la sauvegarde du profil ;
- autosave des drafts, gate pré-envoi version précédente/modifiée, timeline/restauration des révisions, suppression de profils et dual selector complet restent hors de cette étape.

### UIX-02 Stage 3 - autosave WorkingDraft et gate pré-envoi - terminé

- les champs éditables d'un profil custom déclenchent un autosave local debounced via le `IGenerationProfileCatalogStore` existant ;
- l'éditeur versionne ses changements locaux afin qu'un save asynchrone plus ancien ne puisse jamais réécrire une saisie plus récente ;
- les valeurs intermédiaires invalides restent locales et non persistées jusqu'à redevenir valides ;
- l'éditeur d'un profil custom confirmé est restauré à travers le refresh conversation qui précède le stream ; pendant une réponse active, édition + autosave restent possibles mais `Save revision` reste désactivé ;
- avant un nouvel envoi, Presentation flush le draft puis inspecte le profil réellement résolu pour la branche active, jamais un choix de ComboBox non sauvegardé ;
- si ce profil possède un WorkingDraft, aucun message User n'est persisté et aucun provider n'est appelé avant le choix explicite `Use previous version` / `Use modified version` ;
- la version précédente conserve le WorkingDraft et utilise la dernière révision confirmée ; la version modifiée commit d'abord le draft en nouvelle révision immuable puis envoie ;
- un draft stale désactive la version modifiée sans rebase silencieux ; Cancel/Escape conserve message et draft ;
- Retry d'un message User déjà persisté reste hors de ce gate de nouvel envoi ;
- aucune timeline/restauration visuelle de révisions, suppression de profil, bibliothèque de modèles, dual selector complet, UI branches/context, RAG, tool ou multimodal dans cette étape.

### Etapes suivantes

- UIX-02 : historique/restauration des révisions de profils, puis progression vers le dual selector complet ;
- UIX-03 : contexte, provenance/révisions et navigation de branches progressivement exposés ;
- Lot 11 : RAG foundation après le track UIX recommandé.

## Lots 11 à 15

### Lot 11 — RAG foundation

Knowledge-base Domain, ingestion, chunking, metadata, embeddings, vector store, index lifecycle et tests.

### Lot 12 — Retrieval + grounded generation

Hybrid retrieval, reranking, context budget, citations, grounded prompt/no-answer, dataset d'évaluation et métriques.

### Lot 13 — Tools/MCP/agents

Tool model, function calls/results, permissions, confirmations utilisateur, audit log et MCP. Le framework agent ne doit être réévalué qu'à cette étape.

### Lot 14 — Multimodal

Structured message parts, attachments, stockage image/fichier, mapping provider, UI multimodale et limites de sécurité/taille.

### Lot 15 — Release/platform

Packaging Windows/Linux/macOS, update channel, crash reporting, localisation, E2E, performance, accessibilité, priorités mobile/browser, licence et version/tag/release process.

## Séquence recommandée

```text
Docs sync — terminé
   ↓
Lot 10.1 + 10.2 P0 security/ownership — terminé
   ↓
UIX-01 Stage 8 — terminé
   ↓
UIX-01 Stage 9 — terminé
   ↓
UIX-01 Stage 10A — terminé
   ↓
UIX-01 Stage 10B — terminé
   ↓
UIX-01 Stage 10C — terminé
   ↓
UIX-01 Stage 10D — terminé
   ↓
Lot 10.3 — terminé
   ↓
Lot 10.4 — terminé
   ↓
Lot 10.5 — terminé
   ↓
Lot 10.6 — terminé
   ↓
Lot 10.7 — terminé
   ↓
Lot 10B
   ↓
UIX-02 / UIX-03
   ↓
Lot 11 → 15
```
