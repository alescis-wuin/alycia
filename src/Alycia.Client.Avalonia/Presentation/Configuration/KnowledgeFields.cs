using static Alycia.Client.Avalonia.DialogFields;
using System.Linq;
using Alycia.Contracts.Chat;
using Avalonia.Controls;

namespace Alycia.Client.Avalonia;

internal sealed class KnowledgeFields
{
    private readonly CheckBox _enabled;
    private readonly TextBox _model;
    private readonly NumericUpDown[] _numbers;
    public Expander Panel { get; }

    public KnowledgeFields(EnrichmentOptions options)
    {
        _enabled = new() { Content = "Préparer les annotations et synthèses", IsChecked = options.Enabled };
        _model = Input(options.Model, 200);
        var items = new (string Title, int Value, int Min, int Max)[] {
            ("Appels par cycle", options.JobCalls, 1, 8192), ("Essais par unité", options.CallsPerUnit, 1, 4),
            ("Fenêtre du résumeur · tokens", options.InputTokens, 2048, 32768), ("Réserve de sortie · tokens", options.OutputTokens, 128, 4096),
            ("Taille cible · tokens", options.TargetTokens, 64, 1024), ("Documents traités en parallèle", options.Workers, 1, 4),
            ("Délai par appel · secondes", options.TimeoutSeconds, 5, 600), ("Profondeur maximale des réductions", options.MaxDepth, 1, 24) };
        _numbers = items.Select(i => new NumericUpDown { Minimum = i.Min, Maximum = i.Max, Value = i.Value, Increment = 1, FormatString = "0" }).ToArray();
        var body = new StackPanel { Spacing = 8, Children = { _enabled, Label("Modèle local d’annotation · vide pour l’extraction sans modèle"), _model } };
        for (int i = 0; i < items.Length; i++) { body.Children.Add(Label(items[i].Title)); body.Children.Add(_numbers[i]); }
        Panel = new() { Header = "Indexation des connaissances", Content = body };
    }
    public EnrichmentOptions Read()
    {
        int Value(int index) => (int)(_numbers[index].Value ?? 0);
        var options = new EnrichmentOptions(_enabled.IsChecked == true, _model.Text?.Trim() ?? "",
            Value(0), Value(1), Value(2), Value(3), Value(4), Value(5), Value(6), Value(7));
        options.Validate(); return options;
    }
}
