# D04 — Configuration à la frontière d’inférence

**Partiel.** La configuration liée au démarrage produit une copie indépendante à chaque résolution. Les appels Ollama ordinaires et streaming prennent un instantané des paramètres de génération avant leur premier await. Le schéma JSON est cloné. La préparation de la tokenisation copie les réglages avant d’interroger le fournisseur.

Le test InFlightCallKeepsItsOriginalSettings a échoué dans les deux modes avant modification (graine reçue 99, attendue 42). Après correction, 42 tests Ollama, index et configuration ont réussi. Filtre exécuté sur Alycia.Modules.Chat.Tests : `FullyQualifiedName~OllamaChatModelTests|FullyQualifiedName~ConversationSettings|FullyQualifiedName~EmbeddingQuerySettings`. Le jeu v1 est celui de D00. Les objets de contexte, de recherche et de routage partagent encore ChatOptions à l’intérieur d’une opération. La séparation de ces consommateurs reste ouverte.
