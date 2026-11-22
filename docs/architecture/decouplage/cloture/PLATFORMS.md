# Recette de plateforme

| Cible | Résultat et preuve |
|---|---|
| Linux x86-64 et .NET 10 | Restauration, builds Debug/Release et publications de l’API et de Boréale réussis. Voir `recette/environnement.json` et les journaux de build et de publication. |
| PostgreSQL 17.12 et pgvector 0.8.0 | Bases jetables créées pour les tests, migrations appliquées, catalogue historique identique, interrogations réelles de vecteurs et séquencement des commits observé. Voir `recette/schema.tsv`, `recette/versions.txt` et `TRANSACTIONS.md`. |
| Ollama CPU | Qwen 2.5 0.5B a généré un tour NDJSON avec rejeu. All-MiniLM 22M a encodé trois entrées en dimension 384. Le doublon donne le même vecteur, l’entrée différente donne un autre vecteur, toutes les valeurs sont finies. Version, digests et résidence sont dans `recette/http-ollama-version.json`, `recette/http-embeddings.json` et `recette/http-resident.json`. |
| ONNX Runtime CPU | Chargement des poids quantifiés et du tokenizer, identité, positions, classement, annulation et repli vérifiés. Voir `recette/onnx-sha256.tsv` et les TRX des classes RerankerContractTests et NativeRerankerTests. |
| Docker / Compose | `docker compose config --quiet` a réussi. La connexion au daemon de l’hôte a échoué. L’essai d’un daemon local indépendant a échoué dans containerd sur la création de son socket Unix. L’image, le démarrage des services, leur santé et l’arrêt ne sont pas attestés. Voir `recette/docker-platform.log` et `recette/docker-daemon.log`. |
| Ollama GPU et accélération ONNX | NON_TESTÉ. Aucun périphérique de calcul GPU exposé au processus. Résidence, annulation sous charge GPU et latences d’un backend GPU restent à vérifier sur une machine équipée. |
| KDE/Wayland | NON_TESTÉ en session graphique native. L’assembly Boréale a été publiée et 63 tests client ont réussi. L’ouverture d’une vraie fenêtre, le focus clavier, l’échelle d’affichage et le sélecteur de fichier n’ont pas été observés sous KDE. |
| Windows / PowerShell | NON_TESTÉ sur Windows natif. La session de recette est Linux. Restaurer la solution sous Windows, puis exécuter `./scripts/check.ps1 -Integration` avec PostgreSQL de test, pgvector et les poids ONNX. Le lancement et la sélection de fichier par Boréale doivent également être exercés sur cette cible. |

Les suites HTTP `all`, `originals` et `mock` utilisent PostgreSQL de test et des fournisseurs contrôlés. Le cas `ollama` utilise le processus Ollama réel. Les deux catégories sont identifiées dans leurs fichiers JSON. Les mesures de performance donnent les paramètres de comparaison et les observations individuelles.
