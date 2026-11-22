# D12 — Recherche de passages

**PARTIEL.** Le classement BM25, la fusion des rangs, la diversité, la déduplication et le cache lexical sont dans Alycia.Modules.Retrieval. Le projet dépend uniquement des contrats. L’adaptateur ONNX occupe son propre projet, avec son tokenizer et ses dépendances natives. Les deux consomment le contrat de passage. Les filtres avant recherche et le contrôle de portée restent dans les adaptateurs producteurs. L’orchestrateur de recherche utilise encore des projections et options Chat.

29 tests ciblés ont réussi. Le benchmark de référence a été reconstruit et exécuté avec le corpus pipeline-v2. Les 92 lignes ordonnées, hors quatre mesures de durée/allocation, donnent la même empreinte SHA-256 qu’en D00, 0811f60b9dd1b5f42b9249e9876172c3a6b716f311fcc403da21e0f5407a4990. Le chargement de poids ONNX réels reste à vérifier.
