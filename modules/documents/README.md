# Alycia · Documents

Import, extraction structurée, originaux et contrôle de visibilité des pièces jointes.

Installer le SDK .NET 10.0.404 et Python 3. Fournir le feed local produit par le maître. Depuis une copie de ce répertoire, utiliser la commande suivante.

```sh
python3 scripts/check.py check --feed /chemin/vers/local-feed
```

`build` restaure en mode verrouillé et compile en Release. `test` et `check` exécutent également la suite propriétaire et exigent le succès de chaque cas. Sous Windows, le point d’entrée Python accepte `py -3`.

Les responsabilités, les projets et la persistance sont décrits dans [MODULE.md](MODULE.md).
