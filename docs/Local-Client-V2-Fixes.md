# Alycia V2 — correctifs modèles, imports et réponses

Branche : `feature/local-client-v1-20261116`.
Base : `f2155a39fafd5ace3d9784f9c3e952f9f47141cb`.
Ces trois lots sont implémentés sans nouvelle migration ni dépendance ajoutée.

## Lot 1 — utiliser un modèle capable de répondre

1. Comparer le modèle de la conversation à celui de l’essai en terminal.
2. Vérifier les capacités Ollama via `/api/show`, à la sélection et avant génération.
3. Afficher le modèle actif et permettre sa modification dans la conversation.
4. Tester le refus d’un modèle d’embeddings et la reprise de l’ancienne conversation.

La capture indiquait `embeddinggemma:latest` pour la conversation alors que la
commande réussie utilisait `qwen3:4b-instruct`. Le fonctionnement confirmé d’une
nouvelle conversation avec modèle génératif concorde avec ce diagnostic. Le journal
contenait HTTP 400 mais pas le corps JSON du refus : ce dernier n’est pas reconstitué.

Les listes n’offrent désormais que les modèles locaux annonçant `completion`.
La vérification porte sur les capacités, y compris pour les alias, et conserve
l’exclusion des modèles distants. Le bouton **Modèle actif** ouvre les réglages
de la conversation ; le sélecteur latéral reste dédié aux nouvelles conversations.
Un échec conserve le brouillon, indique le modèle concerné et n’affiche plus
simultanément l’accueil d’une conversation vide.

## Lot 2 — importer les chemins relatifs usuels

1. Vérifier la transmission multipart .NET vers le parseur ASP.NET.
2. Accepter les segments `.` et séparateurs répétés sans accepter les remontées `..`.
3. Normaliser les entrées ZIP et détecter leurs collisions avant enregistrement.
4. Tester import, téléchargement, inventaire et synthèse avec l’API et PostgreSQL.

Les noms ordinaires et accentués traversent correctement le multipart. Le validateur
rejetait en revanche les variantes `./dossier/fichier` et les séparateurs doublés.
Ces variantes sont maintenant normalisées. Les noms Unicode restent conservés ;
les répertoires ZIP avec séparateurs Windows sont reconnus. Chaque refus indique
désormais le chemin fautif et sa raison, au lieu d’une erreur générique.

Les originaux restent stockés sous identifiants GUID. Chemins absolus, URI, lecteurs,
remontées, liens symboliques, dépassements et collisions restent refusés. Les
collisions après normalisation n’enregistrent aucun fichier du lot.

**Limite du diagnostic :** aucune archive originale en échec n’a été fournie.
Le défaut des chemins relatifs est reproduit et corrigé, mais son rôle dans tous
les échecs rapportés n’est pas démontré. Le sélecteur de dossiers KDE/Wayland devra
être vérifié sur le poste avec le nouveau message détaillé en cas d’échec.

## Lot 3 — rendre les réponses et émojis lisibles

1. Limiter la largeur de lecture et mieux hiérarchiser titres, tableaux et actions.
2. Interpréter les codes d’émojis et entités Markdown pendant le flux.
3. Conserver les séquences Unicode composées et utiliser les polices d’émojis.
4. Vérifier le rendu Avalonia/Skia et les formats large et compact.

Les réponses bénéficient d’une typographie plus lisible, de tableaux alignés et
des actions de copie en tête de message. `:rocket:`, `&amp;` et les entités Unicode
sont interprétés hors code. Les drapeaux, couleurs de peau et séquences avec ZWJ
restent groupés pour leur rendu. Un code d’émoji reçu en plusieurs fragments est
réinterprété lorsqu’il devient complet. Le texte source et les blocs de code
restent inchangés. Aucune image distante ni HTML exécutable n’est introduit.

## Vérifications effectivement exécutées

| Contrôle ciblé | Résultat |
|---|---|
| Compilation de la solution, SDK .NET 10.0.404 | Réussite, 0 avertissement, 0 erreur |
| Fournisseur Ollama et flux | 25 tests réussis, aucun ignoré ; HTTP simulé |
| Fichiers, inventaire et analyses | 23 tests réussis, aucun ignoré ; PostgreSQL 16.2 isolé |
| Parcours graphique et assertions Markdown/émojis | 1 test réussi ; Avalonia/Skia headless |
| Recette HTTP complète | 9 groupes réussis ; API réelle, PostgreSQL isolé, Ollama simulé |
| Inspection visuelle | 1240 × 880 et 960 × 700 ; émojis couleur et contrôles lisibles |
| Lint et compilation des deux scripts Python modifiés | Réussite |

Ces **49 tests .NET ciblés** ne constituent pas une nouvelle exécution de toute
la suite historique V2. Les 17 cas de chemins et multipart sont inclus dans les
23 tests de fichiers, pas additionnés une seconde fois. La recette HTTP effectue
3 réponses conversationnelles et 46 appels de résumé simulés, puis nettoie ses
propres données. Les compteurs et empreintes des rapports sont conservés dans
[le relevé de validation](validation/Local-Client-V2-Fixes-Checks.json).

Restent à tester sur le poste : Ollama réel, qualité des réponses et des résumés,
sélecteurs et presse-papiers KDE/Wayland, archives du signalement et polices locales.
Windows, Docker et PostgreSQL 17 n’ont pas été exécutés pour ces correctifs.

## Mise à jour et essai

Fermer le client et arrêter l’ancien lancement de l’API, puis dans le clone existant :

```sh
git status --short
git pull --no-rebase --ff-only origin feature/local-client-v1-20261116
```

Conserver les modifications locales si Git refuse l’intégration. Ne pas utiliser
`reset --hard`. Relancer `make demo-api` et `make demo-client` dans deux terminaux.

1. Ouvrir **Modèle actif**, choisir un modèle génératif installé et enregistrer.
2. Importer un petit dossier, puis un ZIP ; consulter **Documents** et `tree.txt`.
3. Demander une réponse contenant titres, tableau, code et `:rocket: 👩🏽‍💻 🇫🇷 ❤️`.
4. Exécuter, avec l’installation démarrée :

```sh
python3 scripts/verify-local.py --model 'qwen3:4b-instruct' --extended
```

Remplacer le nom si nécessaire. Cette recette réalise de vraies générations,
attend huit groupes réussis et nettoie uniquement ses données synthétiques.
Le [guide d’utilisation](Local-Client-V2-Guide.md#8-import-de-dossier-ou-zip-refusé)
détaille les imports et l’installation facultative d’une police d’émojis.
