# D16 — Doubles et composition de développement

**PARTIEL.** Les doubles de texte scripté, de vecteurs déterministes et d’index à portée limitée forment une assembly dépendant uniquement des contrats. Le même scénario de conformité s’exécute sur le double texte et le véritable adaptateur Ollama alimenté par un transport de test. Cinq cas xUnit passent, dont l’interruption, les refus, les partitions et l’ordre des deltas.

La sonde scripts/test-mock-api.py démarre l’API sans PostgreSQL ni Ollama, exerce le catalogue et l’échantillon, vérifie le refus de l’Origin et du Host étrangers, puis démontre le refus au démarrage en Production. Dans un chat complet de développement, le même opt-in remplace seulement les deux transports de modèle. La matrice D19 identifie les ports qui n’ont pas encore de double autonome ou de suite commune.

À D19 les huit cas de la suite indépendante Inference.Tests incluent une conformité texte et une conformité vecteurs exécutées chacune sur le double et l’adaptateur Ollama. La suite se construit dans un répertoire contenant uniquement Contracts, Modules.Inference et Modules.Testing.
