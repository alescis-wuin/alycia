# Alicia — Spécification globale UI/UX et principes d’interaction

**Version :** v0.2
**Statut :** spécification consolidée — décisions normatives + addendum reasoning validé au Stage 7
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

Suppression : confirmation modale compacte `Annuler | Supprimer`.

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
- le stream suit automatiquement seulement si le nouveau contenu sortirait de la viewport.

### DETACHED

- déclenché lorsque l’utilisateur remonte volontairement ;
- seuil technique initial suggéré : ~96 px depuis le bas ;
- aucun nouveau chunk ne réactive le suivi ;
- bouton flottant `↓` apparaît au-dessus du composer.

Retour à FOLLOWING uniquement par :

- clic sur `↓` ;
- nouvel envoi utilisateur.

La fin du stream ne réactive pas l’auto-scroll.

État et position de scroll persistés **par conversation**, y compris entre les lancements.

---

## 8. Vue Provider

### 8.1 Structure

- aucun historique de conversations ;
- rail global toujours visible ;
- titre + navigation contextuelle en haut ;
- zone gauche divisée :
  - Édition ;
  - Informations ;
- zone droite :
  - Logs/progression.

Sur largeur réduite : Logs passent sous Édition/Informations.

### 8.2 Liste des providers

Liste de cartes compactes, une par provider :

- icône ;
- nom ;
- état.

Exemples d’état :

- Non installé ;
- Prêt ;
- En cours d’installation ;
- Running ;
- Mise à jour disponible ;
- Erreur.

La carte sélectionnée utilise un accent discret.

### 8.3 Divulgation progressive

Non installé :

- description courte ;
- `Installer`.

Installé :

- Démarrer ;
- Arrêter ;
- Mettre à jour si disponible ;
- Désinstaller.

Pas de réglages modèle dans cette vue.

### 8.4 Logs/progression

Pendant installation/démarrage/erreur :

- panneau Logs visible automatiquement ;
- une progression principale :
  - étape ;
  - pourcentage ;
  - détail ;
- timeline discrète des étapes précédentes.

Au repos : panneau repliable/réduit.

### 8.5 Désinstallation

Deux actions explicites :

1. désinstaller le provider/runtime ;
2. désinstaller et supprimer modèles/cache associés.

Aucune suppression de données lourdes implicite.

### 8.6 Informations provider

Affichage direct :

- état ;
- version ;
- backend ;
- GPU détecté ;
- VRAM disponible/totale si disponible.

`Plus de détails` :

- chemin runtime ;
- endpoint ;
- cache ;
- arguments ;
- diagnostic/détection.

Mise à jour : badge discret, non bloquant pour la version en cours.

---

## 9. Vue Modèles

### 9.1 Structure

- aucun historique de conversations ;
- rail global visible ;
- titre + navigation contextuelle ;
- gauche : sélection/édition/informations ;
- droite : logs.

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

## 23. État d’implémentation au checkpoint c51f1ef

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
- Stop/Retry guard.

### Encore requis par cette spécification

- persistence collapse history ;
- auto-collapse responsive sans perte de préférence ;
- FOLLOWING / DETACHED ;
- scroll per conversation ;
- conversation icons ;
- title generation ;
- assistant avatar ;
- hover/focus timestamp/provenance/copy ;
- model/profile dual selector ;
- profiles ;
- context button/panel ;
- one-shot features ;
- configuration gates complets ;
- timeline Message/Event ;
- immutable revisions ;
- GenerationSnapshot ;
- branching ;
- Provider final cards/logs/update/uninstall ;
- Model library/load/unload/profiles ;
- Reduced Motion complet.

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
