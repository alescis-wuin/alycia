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
| Lot 10 Robustesse/sécurité/observabilité | en cours | 10.1–10.6 terminés sur cette branche |
| Lot 10.1/10.2 sécurité + ownership | terminé | session locale durcie |
| UIX-01 Stage 8 | terminé | FOLLOWING / DETACHED + UI state |
| UIX-01 Stage 9 | terminé | décomposition Presentation en six ViewModels |
| UIX-01 Stage 10 | terminé | 10A–10D terminés; UIX-01 foundation clôturée |
| Lot 10B Profiles/revisions/provenance/context | planifié | à faire |
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

### 10.7 Observabilité

- provider/model/version ;
- latence ;
- prompt/eval timing si accessible ;
- input/output token usage ;
- cancellation/failure ;
- aucun prompt/message brut dans les logs par défaut.

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

Introduire avant RAG :

- `GenerationSnapshot` immuable ;
- profils de génération ;
- révisions de messages ;
- provenance et contexte attachés à la génération ;
- branchement de conversation sur révision ;
- budget de contexte explicite.

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
Lot 10.7 — prochain
   ↓
Lot 10B
   ↓
UIX-02 / UIX-03
   ↓
Lot 11 → 15
```
