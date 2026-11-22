# Vérifier les vingt commits de phase

Chaque entrée du manifeste donne l’objet de commit natif signé, son identifiant, son arbre et le commit GitHub publiant le même arbre. Les branches GitHub de revue et de destination s’appellent respectivement alycia-decouplage-modulaire-20261121 et develop.

La clé publique de vérification est dans [allowed_signers](../allowed_signers). Son empreinte SSH est `SHA256:xrQ+Du5Y/hqC1WxEhfZYqrKYe1DUdyxG9H+Ji8m40c0`. Elle identifie la clé utilisée pour cette exécution. GitHub rapporte séparément le statut de vérification de ses propres objets de commit.

Depuis la racine d’un clone avec l’historique de la branche de revue, exécuter les commandes suivantes. La vérification écrit uniquement les objets de commit locaux manquants. Elle vérifie les vingt signatures et l’ordre des parents depuis le SHA initial de develop, puis compare chaque arbre à l’arbre publié.

```bash
git fetch origin
git switch alycia-decouplage-modulaire-20261121
git pull --ff-only
python3 scripts/architecture/verify-signed-phases.py
```

Le commit de livraison ajoute ce manifeste. Il suit la dernière phase D19. Pour examiner un commit natif sans modifier le checkout courant, utiliser `git worktree add --detach ../alycia-phase <SHA-natif>`.

La publication API Release issue de D19 a également été lancée en Production sur loopback. La sonde /healthz a renvoyé 200 avec Healthy et la route de démonstration /api/chat/models a renvoyé 404.
