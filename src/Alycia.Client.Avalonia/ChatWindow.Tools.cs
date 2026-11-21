using static Alycia.Client.Avalonia.DialogFields;
using System.Threading.Tasks;

namespace Alycia.Client.Avalonia;

public partial class ChatWindow
{
    private async Task ProjectToolsAsync()
    {
        if (_current is not { } chat) return;
        var inspection = await ToolInspection.LoadAsync(_client, chat.Id, _lifetime.Token);
        if (_current?.Id != chat.Id) return;
        var panel = inspection.CreatePanel(Label);
        var action = await ActionDialogAsync("Sources et outils", panel, [("Réglages", "settings"), ("Analyser tous les fichiers", "analysis")]);
        if (action == "settings") await SettingsAsync();
        if (action == "analysis") await AnalysesAsync();
    }
}
