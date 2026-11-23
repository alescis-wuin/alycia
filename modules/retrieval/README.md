# Alycia · Recherche

Sélection et classement des passages, BM25, audit des réponses et reclassement ONNX.

Installer le SDK .NET 10.0.404 et Python 3. Fournir le feed local produit par le maître. Depuis une copie de ce répertoire, utiliser la commande suivante. Définir `ALYCIA_RERANK_DIRECTORY` sur le modèle Horizon contenant `tokenizer.json` et `onnx/model_quantized.onnx`. La recette contrôle les deux poids par SHA-256.

```sh
python3 scripts/check.py check --feed /chemin/vers/local-feed
```

`build` restaure en mode verrouillé et compile en Release. `test` et `check` exécutent également la suite propriétaire et exigent le succès de chaque cas. Sous Windows, le point d’entrée Python accepte `py -3`.

Les responsabilités, les projets et la persistance sont décrits dans [MODULE.md](MODULE.md).
