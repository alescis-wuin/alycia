# D14 — Changement mémoire du tour

**PARTIEL.** La préparation produit un PreparedMemoryChange uniquement quand la mémoire doit évoluer. Le port d’écriture reçoit le résumé et sa provenance, ou le ledger, sans recevoir la fenêtre de contexte. Le tour applique le changement dans sa transaction existante. La lecture des connaissances historiques par le magasin temporel passe par IHistoryKnowledge, ce qui retire l’exception d’accès au KnowledgeStore concret.

Les 11 tests ciblés StructuredMemory, TemporalMemory, PostgresChat et ConversationContextPreparation passent avec PostgreSQL réel. Le stockage temporel partage encore le contexte EF pour lire conversations et messages. Les champs du ledger restent dans leur table historique.
