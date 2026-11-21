# D10 — Adaptateur Ollama autonome

Alycia.Modules.Inference contient les transports de texte et de vecteurs, la lecture des capacités et le tokenizer Qwen. Son unique référence produit est Contracts. ITextGeneration consomme des paramètres immuables et retourne la réponse physique. ChatGenerationAdapter traduit cette réponse et les capacités pour le tour. La composition fournit le même InferenceScheduler singleton aux deux transports ; l’attente du permis reste annulable, l’échéance commence après acquisition, les lignes du flux la renouvellent. Le verrou de conversation est hors du module physique.

58 tests du fournisseur simulé, de l’admission, du comptage, des outils et du streaming ont réussi. **Partiel** pour la séparation du signal de priorité du verrou de conversation et les mesures sur un modèle réel. Ces essais n’ont utilisé aucun poids Ollama ni GPU.
