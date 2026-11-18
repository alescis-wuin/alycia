using System;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CSharpMath.SkiaSharp;
using SkiaSharp;

namespace Alycia.Client.Avalonia;

internal static class FormulaView
{
    // Bound both parser depth and the eventual native surface allocation.
    internal static Control Create(string tex, double size, IBrush foreground)
    {
        var fallback = new SelectableTextBlock { Text = tex, FontFamily = Appearance.Mono, TextWrapping = TextWrapping.Wrap };
        if (tex.Length is 0 or > 2048) return fallback;
        int depth = 0;
        foreach (char ch in tex)
        {
            if (ch == '{' && ++depth > 24 || ch == '}' && --depth < 0) return fallback;
        }
        if (depth != 0) return fallback;
        try
        {
            var ink = foreground is ISolidColorBrush brush ? brush.Color : Colors.Gray;
            var painter = new MathPainter
            {
                FontSize = (float)size * 2,
                TextColor = new SKColor(ink.R, ink.G, ink.B, ink.A),
                LaTeX = tex
            };
            if (!string.IsNullOrEmpty(painter.ErrorMessage)) return fallback;
            var bounds = painter.Measure(2048);
            if (!(bounds.Width is >= 1 and <= 4096 && bounds.Height is >= 1 and <= 1024)) return fallback;
            using var png = painter.DrawAsStream();
            if (png is null) return fallback;
            var bitmap = new Bitmap(png);
            var image = new Image { Source = bitmap, Width = bitmap.PixelSize.Width / 2d,
                Height = bitmap.PixelSize.Height / 2d, HorizontalAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetName(image, tex);
            ToolTip.SetTip(image, tex);
            // Keep the bitmap alive across reattachment; release with the owning view.
            return image;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return fallback;
        }
    }
}
