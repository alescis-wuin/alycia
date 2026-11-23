# Session relationnelle

Un appel à `AtomicAsync` via la façade EF démarre la transaction, exécute l’opération et valide après son succès. Lorsqu’un appelant fournit déjà une transaction, `WithConnectionAsync` réutilise cette connexion et cette transaction. Il garde la responsabilité du commit. Une transaction terminée est refusée. La suite propriétaire exerce ce refus et le maître vérifie le rollback et la coordination des adaptateurs réels sur PostgreSQL.
