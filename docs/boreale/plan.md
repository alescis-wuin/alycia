# Lot UI/UX Boréale

Spécifications v0.2 validées le 18 novembre 2026. Base analysée `0d4c1d9`.
Le client affichait la recherche, le modèle, les options techniques et les actions de conversation dans le fil. Les documents et leurs originaux appartenaient à une conversation. Le registre Workspaces fournissait déjà les identités de projet. Markdig construisait des contrôles Avalonia avec des couleurs fixes.

| Phase | Étapes dans l’ordre | Vérification attendue |
| --- | --- | --- |
| 1. Composants natifs | Embarquer Luciole et Hack avec leurs notices. Définir les ressources Aube/Nuit. Préparer le rendu mathématique, ses limites et le repli TeX. | Polices, contrastes, résolution de ressources, formule native |
| 2. Projets | Relier les conversations au registre Workspaces. Ajouter les métadonnées, les notes, les documents et les exclusions. Adapter recherche, contexte, inventaire, outils et migration. | Deux conversations dans un projet, un autre projet et une conversation indépendante |
| 3. Conversation | Recomposer navigation, en-tête et saisie. Raccorder le projet actif, le modèle, les raccourcis et les brouillons. | Changement de portée, historique paginé, génération et arrêt |
| 4. Espaces de travail | Documents, aperçu, import et modes. Consignes et notes. Inspecteur des sources, de la mémoire et du contexte. Paramètres par portée. | Parcours depuis la fenêtre principale |
| 5. Réponses | Adapter Markdig aux thèmes. Colorer le code. Rendre les formules et les notes. Préserver la copie et le défilement. | Corpus de rendu, erreur de syntaxe et flux incomplet |
| 6. Recette et remise | Régression, migration et isolation. Consigner les résultats exécutés. Décrire installation, sauvegarde, exécution et utilisation sur CachyOS. | Rapport et guide avec limites vérifiables |
