# Compte rendu de la refonte Boréale

Lot du 18 novembre 2026 sur `feature/local-client-v4-context-quality-20261117`. Révision de départ `0d4c1d9d63f40a66af0645569e4c224bb311e2ec`. Le code et les recettes sont livrés. La compilation .NET, le démarrage du client et la recette PostgreSQL restent à confirmer sur une machine équipée.

## Du projet initial à la nouvelle interface

La branche utilisait déjà Avalonia, Markdig, le streaming avec brouillons, l’analyse récursive, les outils de lecture et les citations. Trois limites affectaient directement les usages prioritaires. Le fil partageait son espace avec de nombreuses commandes techniques, les pièces restaient attachées à une seule conversation, et le lecteur Markdown utilisait des couleurs fixes sans composition mathématique ni coloration du code.

| Phase | Étapes réalisées | Résultat dans le produit |
| --- | --- | --- |
| 1. Identité et composants | Intégrer les deux familles et leurs notices, créer les ressources sémantiques, définir les trois choix de thème et préparer le composant TeX. | Aube et Nuit boréales, Luciole pour la lecture, Hack pour le code, marque SVG et commandes vectorielles nommées. |
| 2. Projets et persistance | Réutiliser l’identité Workspaces, ajouter l’appartenance facultative, séparer les propriétaires des fichiers, versionner les consignes et notes, filtrer la recherche, les outils et la construction du contexte. | Sources communes, pièces privées, exceptions locales et contrôle de révision. Une génération de projet utilise une transaction PostgreSQL à lecture répétable et enregistre la révision de départ. |
| 3. Conversation et navigation | Déplacer les commandes occasionnelles dans le menu, filtrer l’historique par projet, simplifier la saisie, conserver l’arrêt et la reprise du brouillon, adapter les panneaux à la largeur. | Fil central, sélecteur de projet, volet à la demande et raccourci de concentration. Le modèle actif et le modèle par défaut ont chacun leur emplacement. |
| 4. Documents, contexte et mémoire | Relier l’import de projet aux originaux stockés, ajouter filtre, sélection multiple, aperçu, partage et retrait, raccorder les consignes et les notes à l’API, ouvrir le passage cité à sa version, regrouper les options avancées. | La portée apparaît dans Documents. Le contexte du brouillon, les sources fournies à une réponse et la mémoire privée se consultent séparément. La promotion d’une citation de mémoire est explicite. |
| 5. Lecture des réponses | Étendre Markdig aux notes et aux formules, raccorder les citations aux extraits, colorer le code, prendre en compte les thèmes, la taille de lecture et le repli des lignes, conserver la position de lecture. | Tables défilables, code sélectionnable, copie du bloc ou du message, TeX en ligne ou en bloc, repli en texte source. |
| 6. Recette et remise | Adapter le test de la fenêtre, ajouter des scénarios d’isolation, de migration et de version, une capture sur les deux thèmes, un relevé facultatif de charge et les instructions CachyOS. | Contrôles hors ligne exécutés, recette native fournie, répétition de migration sur une copie et guide du poste. |

Le [plan par étapes](plan.md) et le [guide d’exécution et d’utilisation](guide-cachyos.md) accompagnent le lot.

## Données et choix techniques

- Les anciens fils gardent leur identifiant, leurs messages, leur mémoire et leurs pièces privées. La migration les laisse dans **Hors projet**. Le mode documentaire historique `project` reste privé jusqu’au partage volontaire. La structure de migration et la recette de comparaison sont disponibles dans le dépôt.
- Les profils de chat réemploient les identifiants du registre Workspaces. Les documents communs ont un propriétaire de projet. Les octets d’un original privé promu sont copiés pour conserver les deux cycles de vie. À chemin et empreinte identiques, l’index et la préparation du contexte privilégient une seule version éligible.
- Les notes de projet portent une révision de profil. Une note promue garde la conversation et le numéro du message d’origine. La disponibilité d’un document, les extraits effectivement fournis et les références écrites par le modèle sont trois informations distinctes.
- Retirer un document commun le rend indisponible aux prochains appels. Ses anciennes références restent dans les réponses. Les originaux retirés restent sur disque pour les lectures en cours et les sauvegardes. L’export JSON d’un fil inventorie les dépendances communes. Le transfert intégral des documents et des mémoires passe par la sauvegarde.
- Le rendu reste constitué de contrôles Avalonia. Une seule nouvelle dépendance directe du client, `CSharpMath.SkiaSharp 1.0.0-pre.2`, sert à composer le TeX. Ce choix de préversion demande une vérification native de la restauration NuGet et du rendu Skia sur le poste. [Fiche du paquet](https://www.nuget.org/packages/CSharpMath.SkiaSharp/1.0.0-pre.2)
- Le code propose une coloration lexicale bornée. Les langages inconnus restent en texte brut. Le moteur TeX limite la taille et l’imbrication des expressions. La sélection à la souris se fait par contrôle Avalonia. La copie du message permet de reprendre plusieurs blocs ensemble.

## Vérifications exécutées

| Contrôle | Résultat observé |
| --- | --- |
| Tests Python de sauvegarde et d’évaluation | 10 réussis. |
| Nouveaux tests de ressources Boréale | 3 réussis. Polices embarquées, noms accessibles, références de ressources et 54 couples de contraste contrôlés. |
| Palette sur les fonds de lecture | Contraste minimal mesuré de 4,85:1 en Aube et de 6,23:1 en Nuit pour les couleurs de texte testées, valeurs arrondies vers le bas. Contours testés à au moins 3,67:1 et 4,20:1 respectivement. Il s’agit des couleurs déclarées dans les ressources. |
| Structure du client | 3 AXAML et 12 fichiers de projet analysés comme XML. 84 contrôles nommés uniques dans la fenêtre, 82 références directes relevées en C# et 6 gestionnaires déclarés dans son AXAML résolus par nom. Les noms de famille internes des huit TTF ont été lus. |
| Corpus existant | 8 fichiers et 36 cas validés. SHA-256 du manifeste et des cas `3789bbeec4dda426dd5671f98ad6c511e60369238d8cdb5933890e67be1427d4`. |
| Scripts | Syntaxe Python des scripts analysée, syntaxe Bash des commandes de contrôle, lancement et migration vérifiée. |
| Compilation et tests .NET | `bash scripts/check.sh` s’est arrêté à la première restauration avec `dotnet: command not found`. Les tests C#, les captures et les mesures de performance n’ont donc pas été exécutés dans cet environnement. |
| PostgreSQL, Ollama et bureau | Aucune base utilisateur migrée. La recette HTTP, la répétition sur sauvegarde, l’inférence réelle et le suivi du thème sous KDE Wayland restent à exécuter. |

## Ce que vérifie la recette fournie

Le scénario HTTP Boréale couvre deux conversations du même projet, un autre projet et un fil indépendant. Il teste la recherche, l’accès par identifiant, la lecture par outil, l’exclusion locale d’une source partagée épinglée et la conservation des réponses après retrait. Un modèle simulé suspend une génération pendant une modification des consignes pour comparer la révision inscrite au diagnostic avec le contexte de l’appel suivant. Une promotion de note vérifie sa provenance et sa portée.

Le test Avalonia parcourt le fil, les réglages avancés, les détails et le passage source, la conservation du brouillon et la suppression de la conversation. Le corpus du lecteur couvre entre autres les accents, les emoji, les notes, un montant en dollars, une formule incomplète, une fraction, le repli TeX et la restitution des caractères après coloration. Les captures et mesures facultatives se déclenchent depuis le guide. Les seuils de réactivité et le confort à l’échelle réelle restent à établir sur le MSI.
