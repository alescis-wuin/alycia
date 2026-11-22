# D17 — Composition et présentation

**PARTIEL.** Le conteneur valide les dépendances et les durées de vie au démarrage. Les portées d’une application partagent un verrou par conversation, un signal de priorité et un permis d’inférence physique distincts. Les domaines les reçoivent par injection. Le choix du modèle du projet à la création d’une conversation est sorti de l’endpoint. Boréale continue à ne référencer que Contracts.

30 tests ciblés passent, couvrant entre autres l’exclusion concurrente, la priorité, la diffusion et les transactions. La recette HTTP transport a démarré l’hôte réel et PostgreSQL. La composition de développement simulée est éprouvée en D16. L’ouverture native de Boréale a été tentée sur Xorg avec le pilote dummy. Le système refuse la création des sockets Unix avant l’ouverture de l’écran. Les tests de présentation headless et le résultat de la recette HTTP complète figurent en D19.
