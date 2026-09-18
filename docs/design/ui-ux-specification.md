# Alicia — Spécification globale UI/UX et principes d’interaction

**Version :** v0.4
**Statut :** spécification consolidée — UIX-01 foundation complète (Stages 1–10D)
**Cible :** Alicia Desktop (Avalonia, Windows/Linux/macOS)
**Objectif :** fournir une interface simple, moderne, ludique, accessible et extensible sans accumuler de bruit visuel à mesure que les fonctions IA se complexifient.

---

## 1. Vision UX

Alicia doit suivre quatre principes directeurs :

1. **Sobriété fonctionnelle** — n’afficher que l’action utile au bon niveau de portée ; masquer le complémentaire jusqu’à ce qu’il soit nécessaire.
2. **Séparation claire des espaces** — Conversation, Provider et Modèles sont des vues distinctes.
3. **Portée visible dans la géographie de l’interface** — plus une fonction est globale, plus son point d’entrée est situé à gauche ; plus elle est ponctuelle, plus elle se rapproche du message et de l’action d’envoi.
4. **Traçabilité immuable** — tout état persistant confirmé est versionné ; toute génération conserve la provenance exacte de ce qui l’a produite.

Direction visuelle : **Playful Premium sobre** — fond sombre, accents teal/vert et violet, orange pour édition/attention, rouge pour destructif/erreur, icônes expressives, micro-animations courtes et aucun effet décoratif permanent.

---

## 2. Grammaire spatiale des portées

| Portée | Rôle | Point d’entrée |
|---|---|---|
| **Globale** | Changer de grande zone d’Alicia | Rail global tout à gauche |
| **Collection / intermédiaire** | Choisir l’objet actif | Historique, cartes providers, dual box modèles/profils |
| **Conversation** | Définir le contexte persistant de la conversation | Bouton pile/couches + panneau contexte |
| **Message** | Ajouter une capacité ponctuelle au prochain message | Bouton fonctionnalités ponctuelles |
| **Génération** | Choisir qui répond et avec quel profil | Dual selector modèle/profil dans le composer |
| **Action** | Envoyer | Bouton d’envoi |

Règle : **une capacité doit être affichée au niveau de portée où elle agit**.

---

## 3. Navigation globale

### 3.1 Rail global

- Toujours visible, **icon-only**, environ **56 px**.
- Destinations initiales :
  - Conversations
  - Providers
  - Modèles
- Clic sur une icône : navigation immédiate.
- Survol volontaire : flyout de titres superposé à droite, sans déplacer le contenu.
  - délai d’ouverture cible : ~275 ms ;
  - délai de fermeture après sortie rail + flyout : ~450 ms ;
  - retour du pointeur pendant ce délai : fermeture annulée.
- Focus clavier : flyout ouvert immédiatement.
- Tooltip disponible sur chaque icône.
- État sélectionné : un seul marqueur subtil (accent ou fond arrondi), sans glow permanent.
- Reduced Motion : transitions quasi instantanées.

### 3.2 Historique des conversations

Visible uniquement dans la vue Conversation.

- Largeur cible : **300 px**, non redimensionnable.
- Visuellement fusionné avec le rail global, avec une différence de teinte subtile.
- Peut être replié indépendamment du rail.
- Quand replié : disparition complète de l’historique ; petit bouton flottant arrondi/chevron pour le rouvrir.
- État replié/déplié persisté entre les lancements.
- Sous une largeur insuffisante : repli automatique.

---

## 4. Vue Conversation

### 4.1 Panneau d’historique

En-tête minimal :

- recherche ;
- petit bouton `+` à droite de la recherche ;
- aucun titre « History », compteur, bouton Refresh ou gros bouton « New conversation ».

Chaque conversation affiche uniquement :

- icône personnalisable ;
- titre ;
- bouton `…` au survol/focus.

Actions :

- clic gauche : ouvrir ;
- clic droit : menu contextuel ;
- `…` : même menu contextuel ;
- menu : Renommer (crayon orange), Changer l’icône, Supprimer (rouge, séparé visuellement).

Icônes :

- bibliothèque prédéfinie ;
- emoji ;
- symbole textuel ;
- image personnalisée ;
- couleur personnalisable.

**Implémentation Stage 10A :** la première frontière persistante livre une bibliothèque prédéfinie de 8 catégories et une palette de 7 couleurs. Emoji, symbole arbitraire et image personnalisée restent des extensions compatibles avec cette frontière Presentation ; ils ne sont pas simulés dans Domain/Application.

Suppression : confirmation modale compacte `Annuler | Supprimer`.

#### 4.1.1 Responsive history + suppression — Stage 10C

- seuil de layout Conversation étroit : **~900 DIP** dans la surface Conversation (hors rail global) ;
- en dessous du seuil, l’historique ne réduit plus la largeur des messages : il s’ouvre comme un overlay de **300 DIP** avec backdrop ;
- l’ouverture/fermeture de cet overlay est temporaire et ne modifie jamais la préférence persistée du layout large ;
- sélectionner ou créer une conversation referme l’overlay automatiquement ;
- les marges de contenu passent progressivement de 28 à 18, 14 puis 10 DIP aux seuils 980/900/700 ;
- la confirmation de suppression est une modale racine qui couvre historique + conversation, bloque les interactions en arrière-plan et n’est jamais validée par clic sur le backdrop ;
- ordre des actions destructives : `Cancel | Delete` ; `Escape` annule.

### 4.2 Recherche et preview

Recherche dans :

1. correspondance exacte du titre ;
2. correspondance partielle du titre ;
3. contenu des messages.

Le titre est fortement prioritaire sur le contenu.

Au survol d’une conversation (~350 ms) :

- preview flottante à droite du panneau ;
- maximum 3 messages ;
- 2–3 lignes maximum par message ;
- aucune date/heure ;
- si la conversation apparaît grâce à la recherche de contenu, la preview privilégie le passage pertinent plutôt que les derniers messages.

### 4.3 Messages

- Assistant à gauche, utilisateur à droite.
- Largeur maximale des bulles : **72 %**.
- Pas de largeur minimale forcée.
- Avatar Alicia uniquement ; pas d’avatar utilisateur.
- Au survol/focus d’un message :
  - timestamp en dessous à gauche ;
  - provenance modèle/profil ;
  - bouton Copier à droite sous forme d’icône uniquement.
- Aucun badge de provenance permanent sur chaque message.

### 4.4 Timeline d’événements

L’historique est une timeline de **messages + événements**, les événements n’étant pas des ChatMessage.

Exemples :

- `Profil : Code → Créatif`
- `Profil Code mis à jour`
- `Modèle : Qwen3 4B → Llama 3.1 8B`
- `architecture.md ajouté au contexte`
- `5 fichiers ajoutés au contexte`
- `Instructions de conversation modifiées`
- fichier/lien/note retiré du contexte
- création de branche.

Les événements restent compacts et secondaires.
Un clic sur un événement de contexte ouvre le panneau Contexte sur la révision/les éléments concernés.

### 4.5 Titre de conversation

Création implicite de la conversation :

- clic sur une suggestion ; ou
- saisie directe + envoi.

Aucune étape artificielle « créer une conversation vide ».

Titre provisoire :

- extrait du premier message utilisateur ;
- ~44 caractères maximum ;
- coupé proprement à un mot ;
- `…` ;
- italique + teinte atténuée.

Titre définitif :

- généré après la première réponse Assistant terminée avec succès ;
- entrée = premier message User + première réponse Assistant ;
- aucune génération si réponse annulée/échouée ou titre déjà renommé manuellement ;
- si échec du titrage : conserver le titre provisoire.

Plages proposées :

| Taille du contenu initial | Titre demandé |
|---|---|
| 0–160 caractères | 12–28 caractères |
| 161–600 | 18–40 |
| >600 | 24–52 |
| Maximum absolu | 56 |

Budget de génération du titre : environ 24–32 tokens.

### 4.6 Empty states

**Aucun provider/modèle prêt + aucune conversation :**
- pas de panneau d’historique ;
- vue d’onboarding guidée vers Provider puis Modèle ;
- navigation contextuelle affichée.

**Conversations existantes mais IA indisponible :**
- historique et messages restent consultables ;
- composer remplacé par un composant d’état ;
- un seul CTA principal exact : installer/démarrer/configurer/charger selon le diagnostic.

**Tout prêt + aucune conversation :**
- icône Alicia ;
- `Comment puis-je vous aider ?`
- 4 suggestions, deux colonnes, largeur identique ;
- composer en bas.
- Les suggestions peuvent provenir du profil sélectionné.

#### 4.6.1 Gate de configuration — Stage 10B

Tant que le provider local n’est pas `Running`, le composer n’est pas présenté comme disponible. Alicia affiche une seule action principale déterminée par le diagnostic courant :

| Diagnostic | CTA |
|---|---|
| aucun provider | `Configure provider` |
| provider non inspecté | `Check provider` |
| provider absent | `Install provider` |
| provider unsupported/faulted | `Review provider` |
| modèle/configuration absente | `Configure model` |
| réglages modèle non sauvegardés | `Review model settings` |
| runtime + modèle prêts | `Load model` |
| opération en cours | CTA unique désactivé reflétant la phase |

Avec un historique existant, le gate remplace uniquement le composer : les conversations et messages restent consultables. Sans aucune conversation et avec l’IA indisponible, le gate devient l’onboarding central et le panneau d’historique vide est masqué. Les actions de configuration ouvrent les workspaces globaux correspondants plutôt que de dupliquer leurs éditeurs dans Conversation.

---

## 5. Composer

### 5.1 Principe

Le composer est le centre de commande local de la conversation, mais ne doit pas devenir une barre d’outils.

Groupes de portée :

- **gauche bas** : contexte de conversation + modèle/profil ;
- **droite bas** : fonctionnalités ponctuelles du message + envoi.

### 5.2 Disposition adaptative

État compact possible si tout tient confortablement :

`[Contexte] [Modèle | Profil]  Texte flexible  [Fonctions] [Envoyer]`

Quand le texte devient long ou multiligne :

```text
┌──────────────────────────────────────────────────────┐
│ Zone de texte transparente                           │
│ ligne 2...                                           │
│                                                      │
│ [Pile] [Modèle | Profil]            [✦] [Envoyer]   │
└──────────────────────────────────────────────────────┘
```

Règles :

- conteneur opaque ;
- champ texte transparent/quasi transparent ;
- actions toujours stables en bas en mode développé ;
- hauteur maximale du champ : au-delà, scroll interne.

### 5.3 Bouton Contexte

Icône retenue : **pile/couches**.

Sémantique :

- contexte permanent de la conversation ;
- instructions ;
- fichiers ;
- liens ;
- notes ;
- futurs réglages RAG/mémoire.

Ce bouton est distinct du bouton de fonctionnalités ponctuelles.

### 5.4 Bouton Fonctions ponctuelles

Concerne le prochain message uniquement :

- ajouter un fichier ponctuel ;
- générer une image ;
- utiliser un outil ;
- futures actions multimodales.

La fonction Context n’est pas un sous-menu de ce bouton.

### 5.5 Dual selector modèle/profil

Dans le composer : contrôle compact unique, par exemple :

`Qwen3 4B | Code ▾`

Au clic : popup dual box :

- colonne gauche = modèles ;
- colonne droite = profils du modèle sélectionné.

Le choix modèle/profil est persisté par conversation et restauré après changement de conversation ou redémarrage.

---

## 6. Panneau Contexte de conversation

### 6.1 Position

Position retenue : **à gauche de la zone de messages**, entre historique et messages.

Grand écran :

`[Rail][Historique][Contexte][Messages]`

- docké ;
- réduit la largeur de messages + composer ;
- largeur confortable cible : **360–420 px**.

Fenêtre étroite :

- overlay au-dessus de la zone messages/composer ;
- clic extérieur / Esc pour fermer.

Cette position reflète la règle : le contexte est plus global qu’un message mais plus local que la liste de conversations.

### 6.2 Contenu

Ordre initial :

1. résumé ;
2. instructions ;
3. fichiers ;
4. liens ;
5. notes.

Instructions :

- par défaut, elles **étendent** les instructions du profil ;
- case à cocher disponible : `Remplacer totalement les instructions de base du profil` ;
- non cochée par défaut.

Toute modification confirmée du contexte crée une nouvelle révision et un événement de timeline.

---

## 7. Scroll et streaming

Deux états :

- `FOLLOWING`
- `DETACHED`

### FOLLOWING

- activé à l’envoi d’un message utilisateur ;
- le stream suit automatiquement seulement si le nouveau contenu sortirait de la viewport ;
- un bouton arrondi centré `↑ Pause auto-scroll` est affiché au-dessus du composer pour suspendre explicitement le suivi sans devoir scroller.

### DETACHED

- déclenché par `↑ Pause auto-scroll` ou lorsque l’utilisateur remonte volontairement ;
- seuil technique cible : ~32 px depuis le bas ;
- aucun nouveau chunk ne réactive le suivi ;
- le contrôle central devient `↓ Resume & jump to latest`.

Retour à FOLLOWING uniquement par :

- clic sur `↓ Resume & jump to latest` ;
- nouvel envoi utilisateur.

La fin du stream ne réactive pas l’auto-scroll.

État et position de scroll persistés **par conversation**, y compris entre les lancements.

---

## 8. Vue Provider

### 8.1 Structure — refinement 10.7A

La vue Provider doit rester une surface de contrôle, pas un tableau de bord technique permanent. L'ordre de lecture est :

1. **Runtime** — sélection, état, version, progression et lifecycle ;
2. **Runtime updates** — comparaison managed/validated/upstream et actions explicites ;
3. **Maintenance** — repliée par défaut ;
4. **Danger zone** — uniquement dans Maintenance ;
5. **Technical details** — repliés par défaut.

Le contenu est centré dans une largeur de lecture bornée. Les grandes cartes concurrentes `Édition | Informations` sont supprimées : une surface principale porte la tâche Runtime, les blocs secondaires utilisent une surface plus discrète et l'espace remplace les séparateurs répétitifs.

### 8.2 Hiérarchie des actions

- **Start** conserve la plus forte emphase dans la vue Provider ;
- Detect, Install, Stop, Check update et Update validated restent immédiatement accessibles mais visuellement secondaires ;
- les actions destructives ne partagent jamais la même emphase que le lifecycle normal ;
- aucun bouton, binding, état d'activation ou contrat provider n'est modifié par 10.7A.

### 8.3 Divulgation progressive

Le normal path ne doit pas exposer en permanence les actions rares :

- Maintenance est repliée au repos ;
- les tailles runtime/cache, cleanup et désinstallation apparaissent après ouverture de Maintenance ;
- Technical details est un second disclosure pour les informations supplémentaires ;
- progression et erreurs restent visibles dans la section où l'action a lieu ;
- les données indispensables à la décision ne sont pas cachées dans un tooltip.

### 8.4 Progression et statut

Pendant installation/démarrage/update :

- une seule progression principale est affichée près du Runtime ;
- étape, pourcentage et détail restent annoncés par live status ;
- Reduced Motion conserve le texte même lorsque l'indeterminate animation est supprimée.

Au repos, la progression disparaît et le statut textuel demeure dans l'en-tête.

### 8.5 Désinstallation

Deux actions explicites restent inchangées :

1. désinstaller le provider/runtime ;
2. désinstaller et supprimer modèles/cache associés.

`Clean old releases` reste une troisième maintenance distincte. Toutes les suppressions restent sous confirmation explicite et la Danger zone est séparée visuellement du lifecycle normal.

### 8.6 Informations provider

Affichage direct :

- état ;
- provider ;
- version détectée ;
- releases managed/validated/upstream dans Updates ;
- détail utilisateur sûr.

Détails secondaires :

- état de configuration ;
- rappel que modèle/génération appartiennent à Models ;
- diagnostics techniques bruts restent hors Presentation.

### 8.7 Accessibilité visuelle

- hiérarchie par titres, proximité et espace avant d'ajouter des lignes/bordures ;
- un état n'est jamais exprimé uniquement par couleur ;
- focus visible conservé ;
- libellés textuels conservés sur les actions critiques ;
- AutomationProperties, HeadingLevel et LiveSetting existants restent présents ;
- les disclosure controls utilisent le contrôle natif `Expander`.

---

### 8.7 Observabilité — Lot 10.7

L’observabilité ne doit pas transformer Provider en dashboard permanent. La dernière génération est projetée dans **Technical details**, replié par défaut, avec : outcome, provider/modèle/version, latence end-to-end, temps au premier output, tokens input/output/cache et timings/tok/s uniquement lorsqu’ils sont fournis. L’absence de métrique est affichée comme indisponible et n’est jamais estimée silencieusement.

Le journal structuré local ne contient aucun prompt/message, raisonnement, réponse, body HTTP, secret, endpoint, log tail ou diagnostic brut. L’état `Cancelled` est distinct de `Failed`; l’échec n’expose que la classification provider-neutral sûre.

## 9. Vue Modèles

### 9.1 Structure

- aucun historique de conversations ;
- rail global visible ;
- titre + navigation contextuelle ;
- une surface principale centrée pour la configuration ;
- résumé provider secondaire dans la même surface, sans carte concurrente ;
- Runtime et Generation conservent leurs disclosures existants ;
- la grammaire visuelle est alignée avec Provider sans modifier les bindings ni la sauvegarde.

### 9.2 Ajout Hugging Face

Champ texte direct :

`Ajouter un modèle Hugging Face`

L’utilisateur saisit `owner/model` (quantification optionnelle si supportée).

Si validation/chargement réussit :

- Alicia crée une carte modèle persistante.

### 9.3 Dual box Modèles / Profils

Deux colonnes :

**Modèles :**
- icône ;
- nom ;
- quantification ;
- état.

**Profils du modèle sélectionné :**
- icône ;
- nom ;
- état éventuel.

Le profil `Par défaut` :

- toujours présent ;
- utilisable ;
- non éditable ;
- non renommable ;
- non supprimable.

Les profils sont spécifiques à un modèle dans la première version.

### 9.4 Chargement

Un seul modèle chargé à la fois pour le moment.

`Charger le modèle` :

- si provider arrêté : démarrage explicite du provider ;
- puis chargement modèle ;
- puis état Prêt.

Séquence affichée dans progression/logs :

`Démarrage provider → Chargement modèle → Prêt`

Changement de modèle bloqué pendant un stream actif.

### 9.5 Paramètres

Séparation :

**Runtime** — paramètres nécessitant éventuellement reload :
- contexte ;
- futurs réglages runtime.

**Génération** — sans redémarrage :
- prompt/instructions système du profil ;
- max output tokens ;
- température ;
- top_p ;
- top_k ;
- seed ;
- suggestions initiales ;
- futurs penalties/reasoning compatibles.

Champ vide = valeur native provider/modèle ; aucune valeur magique Alicia.

### 9.6 Suppression

Deux actions :

1. retirer de la bibliothèque Alicia ;
2. supprimer le modèle et ses fichiers locaux.

Si chargé : décharger avant suppression.

---

## 10. Profils de génération

### 10.1 Rôle

Un profil décrit **comment un modèle répond**.

Exemples :

- Par défaut ;
- Code ;
- Créatif ;
- Réponses courtes.

Un profil contient :

- nom ;
- icône/couleur ;
- instructions système de base ;
- température ;
- top_p ;
- top_k ;
- seed ;
- max output tokens ;
- suggestions initiales.

### 10.2 Profil par défaut

- non modifiable ;
- non supprimable ;
- toujours disponible ;
- reflète les comportements natifs du provider/modèle.

### 10.3 Brouillons persistants

Une édition non confirmée produit un **WorkingDraft** :

- sauvegardé automatiquement localement ;
- restauré après changement de conversation, redémarrage application ou machine ;
- statut compact : `Brouillon non enregistré localement` ;
- tooltip explicatif.

Pendant un stream utilisant le profil édité :

- le brouillon reste modifiable/persisté ;
- action de sauvegarde désactivée ;
- texte et tooltip indiquent qu’une génération utilisant ce profil est en cours.

Avant un nouvel envoi avec un profil modifié non confirmé, le composer est bloqué et propose :

- `Utiliser la version précédente`
- `Utiliser la version modifiée`

`Utiliser la version modifiée` crée une nouvelle révision immuable avant la génération.

### 10.4 Historique des profils

Le modèle de données et les API de révision sont prévus dès maintenant.
L’UI complète de timeline/restauration de versions est différée.

---

## 11. Navigation contextuelle de configuration

Rôle distinct du rail global :

- rail global = où aller ;
- navigation contextuelle = quelles étapes sont nécessaires pour rendre Alicia opérationnelle.

Elle apparaît uniquement lorsqu’un parcours de configuration est utile.

Exemple :

`Provider → Modèle → Conversation`

États :

- actif ;
- terminé ;
- en attente ;
- erreur.

Les étapes sont cliquables.

Quand tout est prêt : la navigation contextuelle disparaît.

Retour Conversation : restaure conversation sélectionnée, scroll, état de l’historique et modèle/profil.

---

## 12. Révisions, snapshots et provenance

### 12.1 Principe général

**Toute modification confirmée d’un élément persistant crée une nouvelle révision immuable.**

Chaque entité possède :

- `EntityId` stable (UUID).

Chaque révision possède :

- `RevisionId` (UUID) ;
- `EntityId` ;
- `ParentRevisionId?` ;
- timestamp ;
- payload autosuffisant ou référence immutable/hash ;
- `PayloadHash`.

Une révision doit être reconstruisible sans devoir rejouer toute la chaîne de patches.

Les brouillons restent séparés de l’historique confirmé.

### 12.2 Exemples de création de révision

- sauvegarde profil ;
- modification confirmée du contexte ;
- envoi de message utilisateur ;
- édition d’un ancien message ;
- réponse Assistant finalisée ;
- titre généré ;
- fichier/image généré ou transformé.

Un stream partiel ne crée pas de message historique final.

### 12.3 GenerationSnapshot

Tout élément généré référence les révisions ayant contribué à sa génération.

Exemple :

- provider ;
- modèle ;
- révision du profil ;
- révision du contexte ;
- messages d’entrée ;
- assets ;
- outils ;
- options effectives.

Chaque message Assistant référence uniquement un `GenerationSnapshotId`.

Les snapshots identiques peuvent être dédupliqués.

### 12.4 Registre local à la conversation

La sauvegarde d’une conversation peut conserver un registre des versions/snapshots réellement utilisés.

Les messages référencent des UUID, pas des indices, pour rester robustes aux migrations, tris et déduplications.

Les anciennes versions ne sont jamais réécrites lors d’une modification ultérieure.

---

## 13. Conversations branchables

L’historique n’est pas une liste mutable mais un **graphe orienté**.

Éditer un message ancien ne remplace jamais l’original :

```text
U1 → A1 ──┬→ U2 → A2 → U3 → A3
          └→ U2' → A2'
```

Le préfixe immuable est partagé, pas dupliqué.

Principes :

- chaque branche possède son identité ;
- chaque branche hérite de l’état au point de divergence ;
- modèle/profil/contexte peuvent ensuite diverger indépendamment ;
- l’ancienne branche reste intacte ;
- événements et messages sont des nœuds de timeline.

Concepts prévus :

- Conversation ;
- ConversationBranch ;
- MessageNode ;
- EventNode ;
- révisions immuables ;
- snapshot de génération.

---

## 14. Accessibilité et ergonomie

Obligatoire :

- navigation clavier complète ;
- focus visible ;
- tooltips sur icon-only ;
- noms accessibles AutomationProperties ;
- ordre de tabulation cohérent ;
- Esc ferme menu/panneau/modal ;
- grandes cibles d’interaction ;
- contrastes suffisants ;
- annonces polies pour progression/erreurs ;
- aucune information portée uniquement par la couleur ;
- Reduced Motion respecté.

Animations :

- 120–220 ms en général ;
- easing doux ;
- aucune animation continue décorative ;
- jamais bloquante.

**Implémentation Stage 10D :**

- l’overlay history étroit et la confirmation de suppression deviennent des régions de focus temporaires avec focus initial, cycle Tab/Shift+Tab et restauration best-effort ;
- `Escape` ferme ces surfaces avant d’être interprété comme Stop de génération ;
- le workspace global courant et l’état sélectionné/métadonnées/identité des conversations sont exposés aux technologies d’assistance sans dépendre de la couleur ;
- Reduced Motion couvre aussi la temporisation du flyout global et les progressions indéterminées, tout en conservant les libellés/live-status.

Erreurs :

- toast pour événement ponctuel ;
- erreur inline persistante si une action utilisateur est requise.

---

## 15. Responsive

Conversation :

- panneau historique replié automatiquement si largeur insuffisante ;
- contexte docké sur grand écran ;
- contexte en overlay sur largeur insuffisante.

Provider / Modèles :

- desktop : panneaux Édition/Informations à gauche, Logs à droite ;
- étroit : Logs sous les autres panneaux.

Le changement de layout ne doit jamais modifier la hiérarchie logique des portées.

---

## 16. Éléments UI à retirer ou masquer

À retirer de la vue Conversation actuelle :

- titre Alicia ;
- `LOCAL CONVERSATIONS` ;
- titre `History` ;
- compteurs d’historiques/messages ;
- titre permanent de conversation ;
- bouton Refresh ;
- gros bouton `+ New conversation` ;
- bloc Provider/Modèle dans la sidebar Conversation ;
- texte `Start the selected provider before sending messages` ;
- texte `Local history ready • AI provider stopped` ;
- aide permanente `Ctrl+N / F5 / F2 / Esc` ;
- boutons rename/delete permanents ;
- textes techniques permanents et statuts verbeux.

Les raccourcis clavier peuvent rester fonctionnels sans être affichés en permanence.

---

## 17. Inventaire des composants

### À créer

- `GlobalNavigationRail`
- `GlobalNavigationFlyout`
- `ConversationHistoryPane`
- `ConversationHistoryItem`
- `ConversationPreviewFlyout`
- `ConversationContextMenu`
- `ConversationView`
- `ConversationTimeline`
- `ConversationEventItem`
- `ChatMessageBubble`
- `ConversationComposer`
- `ConversationContextButton`
- `MessageFeaturesButton`
- `ModelProfileSelector`
- `ConversationContextPanel`
- `ConversationEmptyState`
- `ConfigurationGate`
- `ScrollToLatestButton`
- `ContextualSetupNavigation`
- `ProviderView`
- `ProviderCard`
- `ProviderEditorPanel`
- `ProviderInformationPanel`
- `OperationLogPanel`
- `OperationProgress`
- `ModelView`
- `ModelProfileDualBox`
- `HuggingFaceModelInput`
- `ModelCard`
- `GenerationProfileCard`
- `ProfileEditor`
- `RuntimeSettingsPanel`
- `GenerationSettingsPanel`
- `ToastHost`
- `ConfirmationDialog`

### À refondre

- `MainView` → shell léger + vues dédiées ;
- `MainViewModel` → séparation des responsabilités ;
- projection des messages → provenance + hover actions ;
- persistence conversation → timeline, branches, UI state, provenance ;
- persistence configuration → profils/révisions/drafts ;
- orchestration navigation → globale + contextuelle.

ViewModels suggérés :

- `ShellViewModel`
- `ConversationViewModel`
- `ConversationHistoryViewModel`
- `ConversationContextViewModel`
- `ProviderViewModel`
- `ModelViewModel`
- `ProfileEditorViewModel`

### À supprimer de l’affichage

Tous les éléments listés en section 16 ; les comportements utiles restent accessibles via menu contextuel, tooltip ou vue dédiée.

---

## 18. Schémas de navigation

### Navigation principale

```text
                   ┌──────────────┐
                   │ Conversations│
                   └──────┬───────┘
                          │
              ┌───────────┴───────────┐
              │       Rail global      │
              └───────┬────────┬───────┘
                      │        │
                 Provider    Modèles
```

### Configuration nécessaire

```text
Conversation
     │ IA indisponible
     ▼
Provider ─────► Modèle ─────► Conversation
 installer      configurer       reprendre
```

### Contexte de conversation

```text
Conversation
   │ bouton pile/couches
   ▼
Panneau Contexte
   ├── Instructions
   ├── Fichiers
   ├── Liens
   └── Notes
        │ modification confirmée
        ▼
ContextRevision + événement timeline
```

### Chargement d’un modèle

```text
Charger modèle
     │
     ├── Provider arrêté → Démarrer provider
     │
     └────────────────────► Charger modèle
                               │
                               ▼
                              Prêt
```

---

## 19. Références visuelles validées

Les mockups Conversation et Modèles ainsi que l’icône pile/couches servent de **références conceptuelles**, pas de spécification pixel-perfect.

---

## 20. Décisions ouvertes / différées

À préciser plus tard sans bloquer la fondation :

- design exact de la navbar contextuelle ;
- seuils responsive exacts ;
- UI complète de l’historique/restauration des révisions ;
- UI des branches de conversation ;
- détail des actions du bouton fonctionnalités ponctuelles ;
- comportement final des pièces jointes ponctuelles ;
- politique d’archivage/GC des blobs et snapshots ;
- visualisation/comparaison des révisions ;
- catalogues de modèles/providers futurs ;
- RAG, outils, images et multimodal.

---

## 21. Critère de validation avant implémentation

La fondation UI/UX peut être considérée prête lorsque :

- la séparation globale / collection / conversation / message est comprise et stable ;
- Conversation, Provider et Modèles ont des responsabilités exclusives ;
- le composer expose peu d’actions mais donne accès à toute la complexité future ;
- le contexte permanent est distinct des fonctions ponctuelles ;
- l’historique combine messages et événements sans bruit ;
- la provenance et le branching sont supportés par les données ;
- responsive, clavier, Reduced Motion et accessibilité sont intégrés au design dès la base.

**Précondition technique indépendante :** fermer les validations/tests encore instables avant le premier patch de refonte UI, afin de partir d’un checkpoint entièrement vert.

---

## 22. Raisonnement de modèle — décision validée

### 22.1 Portée

Le raisonnement est un **paramètre de génération du modèle**, donc son contrôle appartient à la vue **Modèles**, et non à Provider ni au menu ponctuel du message.

Paramètres initiaux :

- `Raisonnement` : activé/désactivé ;
- `Budget de raisonnement` : nombre positif de tokens ;
- suggestion initiale UI : **512 tokens**.

Un ancien réglage non défini peut rester provider-default au niveau du contrat. Lorsqu’un utilisateur sauvegarde explicitement le toggle, son choix devient explicite.

### 22.2 Requête provider

Pour llama.cpp, état explicitement désactivé :

```json
{
  "reasoning_effort": "none",
  "thinking_budget_tokens": 0
}
```

État activé :

```json
{
  "thinking_budget_tokens": 512,
  "reasoning_format": "deepseek"
}
```

Le budget exact est celui choisi par l’utilisateur.

`reasoning_format` est une décision de l’adapter llama.cpp et devra devenir capability-aware si plusieurs familles de modèles nécessitent des parsers différents.

### 22.3 Streaming

Le flux Application distingue :

```text
Reasoning
Content
```

Les providers peuvent produire les deux dans un même événement SSE.

Règles :

- `Reasoning` est visible dans l’UI ;
- `Content` constitue la réponse finale ;
- seul `Content` est persisté dans le `ChatMessage` Assistant actuel ;
- un stream terminé sans `Content` visible est un échec de réponse ;
- le reasoning brut reste éphémère tant que le système de provenance/révisions n’a pas défini sa politique de persistence.

### 22.4 Indicateur d’activité

Avant le premier delta utile, afficher :

```text
Alicia réfléchit.
Alicia réfléchit..
Alicia réfléchit...
```

Puis boucler.

Cadence de référence actuelle : ~420 ms.

Avec **Reduced Motion**, ne pas utiliser une animation continue : conserver un libellé statique du type `Alicia réfléchit…`.

### 22.5 Surface Raisonnement

Lorsque des deltas Reasoning existent :

- surface plus sombre/secondaire que la réponse finale ;
- `Expander` accessible ;
- ouvert pendant le stream ;
- replié après succès ;
- séparateurs d’étapes discrets ;
- une double coupure de ligne peut servir de segmentation heuristique initiale ;
- aucune date/heure ou métadonnée technique répétitive dans chaque étape.

Le raisonnement n’est jamais mélangé visuellement au texte final.

### 22.6 Stop / Retry

Afin d’éviter une annulation réflexe lorsque le modèle réfléchit mais n’a pas encore émis de Content :

- Stop peut être bloqué environ **1,2 s** après le début ;
- Retry peut être retardé environ **700 ms** après interruption/erreur.

Ces durées sont UX, pas des invariants métier ; elles doivent rester testables/injectables.

---

## 23. État d’implémentation après UIX-01 Stage 10D

### Implémenté

- Shell Presentation ;
- rail global 56 px ;
- Conversations / Provider / Models ;
- flyout global différé ;
- Conversation-only workspace ;
- recherche history titre + contenu ;
- preview 3 messages ;
- menus `…` et clic droit ;
- collapse history transient ;
- surface Conversation polish ;
- composer shell opaque/click-to-focus ;
- Send icon-only ;
- reasoning toggle/budget ;
- reasoning SSE séparé ;
- thinking indicator ;
- reasoning expander/steps ;
- Stop/Retry guard ;
- FOLLOWING / DETACHED + scroll persistant par conversation ;
- identité icône/couleur de conversation ;
- gate de configuration déterministe ;
- history responsive en overlay + modale de suppression ;
- accessibilité clavier/focus/Automation et Reduced Motion foundation ;
- UIX-02 Stage 1 : profils confirmés du modèle sauvegardé projetés dans Models et sélection explicite du profil pour la branche active, sans édition de profil ni bibliothèque de modèles.

### Encore requis par cette spécification

- title generation ;
- assistant avatar ;
- hover/focus timestamp/provenance/copy ;
- model/profile dual selector complet avec bibliothèque de modèles ;
- création/édition des profiles, WorkingDrafts et historique de révisions ;
- context button/panel ;
- one-shot features ;
- timeline Message/Event ;
- immutable revisions ;
- GenerationSnapshot ;
- branching ;
- Provider final cards/logs/update/uninstall ;
- Model library/load/unload/profiles ;

---

## 24. Relation avec les Lots racines

Cette spécification est un **track UI/UX transversal**.

Elle ne remplace pas les Lots produit suivants :

- robustesse provider / sécurité / observabilité ;
- provenance/révisions/context ;
- RAG ;
- tools/MCP ;
- multimodal ;
- release/platform.

Les écrans Context/Profile/Timeline doivent être raccordés à de vrais contrats Domain/Application, pas simulés uniquement en Presentation.
