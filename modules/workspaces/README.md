# Alycia · Workspaces

Identité, catalogue, dépôts et profils des espaces. Les lectures et modifications respectent le périmètre du propriétaire.

Installer le SDK .NET 10.0.404 et Python 3. Fournir le feed local produit par le maître. Depuis une copie de ce répertoire, utiliser la commande suivante. La suite PostgreSQL utilise `ALYCIA_CHAT_TEST_CONNECTION`, dirigée vers un serveur de recette disposant de CREATEDB.

```sh
python3 scripts/check.py check --feed /chemin/vers/local-feed
```

`build` restaure en mode verrouillé et compile en Release. `test` et `check` exécutent également la suite propriétaire et exigent le succès de chaque cas. Sous Windows, le point d’entrée Python accepte `py -3`.

Les responsabilités, les projets et la persistance sont décrits dans [MODULE.md](MODULE.md).
