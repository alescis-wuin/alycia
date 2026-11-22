# D07 — Identité canonique des projets

**Partiel.** Workspaces possède désormais la liste, le contrôle d’existence et la création de l’identité. Chat consomme IWorkspaceIdentityDirectory. Il n’importe plus le domaine Workspaces et n’interroge plus workspaces.workspaces. La composition enrôle l’adaptateur Workspaces et l’écriture du profil dans la même session. Le contrat public HTTP et les deux modèles EF sont conservés.

Les 3 tests PostgreSQL de ProjectCreationTests et ProjectFilesTests passent, y compris deux créateurs en compétition et le rollback de l’identité à l’échec du profil. La garde du produit passe avec 14 projets. Elle refuse désormais le retour d’une référence Chat.Infrastructure → Workspaces.Domain/Infrastructure et examine les affectations constantes à DbCommand.CommandText. La propriété physique des profils et la séparation complète des notes et de l’appartenance restent à réaliser.
