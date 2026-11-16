# Alycia — client local V1 : audit et plan

État observé le 16 novembre 2026 via le connecteur GitHub et le dépôt cloné.
Branche de travail : `feature/local-client-v1-20261116`.
Base immuable : `e1c5bc08e7873aa6f43689a91dc09a37da9db75b`.

## Comparaison des branches

| Branche | Commit observé | Avance sur main | Fonctionnalités |
|---|---|---:|---|
| main | b1777f6 | 0 | API et déploiement initiaux |
| testing | b1777f6 | 0 | Identique à main |
| develop | 8179f9d | 22 | Avalonia, diagnostics, Workspaces PostgreSQL, tests |
| prototype/local-ollama-chat | 3e1c49f | 25 | Chat Ollama, historique, fenêtre glissante, interface |
| prototype/conversation-files | e1c5bc0 | 27 | Fichiers persistants, archives, contexte documentaire, Windows |

`prototype/conversation-files` descend de toutes les autres branches présentes :
5 commits devant develop et 2 devant le prototype Ollama. C'est la base
fonctionnelle la plus avancée, sans que cela constitue une validation d'exécution.
La PR #45 (initialisation du propriétaire) est fermée sans fusion : elle ne fait
pas partie du produit intégré. Aucun ticket ouvert au moment de l'audit.

## Constats

- Architecture existante conservée : .NET 10, Avalonia, API ASP.NET, modules
  Domain/Application/Infrastructure, EF Core et PostgreSQL ; Ollama local.
- Les échanges complets sont persistés atomiquement. Les identifiants de requête
  empêchent une répétition après une réponse réseau ambiguë. Une révision EF
  empêche l'enregistrement concurrent d'une réponse obsolète.
- Absence de streaming ; l'utilisateur attend toute la génération. Les métriques
  Ollama ne sont pas remontées. Le raisonnement séparé n'est pas traité.
- Modèle et instruction figés à la création. Pas de profils de prompts, de notes
  persistantes ni de réglages de génération par conversation.
- Historique paginé côté messages, mais liste de conversations et pièces jointes
  chargées sans sélection. Pas de recherche, d'échange JSON/Markdown ou de fork.
- La fenêtre protège les paires user/assistant, avec estimation conservatrice
  UTF-8, pas un tokenizer propre au modèle. Aucune prévisualisation du contexte.
- Les documents suivent leur position dans la fenêtre ; impossible de garder
  explicitement un document de référence ou de l'exclure sans le supprimer.
- L'import de dossiers ouvre aussi les dossiers générés ou fichiers secrets ;
  les limites de taille sont présentes mais certains parseurs restent coûteux.
- L'interface ne sépare pas le brouillon de chaque conversation et n'offre pas
  de copie du code, de sauvegarde d'historique ou de réglages accessibles.
- Accès local protégé contre les origines navigateur, redirections désactivées,
  modèles cloud filtrés. Ce n'est pas un service multi-utilisateur authentifié.
- Tests PostgreSQL conditionnels : une suite verte peut contenir des tests sautés.
  L'exécution réelle sur GPU, KDE/Wayland et Docker doit être distinguée des tests
  avec fournisseurs simulés.

## Phases, lots et étapes (ordre de réalisation)

| Phase / lot | Étapes | Critère de sortie |
|---|---|---|
| P0 — base et audit | Comparer les refs ; figer la base ; créer la branche ; inventorier risques et contrôles | Plan versionné, aucune branche existante modifiée |
| P1 — contexte et configuration | Réglages validés par conversation ; notes et prompts ; sélection documentaire explicite ; prévisualisation ; migration additive | Budget respecté, historique intact, migration reproductible |
| P2 — génération progressive | NDJSON Ollama et API ; annulation propagée ; limite de réponse ; confirmation de persistance ; statistiques | Flux incomplet jamais présenté comme enregistré ; rejeu sans doublon |
| P3 — historique portable | Recherche ; export JSON/Markdown ; import validé et borné ; duplication de conversation ; régénération dans une copie | Conservation de l'original et aller-retour contrôlé |
| P4 — client de développement | Réglages et profils ; aperçu contexte ; flux visible ; copie du texte/code ; brouillons ; exclusions d'import | Fonctionnalités utilisables depuis Avalonia, erreurs explicites |
| P5 — livraison et exploitation | Tests ciblés puis suite ; lint/build ; scripts de contrôle et sauvegarde ; guide Linux/fish/Windows ; compte rendu | Résultats et limites tracés, branche récupérable |

Les lots sont réalisés successivement sur cette seule branche. Le périmètre
livré est un premier client local mono-utilisateur permettant de discuter,
conserver/reprendre les conversations, fournir du code et documents au modèle,
maîtriser les instructions et le contexte, récupérer ses données et développer
avec son assistance. Aucun code produit par le modèle n'est exécuté automatiquement.

## Après cette V1 : axes pour le client avancé

Ces travaux nécessitent leurs propres critères et ne sont pas déclarés livrés :

1. Tokenizer propre à chaque modèle, détection des capacités/contextes via
   `/api/show`, résumé de conversation explicite avec sources et validation
   humaine ; comparer rappel/coût à la fenêtre déterministe de cette V1.
2. Index de projet incrémental et RAG local : exclusion des secrets, découpage
   syntaxique, embeddings, citations, provenance, fraîcheur et jeux de régression.
3. Outils d'édition de code : lecture contrôlée d'un workspace, diffs à approuver,
   worktrees, exécution isolée avec permissions et quotas, aucun accès shell libre.
4. Branches de conversation en arbre, variantes de réponse, édition d'un ancien
   message, journal des prompts/réglages effectifs par génération.
5. Profils multi-backends et capacités multimodales explicites ; aucun basculement
   cloud implicite ; comparaison de qualité/coût/latence avec cas reproductibles.
6. Authentification et autorisation réelles avant tout accès LAN, chiffrement
   applicatif optionnel, sauvegardes chiffrées, rétention et effacement contrôlés.
7. Optimisations mesurées : chargement différé/virtualisation des longues
   conversations, index/recherche plein texte, cache des capacités, files de
   générations, allocation VRAM et benchmarks sur la RTX 4070 Laptop 8 Go.
8. Distribution signée Linux/Windows, tests visuels Wayland et accessibilité,
   restauration automatisée sur base isolée, surveillance des dépendances.

## Sources techniques consultées

- https://docs.ollama.com/api/chat : messages, streaming, options et statistiques.
- https://docs.ollama.com/api/tags : inventaire des modèles.
- https://dotnet.microsoft.com/en-us/download/dotnet/10.0 : environnement .NET.
- Code et tests des cinq branches observées du dépôt alescis-wuin/alycia.

Le compte rendu final distingue contrôles exécutés, contrôles bloqués et recette
encore nécessaire sur la machine de l'utilisateur. Aucune fusion automatique.
