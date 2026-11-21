using static Alycia.Client.Avalonia.DialogFields;
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace Alycia.Client.Avalonia;

public partial class ChatWindow
{

    private async Task PromptsAsync()
    {
        var profiles = PromptPresets.BuiltIn.Concat(await _client.PromptsAsync(_lifetime.Token)).ToArray();
        var selector = new ComboBox { ItemsSource = profiles, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch };
        var name = Input("Mon prompt", 100);
        var prompt = Input(C<TextBox>("SystemPromptInput").Text ?? "", 4000, true);
        var action = new ComboBox
        {
            ItemsSource = new[] { "Appliquer aux nouvelles conversations", "Créer un profil",
            "Modifier le profil sélectionné", "Supprimer le profil sélectionné" },
            SelectedIndex = 0
        };
        selector.SelectionChanged += (_, _) =>
        {
            if (selector.SelectedItem is PromptItem selected) { name.Text = selected.Name; prompt.Text = selected.SystemPrompt; }
        };
        var panel = new StackPanel
        {
            Spacing = 12,
            Children = { Label("Profils intégrés et personnels"), selector,
            Label("Nom"), name, Label("Instruction système"), prompt, Label("Action"), action,
            Label("Un profil appliqué à une conversation est une copie. Modifier la bibliothèque ne modifie pas ses conversations existantes.") }
        };
        if (!await EditDialogAsync("Bibliothèque de prompts", panel, "Appliquer l’action")) return;
        var selectedProfile = selector.SelectedItem as PromptItem;
        switch (action.SelectedIndex)
        {
            case 0:
                C<TextBox>("SystemPromptInput").Text = prompt.Text;
                Feedback("Prompt choisi pour les nouvelles conversations.");
                break;
            case 1:
                await _client.SavePromptAsync(null, name.Text ?? "", prompt.Text ?? "", _lifetime.Token);
                Feedback("Profil créé.");
                break;
            case 2:
                if (selectedProfile is null || selectedProfile.Id == Guid.Empty) throw new ChatApiException("Sélectionnez un profil personnel à modifier, ou créez une copie.");
                await _client.SavePromptAsync(selectedProfile.Id, name.Text ?? "", prompt.Text ?? "", _lifetime.Token);
                Feedback("Profil modifié.");
                break;
            case 3:
                if (selectedProfile is null || selectedProfile.Id == Guid.Empty) throw new ChatApiException("Les profils intégrés sont conservés.");
                if (await EditDialogAsync("Supprimer le profil ?", Label("Supprimer « " + selectedProfile.Name + " » ? Les conversations restent inchangées."), "Supprimer"))
                {
                    await _client.DeletePromptAsync(selectedProfile.Id, _lifetime.Token);
                    Feedback("Profil supprimé.");
                }
                break;
        }
    }
}
