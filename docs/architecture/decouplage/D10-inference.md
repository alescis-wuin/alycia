# D10 — Adaptateur Ollama autonome

Alycia.Modules.Inference contient les transports de texte et de vecteurs, la lecture des capacités et le tokenizer Qwen. Son unique référence produit est Contracts. ITextGeneration consomme des paramètres immuables et retourne la réponse physique. ChatGenerationAdapter traduit cette réponse et les capacités pour le tour. La composition fournit le même InferenceScheduler singleton aux deux transports ; l’attente du permis reste annulable, l’échéance commence après acquisition, les lignes du flux la renouvellent. Le verrou de conversation est hors du module physique.

58 tests du fournisseur simulé, de l’admission, du comptage, des outils et du streaming ont réussi. **Partiel** pour la séparation du signal de priorité du verrou de conversation et les mesures sur un modèle réel. Ces essais n’ont utilisé aucun poids Ollama ni GPU.

## Validation après composition finale

D17 a séparé le bail de conversation, le signal de priorité et le permis physique. À D19 le cache des tokenizers a une durée de vie explicite par hôte. La suite autonome Inference.Tests s’exécute avec le fournisseur simulé et avec les adaptateurs Ollama texte et embeddings alimentés par HTTP de test. Elle vérifie l’ordre, les erreurs, l’annulation, les vecteurs contrôlés et la suspension d’un delta. Les mesures de charge finie des tests d’ordonnancement figurent dans D19-preuves.json. Le résultat de D10 porte sur ce périmètre simulé.
