# D15 — Portée des outils

**PARTIEL.** L’exécution des outils est passée de l’infrastructure à la couche application. Le catalogue, les budgets, le cadrage MCP et les identifiants de preuve sont conservés. L’inventaire et chaque lecture utilisent le port Documents, qui vérifie la portée courante puis lit l’original. La politique de conversation est relue sans suivi EF. Les outils reçoivent les ports de lecture de l’historique et de l’index. Ils ne construisent plus leur dépôt, leur lecteur de fichiers ou leur index de substitution.

19 tests de snapshots, historique, stockage et outils passent avec PostgreSQL. La garde du compilateur passe sur 19 projets. Les doubles de haut niveau et le projet physique autonome de Tools restent à compléter. Le transport HTTP et le protocole MCP sont repris en D19.
