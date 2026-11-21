using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alycia.Contracts.Chat;
using Avalonia.Controls;
using Avalonia.Layout;
using static Alycia.Client.Avalonia.DialogFields;

namespace Alycia.Client.Avalonia;

internal sealed class ConversationSettingsEditor(ChatApiClient client)
{
    internal async Task<ConversationSummary?> EditAsync(Guid id, Func<Control, Func<string?>, Task<bool>> show, CancellationToken ct)
    {
        var settings = await client.SettingsAsync(id, ct);
        var current = settings.Conversation;
        var models = await client.ModelsAsync(ct);
        var model = new ComboBox
        {
            Name = "ConversationModelSelector",
            ItemsSource = models,
            SelectedItem = models.Contains(current.Model, StringComparer.Ordinal) ? current.Model : null,
            PlaceholderText = "Choisir un modèle génératif local",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var system = Input(current.SystemPrompt, 4000, true);
        var notes = Input(current.ContextNotes, 8000, true);
        var options = settings.Options;
        var context = Input(options.ContextTokens.ToString(), 6);
        var output = Input(options.MaxOutputTokens.ToString(), 6);
        var turns = Input(options.MaxHistoryTurns.ToString(), 3);
        var temperature = Input(options.Temperature.ToString(CultureInfo.InvariantCulture), 10);
        var seed = Input(options.Seed?.ToString() ?? "", 12);
        var timeout = Input(options.TimeoutSeconds.ToString(), 3);
        var calls = Input(options.SummaryMaxCalls.ToString(), 4);
        var toolCalls = Input(options.ToolMaxCalls.ToString(), 4);
        var knowledge = new KnowledgeFields(options.Enrichment);
        var budgets = new (string Label, TextBox Input, Action<ChatSettings, int> Set)[]
        {
            ("Résultats d’outils conservés · tokens (0 : adaptatif)", Input(options.ToolContextTokens.ToString(), 6), (o, v) => o.ToolContextTokens = v),
            ("Résultat maximal par appel · octets UTF-8", Input(options.ToolResultBytes.ToString(), 6), (o, v) => o.ToolResultBytes = v),
            ("Durée maximale des outils · secondes", Input(options.ToolTimeoutSeconds.ToString(), 5), (o, v) => o.ToolTimeoutSeconds = v),
            ("Extraits documentaires · tokens (0 : adaptatif)", Input(options.DocumentContextTokens.ToString(), 6), (o, v) => o.DocumentContextTokens = v),
            ("Échanges récents protégés de la réduction · 0 à 20", Input(options.ProtectedHistoryTurns.ToString(), 2), (o, v) => o.ProtectedHistoryTurns = v),
            ("Marge de sécurité · tokens", Input(options.SafetyTokens.ToString(), 6), (o, v) => o.SafetyTokens = v),
            ("Candidats à la recherche · caractères (0 : sans plafond global)", Input(options.IndexMaxCharacters.ToString(), 10), (o, v) => o.IndexMaxCharacters = v),
            ("Passages par lot d’embeddings · 1 à 64", Input(options.EmbeddingBatchSize.ToString(), 2), (o, v) => o.EmbeddingBatchSize = v),
            ("Paires à classifier", Input(options.ClassificationBudget.ToString(), 4), (o, v) => o.ClassificationBudget = v),
            ("Candidats incertains admis", Input(options.NeutralCandidates.ToString(), 2), (o, v) => o.NeutralCandidates = v),
            ("Seuil pertinent · %", Input(((int)Math.Round(100 * options.RelevantThreshold)).ToString(), 3), (o, v) => o.RelevantThreshold = v / 100.0),
            ("Seuil incertain · %", Input(((int)Math.Round(100 * options.NeutralThreshold)).ToString(), 3), (o, v) => o.NeutralThreshold = v / 100.0),
            ("Synthèses dans la réponse · tokens (0 : désactivées)", Input(options.QuerySummaryTokens.ToString(), 4), (o, v) => o.QuerySummaryTokens = v),
            ("Appels de synthèse à la demande (0 : cache seul)", Input(options.QuerySummaryMaxCalls.ToString(), 1), (o, v) => o.QuerySummaryMaxCalls = v),
            ("Mémoire · tokens (0 : adaptatif)", Input(options.MemoryContextTokens.ToString(), 6), (o, v) => o.MemoryContextTokens = v),
            ("Historique hors échanges protégés · tokens (0 : adaptatif)", Input(options.HistoryContextTokens.ToString(), 6), (o, v) => o.HistoryContextTokens = v),
            ("Sous-requêtes par demande · 1 à 8", Input(options.MaxSearchQueries.ToString(), 1), (o, v) => o.MaxSearchQueries = v),
            ("Candidats ONNX · 1 à 64", Input(options.RerankCandidateLimit.ToString(), 2), (o, v) => o.RerankCandidateLimit = v),
            ("Délai du reclassement · millisecondes", Input(options.RerankTimeoutMs.ToString(), 5), (o, v) => o.RerankTimeoutMs = v),
            ("Appels du plan d’exploration · 1 à 64", Input(options.AgentPlanMaxCalls.ToString(), 2), (o, v) => o.AgentPlanMaxCalls = v),
            ("Taille des passages d’embeddings · 128 à 3200 caractères", Input(options.EmbeddingChunkCharacters.ToString(), 4), (o, v) => o.EmbeddingChunkCharacters = v)
        };
        var embedHistory = new CheckBox { Content = "Indexer aussi les messages de cette conversation", IsChecked = options.EmbedHistory };
        var retrieval = new ComboBox { ItemsSource = new[] { "legacy", "lexical", "hybrid" }, SelectedItem = options.RetrievalMode };
        var embedding = Input(options.EmbeddingModel, 200);
        var tokenizer = new CheckBox { Content = "Utiliser le tokenizer compatible du modèle quand disponible", IsChecked = options.TokenCounting == "auto" };
        var tools = new CheckBox { Content = "Autoriser les outils de lecture des sources importées", IsChecked = options.UseProjectTools };
        var continuous = new CheckBox { Content = "Générer jusqu’au signal d’arrêt du modèle", IsChecked = options.GenerateUntilStop };
        var structured = new CheckBox { Content = "Mémoire structurée : citations, décisions et contraintes", IsChecked = options.StructuredMemory };
        var recursive = new CheckBox { Content = "Réduire les anciens échanges si nécessaire", IsChecked = options.AutoSummarize };
        turns.IsEnabled = !options.AutoSummarize;
        recursive.IsCheckedChanged += (_, _) => turns.IsEnabled = recursive.IsChecked != true;
        var panel = new StackPanel { Spacing = 10 };
        var presets = new ComboBox
        {
            ItemsSource = PromptPresets.BuiltIn.Concat(await client.PromptsAsync(ct)).ToArray(),
            PlaceholderText = "Charger un profil de prompt"
        };
        presets.SelectionChanged += (_, _) => { if (presets.SelectedItem is PromptItem item) system.Text = item.SystemPrompt; };
        panel.Children.Add(Label($"Modèle actuellement enregistré : {current.Model}"));
        if (!models.Contains(current.Model, StringComparer.Ordinal))
            panel.Children.Add(Label("Ce modèle n’est pas disponible pour la génération locale. Sélectionnez un modèle ci-dessous pour reprendre cette conversation ; votre brouillon est conservé."));
        panel.Children.Add(Label("Modèle de la conversation"));
        panel.Children.Add(model);
        var modelWarning = Label("Changer de modèle efface le résumé généré de cette conversation.");
        modelWarning.IsVisible = false;
        model.SelectionChanged += (_, _) => modelWarning.IsVisible = model.SelectedItem is string choice && choice != current.Model;
        panel.Children.Add(modelWarning);
        string inherited = (current.WorkspaceId is Guid projectId ? (await client.ProjectAsync(projectId, ct)).DefaultModel : "");
        if (string.IsNullOrEmpty(inherited)) inherited = Appearance.Current.DefaultModel;
        if (models.Contains(inherited)) panel.Children.Add(ActionButtonInline("Restaurer le modèle par défaut", () => model.SelectedItem = inherited));
        panel.Children.Add(Label("Instruction de la conversation"));
        panel.Children.Add(presets);
        panel.Children.Add(system);
        panel.Children.Add(Label("Notes privées de la conversation"));
        panel.Children.Add(notes);
        var advanced = new StackPanel { Spacing = 10 };
        foreach (var toggle in new[] { continuous, recursive, structured, tools, tokenizer }) advanced.Children.Add(toggle);
        foreach (var (label, control) in new (string, Control)[] {
            ("Recherche documentaire", retrieval), ("Modèle d’embeddings pour la recherche hybride", embedding),
            ("Fenêtre du modèle en tokens", context), ("Réserve pour la réponse", output),
            ("Nombre d’échanges récents", turns), ("Température", temperature), ("Graine facultative", seed),
            ("Délai d’inactivité en secondes", timeout), ("Appels de résumé par opération", calls), ("Garde-fou du nombre d’appels (0 : sans quota)", toolCalls) })
        { advanced.Children.Add(Label(label)); advanced.Children.Add(control); }
        foreach (var budget in budgets) { advanced.Children.Add(Label(budget.Label)); advanced.Children.Add(budget.Input); }
        var routeMode = new ComboBox { ItemsSource = new[] { "off", "auto", "direct", "memory", "retrieval", "explore" }, SelectedItem = options.RoutingMode };
        advanced.Children.Add(Label("Stratégie de routage")); advanced.Children.Add(routeMode);
        var documentPrefix = Input(options.EmbeddingDocumentPrefix, 256);
        var queryPrefix = Input(options.EmbeddingQueryPrefix, 256);
        advanced.Children.Add(Label("Préfixe d’embedding des documents")); advanced.Children.Add(documentPrefix);
        advanced.Children.Add(Label("Préfixe d’embedding des requêtes")); advanced.Children.Add(queryPrefix);
        var features = new (CheckBox Control, Action<ChatSettings, bool> Set)[]
        {
            (new() { Content = "Préciser les questions ambiguës avec le modèle d’annotation", IsChecked = options.QueryBriefModelEnabled }, (o, v) => o.QueryBriefModelEnabled = v),
            (new() { Content = "Rechercher par facettes lexicales et sémantiques", IsChecked = options.FacetPlanningEnabled }, (o, v) => o.FacetPlanningEnabled = v),
            (new() { Content = "Classifier les passages avant reclassement", IsChecked = options.ClassificationEnabled }, (o, v) => o.ClassificationEnabled = v),
            (new() { Content = "Reclasser les passages avec le modèle ONNX local", IsChecked = options.RerankEnabled }, (o, v) => o.RerankEnabled = v),
            (new() { Content = "Résoudre les références et décomposer les comparaisons", IsChecked = options.QueryRewriteEnabled }, (o, v) => o.QueryRewriteEnabled = v),
            (new() { Content = "Diversifier les preuves sous le budget de contexte", IsChecked = options.EvidenceSelectionEnabled }, (o, v) => o.EvidenceSelectionEnabled = v),
            (new() { Content = "Indexer les sections parentes et les symboles", IsChecked = options.HierarchicalIndexEnabled }, (o, v) => o.HierarchicalIndexEnabled = v),
            (new() { Content = "Retrouver les déclarations, corrections et propositions datées", IsChecked = options.TemporalMemoryEnabled }, (o, v) => o.TemporalMemoryEnabled = v),
            (new() { Content = "Planifier les recherches et suivre les lacunes", IsChecked = options.AgentPlanningEnabled }, (o, v) => o.AgentPlanningEnabled = v),
            (new() { Content = "Vérifier les affirmations après la génération", IsChecked = options.ClaimVerificationEnabled }, (o, v) => o.ClaimVerificationEnabled = v),
            (new() { Content = "Autoriser une recherche complémentaire de vérification", IsChecked = options.VerificationSearchEnabled }, (o, v) => o.VerificationSearchEnabled = v),
            (new() { Content = "Adapter la fenêtre d’inférence et prioriser la génération", IsChecked = options.AdaptiveInferenceEnabled }, (o, v) => o.AdaptiveInferenceEnabled = v)
        };
        foreach (var feature in features) advanced.Children.Add(feature.Control);
        advanced.Children.Add(embedHistory);
        advanced.Children.Add(knowledge.Panel);
        advanced.Children.Add(Label("L’indexation hybride est indépendante du budget de conversation. Les résultats retirés du contexte libèrent leur budget ; les originaux restent conservés."));
        panel.Children.Add(new Expander { Header = "Avancé", Content = advanced });
        ChatSettings? edited = null;
        string? Validate()
        {
            if (model.SelectedItem is not string modelName || !models.Contains(modelName, StringComparer.Ordinal))
                return "Choisissez un modèle génératif local disponible.";
            if (!int.TryParse(context.Text, out var contextValue) || !int.TryParse(output.Text, out var outputValue)
                || !int.TryParse(turns.Text, out var turnsValue) || !int.TryParse(timeout.Text, out var timeoutValue)
                || !int.TryParse(calls.Text, out var callsValue) || !int.TryParse(toolCalls.Text, out var toolCallsValue)
                || !double.TryParse(temperature.Text?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var temperatureValue)
                || !string.IsNullOrWhiteSpace(seed.Text) && !int.TryParse(seed.Text, out _))
                return "Un réglage numérique est invalide. Les valeurs saisies sont conservées.";
            edited = new ChatSettings
            {
                RoutingMode = routeMode.SelectedItem as string ?? "off",
                EmbeddingDocumentPrefix = documentPrefix.Text ?? "",
                EmbeddingQueryPrefix = queryPrefix.Text ?? "",
                RetrievalMode = retrieval.SelectedItem as string ?? "lexical",
                EmbeddingModel = embedding.Text?.Trim() ?? "",
                TokenCounting = tokenizer.IsChecked == true ? "auto" : "conservative",
                ContextTokens = contextValue,
                MaxOutputTokens = outputValue,
                MaxHistoryTurns = turnsValue,
                TimeoutSeconds = timeoutValue,
                Temperature = temperatureValue,
                SafetyTokens = options.SafetyTokens,
                SummaryMaxCalls = callsValue,
                ToolMaxCalls = toolCallsValue,
                UseProjectTools = tools.IsChecked == true,
                GenerateUntilStop = continuous.IsChecked == true,
                AutoSummarize = recursive.IsChecked == true,
                StructuredMemory = structured.IsChecked == true,
                Seed = string.IsNullOrWhiteSpace(seed.Text) ? null : int.Parse(seed.Text!)
            };
            foreach (var budget in budgets)
            {
                if (!int.TryParse(budget.Input.Text, out var value)) return budget.Label + " : nombre entier attendu.";
                budget.Set(edited, value);
            }
            edited.EmbedHistory = embedHistory.IsChecked == true;
            try
            {
                edited.Enrichment = knowledge.Read();
                foreach (var feature in features) feature.Set(edited, feature.Control.IsChecked == true);
                edited.Validate();
                if (edited.UseProjectTools) ToolWindowBudget.SafetyTokens(edited, SettingsInput.Estimate);
                SettingsInput.ValidateWindow(modelName, system.Text, notes.Text, edited);
                return null;
            }
            catch (ArgumentException exception) { return exception.Message; }
        }
        if (!await show(panel, Validate)) return null;
        return await client.ConfigureAsync(current.Id, current.Revision, (string)model.SelectedItem!, system.Text ?? "",
            notes.Text ?? "", edited!, ct);
    }
}
