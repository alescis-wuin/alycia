using System;
using Avalonia.Controls;
using Avalonia.Media;

namespace Alycia.Client.Avalonia;

internal static class DialogFields
{
    internal static TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
    internal static TextBox Input(string text, int length, bool multiline = false) => new()
    {
        Text = text,
        MaxLength = length,
        AcceptsReturn = multiline,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = multiline ? 90 : 32,
        MaxHeight = multiline ? 180 : 50
    };

    internal static Button ActionButtonInline(string label, Action action)
    {
        var button = new Button { Content = label };
        button.Click += (_, _) => action();
        return button;
    }
}
