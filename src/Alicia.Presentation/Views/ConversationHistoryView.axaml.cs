using Alicia.Presentation.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;

namespace Alicia.Presentation.Views;

public partial class ConversationHistoryView : UserControl
{
    public ConversationHistoryView()
    {
        InitializeComponent();
    }

    private async void OnConversationPreviewPointerEntered(
        object? sender,
        PointerEventArgs eventArgs)
    {
        _ = eventArgs;

        if (sender is not Control { DataContext: ConversationListItemViewModel item })
        {
            return;
        }

        await item.EnsurePreviewLoadedAsync().ConfigureAwait(true);
    }
}
