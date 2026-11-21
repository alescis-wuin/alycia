# Alycia V3 — compte rendu du 17 novembre 2026

Les quatre lots ont été reconstruits depuis `33ba908` sur
`feature/local-client-v1-20261116`, après perte de l’environnement précédent.
Cette reconstruction possède de nouveaux identifiants de commit. Ses résultats
ci-dessous proviennent des contrôles exécutés de nouveau, pas de la validation
de la version perdue. Le [guide V3](Local-Client-V3-Guide.md) donne les commandes
de mise à jour, l’usage et la recette sur le poste.

## Lots livrés

| Lot | Résultat | Commit |
|---|---|---|
| A — contexte | Extraits répartis, classement du code selon la demande, imports de projets persistants | [`ac7be47`](https://github.com/alescis-wuin/alycia/commit/ac7be479e2c5690f37e68975587cae7efb80ca78) |
| B — outils | Lecture des originaux, inventaire/recherche/lecture paginés, appels natifs Ollama bornés, mémoire des outils et traces persistantes | [`e4d15cb`](https://github.com/alescis-wuin/alycia/commit/e4d15cbc675bd43b1a921f513fe93ebf241c81f2) |
| C — MCP | Serveur par conversation et client HTTP réellement utilisé par l’agent, négociation, catalogue, erreurs et contrôles d’accès | [`66a1a1f`](https://github.com/alescis-wuin/alycia/commit/66a1a1f69392496cae8b86099492b1b67f15d773) |
| D — analyse et livraison | Parcours des textes entiers, couverture mesurée, interface, réglages, tests et documentation | Commit portant ce compte rendu |

## Problème corrigé

Le contexte limité d’un échange ne peut pas contenir un projet complet. La
sélection précédente pouvait accorder trop de place aux premiers documents et
ne disposait que du texte extrait, plafonné à 200 000 octets par fichier. Les
imports attachés aux anciens échanges pouvaient aussi sortir de la fenêtre.

Les extraits sont maintenant répartis, les imports de dossiers/archives restent
disponibles et le modèle peut demander les portions utiles des originaux. Pour un
parcours systématique, l’analyse de projet lit chaque texte admissible complet,
avec réduction récursive et manifeste de couverture. Ces trois mécanismes restent
distincts et inspectables dans l’interface.

Les tests incluent un fichier de code dont la cible se trouve au-delà de
200 000 octets : recherche, lecture et résumé systématique retrouvent cette cible.
Les contenus exclus ne sont pas envoyés au modèle. Une analyse active ne retire
pas l’accès des outils aux originaux.

## Architecture et stockage

- Les trois outils sont limités aux pièces jointes de la conversation et ne
  peuvent ni lancer de processus ni lire un chemin arbitraire du poste.
- L’agent utilise les appels natifs Ollama, puis le client MCP local. Le serveur
  applique les règles de lecture, exclusions, pagination et budgets.
- L’historique protégé reste intact ; les anciens résultats d’outils peuvent être
  résumés. Limites, annulation et réponse incomplète n’aboutissent pas à un échange
  enregistré comme terminé.
- La migration `20261117071032_ProjectToolsAndPersistentSources` ajoute
  `ToolTraceJson` et convertit les anciens imports de projet encore en mode
  `window`. Le schéma Chat comprend désormais six migrations.
- Le manifeste d’analyse passe à la version 2 : couverture globale et provenance
  par fichier, avec tailles réellement lues. Les anciens rapports restent lisibles
  et doivent être recalculés pour bénéficier de ces informations.
- Aucune nouvelle dépendance applicative n’est ajoutée pour le protocole MCP.

## Validation exécutée

Environnement : Linux x64, SDK .NET **10.0.404**, runtime **10.0.15** et PostgreSQL
**16.2** isolé. La configuration Compose du projet cible PostgreSQL 17.

| Contrôle | Résultat |
|---|---|
| Compilation de la solution finale | Réussie, 0 avertissement, 0 erreur |
| Contexte, streaming, contrats Ollama, archives et appels natifs | 50 tests réussis |
| Stockage, migration, analyse, isolation et client MCP | 28 tests réussis avec PostgreSQL actif |
| Client Avalonia, dont dialogue des outils et traces | 43 tests réussis |
| Compatibilité de la fenêtre, contrôle d’accès et inventaire | 20 tests réussis |
| Total des sélections .NET disjointes | **141 réussis, 0 échec, 0 ignoré** |
| Recette HTTP avec vraie API et PostgreSQL, Ollama simulé | 13 groupes réussis |
| EF | Aucun changement de modèle sans migration |
| Qualité | Format/lint des fichiers modifiés et compilation Python réussis |
| Rendu graphique | Captures Avalonia/Skia inspectées à 1 240 × 880 et 960 × 700, Markdown et émojis visibles |

Les résultats détaillés sont dans
[`validation/Local-Client-V3-Checks.json`](validation/Local-Client-V3-Checks.json).
Les tests PostgreSQL cités ont été exécutés, sans être ignorés. Le diagnostic
non bloquant du lanceur concernant `VSTEST_ARTIFACTS_DIRECTORY` reste présent.
Les contrôles déjà réussis n’ont pas été répétés après les seuls ajustements de
formatage, documentation et texte d’aide ; la solution finale a été recompilée.

La recette HTTP a exécuté **3 générations conversationnelles ordinaires**, un
parcours d’outils en **4 tours**, et **53 appels de résumé bornés**, avec un modèle
simulé. Elle confirme notamment la négociation MCP, le contrôle d’origine, la
pagination, la lecture au-delà de l’extrait, les traces enregistrées/exportées,
le rejeu sans nouvelle génération et la révocation. Ses données ont été nettoyées.
Ces nombres ne mesurent pas les performances d’un vrai modèle.

## Limites et essais restants

1. **Vrai Ollama.** Aucun modèle/GPU n’a été lancé ici. La recette
   `--extended --tools` du guide reste à exécuter avec le modèle local choisi.
   Annoncer la capacité `tools` ne garantit pas des appels corrects.
2. **Couverture et qualité.** L’exploration par outils est sélective. La couverture
   systématique mesure les entrées lues, pas leur compréhension. Résumés et mémoire
   sont avec perte ; ils doivent être confrontés aux originaux.
3. **Imports.** Les limites de taille/nombre/profondeur sont conservées. Les formats
   documentaires utilisent l’extraction existante, parfois partielle ; pas d’OCR.
   Les fichiers filtrés avant import ne font pas partie de la couverture.
4. **Budget et temps.** Estimation UTF-8, sans tokenizer exact. 8 192 tokens est un
   point de départ pour les outils, pas une garantie pour tous les prompts. La
   recherche littérale lit les originaux et ne possède pas encore d’index ; une
   grosse analyse peut être longue. Les statistiques UI concernent le dernier appel.
5. **Interopérabilité.** Profil MCP HTTP local et sans état testé avec le client
   Alycia et la recette HTTP. Aucune certification ni recette avec un client MCP
   tiers ; aucun gestionnaire de serveurs externes.
6. **Plateformes.** Docker, PostgreSQL 17, bureau KDE natif et Windows n’ont pas été
   exécutés dans cet environnement. L’interface a été testée sur Avalonia headless.

## Suites proposées

- Indexer les symboles et leurs relations pour améliorer les recherches au-delà
  des correspondances textuelles.
- Mettre à jour les analyses par checksum, en ne retraitant que les sources
  modifiées, puis mesurer la fidélité des synthèses sur des projets de référence.
- Ajouter une vue de couverture reliant chaque constat aux pages réellement lues
  et distinguant explicitement les zones encore inexplorées.
- Préparer ensuite des propositions de correctifs avec diff et tests isolés ;
  soumettre l’écriture/exécution à une autorisation explicite et des permissions
  distinctes de la simple lecture.

Références d’implémentation :
[appels d’outils Ollama](https://docs.ollama.com/capabilities/tool-calling),
[transport MCP](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports)
et [outils MCP](https://modelcontextprotocol.io/specification/2025-11-25/server/tools).
