# D11 — Persistance vectorielle et découverte

**PARTIEL.** Le module Indexing possède désormais les jobs et les vecteurs PostgreSQL, la connexion de travail et les opérations de verrouillage. Le worker ne transmet aucun DbContext au magasin. La découverte lit les conversations et les projets actifs par pages de 128 via deux ports propriétaires. Les réglages d’une conversation sont reconstruits depuis la projection persistée. Les deux exceptions EF de la découverte ont été retirées. La lecture d’un original partagé a également été ramenée au lecteur Documents.

Les tests PersistentEmbedding, KnowledgePipeline, TemporalMemoryIntegration et ProjectFiles ont été exécutés avec PostgreSQL et pgvector. La garde du compilateur passe sur 17 projets. Le traitement des lots et la persistance des connaissances utilisent encore les projections EF historiques pour les pièces et messages. Le prédicat d’autorisation reste appliqué au traitement et à la recherche.
