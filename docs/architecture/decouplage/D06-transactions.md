# D06 — Session relationnelle et création de projet

Le coordinateur scoped RelationalSession porte la transaction de création de l’identité et du profil. Il fournit aux adaptateurs une opération à connexion enrôlée et un propriétaire unique du commit. Les couches métier ne reçoivent aucun DbContext ou DbTransaction. L’ouverture EF est équilibrée par une fermeture en finally ; la transaction couvre l’opération jusqu’au COMMIT.

Sur PostgreSQL 17.12 avec pgvector, les 11 tests sélectionnés par `FullyQualifiedName~ProjectCreationTests|FullyQualifiedName~Fork|FullyQualifiedName~Snapshot` ont réussi sans ignoré. Les contraintes et l’historique de migrations restent ceux de D00. La compensation disque après un commit incertain conserve son protocole spécialisé. **Partiel** pour l’extension à tous les participants du tour et à l’import ; aucun essai d’arrêt brutal du processus dans cette phase.
