# Commandes de recette exécutées

Les contrôles utilisent un serveur PostgreSQL jetable avec pgvector, les deux fichiers Horizon vérifiés et un cache NuGet dédié. Les répertoires `$RUN/trx` et `$RUN/capture` correspondent aux archives de même nom dans `preuves/`. Le SHA-256 de chaque sortie publiée est dans `preuves/SHA256SUMS`. Les durées des journaux varient avec la plateforme.

```bash
git ls-remote origin refs/heads/develop
git rev-parse origin/develop 'origin/develop^{tree}'
python3 scripts/modules.py bootstrap-packages
python3 scripts/modules.py check-extraction
dotnet tool restore
dotnet restore Alycia.slnx --locked-mode --configfile NuGet.Config -m:1
dotnet build Alycia.slnx --no-restore -c Debug -m:1 -v:q
dotnet build Alycia.slnx --no-restore -c Release -m:1 -v:q
dotnet publish src/Alycia.Api -c Release --no-build --no-restore -m:1 -o artifacts/acceptance/Alycia.Api
dotnet publish src/Alycia.Client.Avalonia -c Release --no-build --no-restore -m:1 -o artifacts/acceptance/Alycia.Client.Avalonia
python3 scripts/test-mock-api.py --published artifacts/acceptance/Alycia.Api
python3 -m unittest discover -s scripts/tests -p '*_tests.py'
python3 scripts/architecture/check-schema.py artifacts/acceptance
python3 scripts/check-modules.py --all-projects --capture "$RUN/capture"
dotnet test Alycia.slnx --no-build --no-restore -m:1 --logger trx --results-directory "$RUN/trx"
python3 scripts/architecture/finition-audit.py "$RUN/capture" "$RUN/trx" artifacts/acceptance --require-complete --schema artifacts/acceptance/schema.tsv
python3 scripts/tests/http-integration.py --case all > artifacts/acceptance/http-all.json
python3 scripts/tests/http-integration.py --case originals > artifacts/acceptance/http-originals.json
python3 scripts/tests/http-integration.py --case mock > artifacts/acceptance/http-mock.json
python3 scripts/architecture/capture-openapi.py artifacts/acceptance/openapi.raw
python3 scripts/evaluate-context.py offline --pipeline-v2 --output artifacts/acceptance/rag.json
python3 scripts/architecture/verify-baseline.py --http artifacts/acceptance/http-all.json --rag artifacts/acceptance/rag.json --openapi artifacts/acceptance/openapi.raw --trx "$RUN/trx"
bash scripts/quality.sh lint
python3 scripts/architecture/check-docker.py
```

L’entrée `python3 scripts/modules.py check-master` orchestre cette séquence. Elle a atteint le scénario HTTP `originals` avant le redémarrage de l’hôte. Les étapes de transport, OpenAPI, RAG et de comparaison restantes ont été exécutées directement sur le même arbre. La recette Docker a son propre journal dans la VM jetable. Le verdict porte sur les résultats de chaque étape. Les archives TRX et Roslyn sont créées dans un répertoire neuf à chaque exécution. La recette dispose d’un catalogue de référence signé et de 400 identités VSTest contrôlées une à une. L’outil de comparaison vérifie aussi le nom d’assembly attendu de chaque classe déplacée.
