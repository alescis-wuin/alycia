# Exporter et réutiliser les réglages d’une discussion

Dans le menu **⋯** d’une discussion, choisir **Exporter les réglages** et enregistrer `alycia-reglages.json`. L’export contient les valeurs enregistrées, y compris les valeurs par défaut effectivement utilisées par le serveur pour une discussion qui n’a pas encore été configurée.

Ouvrir la discussion cible, puis **⋯ → Importer les réglages**. Choisir le fichier, vérifier l’aperçu et cliquer sur **Appliquer à cette discussion**. L’import remplace l’ensemble des réglages ; ce n’est pas une fusion. Fermer l’aperçu ne modifie rien. Les modifications s’appliquent aux prochains échanges.

Sont transférés :

- le nom exact du modèle de conversation ;
- l’instruction système et les notes de contexte privées de la discussion ;
- toutes les options persistantes : température, graine, fenêtre, historique, mémoire, outils, budgets, délais, recherche, embeddings, indexation, annotations et synthèses.

Les valeurs numériques sont transférées sans passer par les champs du formulaire : par exemple un seuil de `0.456789` conserve sa précision. Les réglages d’indexation et les noms des modèles auxiliaires sont conservés. Le fichier ne télécharge aucun modèle.

Le titre, l’identité, le rattachement au projet, les messages, les documents, leurs permissions, la mémoire calculée et les index ne sont pas copiés. Les consignes et documents partagés du projet cible continuent de s’appliquer. La configuration de la discussion peut donc être identique tout en disposant d’un contexte différent. Les réglages globaux du client et les limites globales d’import documentaire ne font pas partie de cet export.

Le changement de modèle réinitialise le résumé généré de la discussion cible, conformément au comportement existant des réglages ; les messages originaux restent conservés. Pour changer de modèle, celui du fichier doit être disponible dans la liste des modèles génératifs locaux du serveur. Aucun modèle de remplacement n’est choisi automatiquement. Les modèles d’embeddings et d’annotation doivent également être installés pour utiliser les fonctions correspondantes.

Le fichier contient les instructions et notes privées en clair : son aperçu les affiche avant application. Il utilise un format JSON versionné (`alycia-conversation-settings`, version `1`), limité à 256 Kio. Un export d’historique complet n’est pas un fichier de réglages. Les fichiers corrompus, tronqués, avec des options manquantes, inconnues ou dupliquées sont refusés ; aucune option manquante n’est remplacée silencieusement par une valeur par défaut. Une discussion modifiée entre-temps provoque un conflit : recharger puis recommencer l’import.

## Récupération et lancement

Arrêter le client et l’API en cours, puis depuis le dépôt local, sous fish ou Bash :

```bash
cd /developpement/seynax/Alycia
git switch feature/local-client-v4-context-quality-20261117
git pull --ff-only origin feature/local-client-v4-context-quality-20261117
make demo-api
```

Dans un autre terminal :

```bash
cd /developpement/seynax/Alycia
make demo-client
```

## Tests ciblés

```bash
dotnet test tests/Alycia.Modules.Chat.Tests --filter FullyQualifiedName~ConversationSettingsTransferTests
dotnet test tests/Alycia.Client.Avalonia.Tests
```

Le transfert réutilise l’API de réglages existante et son contrôle de révision ; aucune migration de base de données n’est ajoutée. L’aller-retour compare toutes les options sérialisées, vérifie les décimales, les notes, le rejet des fichiers incomplets, l’absence de requête d’écriture pour un fichier invalide et le refus de réessayer automatiquement un conflit ou un modèle rejeté.

Validation de livraison : 11 tests du format de configuration et 48 tests du client réussissent avec .NET SDK 10.0.404. Le client Avalonia et l’API ont été compilés lors de ces contrôles, et le lint réussit. Les tests client utilisent HTTP simulé et Avalonia headless ; le sélecteur de fichiers natif de KDE reste à vérifier sur la machine de destination.
