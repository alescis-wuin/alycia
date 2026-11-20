# Protocole hors ligne

La fixture fixe les originaux, la portée, les questions, les tours antérieurs, les fichiers attendus, k et le budget. Chaque cas passe par les classes de recherche et de composition C# de l’application. Les métriques portent sur les chemins distincts des extraits effectivement retenus. Les cas sans document attendu ont leur propre dénominateur. Le cas temporel hors ligne vérifie l’absence de recherche documentaire superflue. Les tests de mémoire vérifient séparément le contenu rappelé.

Seuils définis avant modification : aucune fuite, aucun dépassement et aucune baisse du rappel moyen, du nDCG moyen ou du MRR par rapport au mode par défaut avant activation générale. Une hausse de latence sur cette petite fixture doit être examinée séparément. Les performances neuronales, la fidélité, le premier token et la VRAM nécessitent une campagne réelle sur le matériel cible.

```bash
dotnet run --project benchmarks/Alycia.ContextBenchmarks -- --offline evaluation/context-v3/offline.json
```
