using System;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Alycia.Client.Avalonia;

internal sealed record Appearance(string Theme = "system", int ReadingSize = 18, string DefaultModel = "", bool WrapCode = false, string ApiUrl = "")
{
    internal static Appearance Current { get; private set; } = new();
    private static string FileName => Path.Combine(
        Environment.GetEnvironmentVariable("ALYCIA_CLIENT_STATE_DIRECTORY")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Alycia"), "appearance.json");

    internal static void Load()
    {
        try
        {
            if (File.Exists(FileName) && new FileInfo(FileName).Length < 32_768)
                Apply(JsonSerializer.Deserialize<Appearance>(File.ReadAllText(FileName)) ?? new());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine("Préférences d’apparence illisibles.");
        }
    }

    internal static void Save(Appearance value)
    {
        string path = FileName;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value));
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); }
        Apply(value);
    }

    private static void Apply(Appearance value)
    {
        Current = value with { ReadingSize = Math.Clamp(value.ReadingSize, 14, 36) };
        if (Application.Current is not { } app) return;
        app.RequestedThemeVariant = value.Theme switch
        {
            "dark" => ThemeVariant.Dark,
            "light" => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };
        app.Resources["ReadingSize"] = (double)Current.ReadingSize;
        app.Resources["WrapCode"] = Current.WrapCode;
    }

    internal static IBrush Color(StyledElement owner, string role) =>
        owner.TryFindResource("Boreal" + role, owner.ActualThemeVariant, out var value)
        && value is IBrush brush ? brush : Brushes.Gray;

    internal static readonly FontFamily Mono = new("avares://Alycia.Client.Avalonia/Assets/Fonts/Hack#Hack");
}
