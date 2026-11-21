# D18 — Barrières physiques

**PARTIEL.** Douze assemblies se compilent en Release dans douze répertoires temporaires indépendants. Chaque répertoire reçoit le projet et uniquement la fermeture de ses références produit autorisées. La garde vérifie ces références dans les projets et dans les entrées réellement évaluées par MSBuild. Le test négatif ajoute Indexing à Retrieval et vérifie le refus. Les obligations de la garde et des deux modèles EF sont également câblées dans check.ps1.

Les commandes effectivement exécutées sont python3 scripts/build-isolated-modules.py, python3 scripts/check-modules.py --matrix et la fixture independent_module. Les projets Documents, Inference, Indexing, Retrieval et son adaptateur ONNX ont leurs propres fichiers de compilation et leurs entrées Docker. Les bibliothèques Chat.Application et Chat.Infrastructure réunissent toujours plusieurs responsabilités. La construction d’une image avec Docker et l’exécution sous Windows ne sont pas vérifiées.
