using Alicia.Presentation.State;
using Alicia.Presentation.ViewModels;
using Avalonia.Headless.XUnit;
using Avalonia.Media;

namespace Alicia.Presentation.Tests.Ui;

public sealed class ConversationIdentityBrushTests
{
    [AvaloniaFact]
    public async Task IdentityPaletteCanBeReadFromAnotherThreadWithoutChangingColors()
    {
        ConversationIdentityColor[] colors = Enum.GetValues<ConversationIdentityColor>();
        ISolidColorBrush[] accents = colors
            .Select(color => Assert.IsAssignableFrom<ISolidColorBrush>(
                ConversationIdentityPresentationCatalog.GetAccentBrush(color)))
            .ToArray();
        ISolidColorBrush[] surfaces = colors
            .Select(color => Assert.IsAssignableFrom<ISolidColorBrush>(
                ConversationIdentityPresentationCatalog.GetSurfaceBrush(color)))
            .ToArray();
        int originalThread = Environment.CurrentManagedThreadId;

        await Task.Factory.StartNew(
            () =>
            {
                Assert.NotEqual(originalThread, Environment.CurrentManagedThreadId);
                uint[] expectedRgb = [0x43E6D1, 0x8C7CFF, 0x61A8FF, 0x72E6A7, 0xFFB454, 0xFF7FA3, 0x93A4C7];
                Assert.Equal(expectedRgb.Length, accents.Length);

                for (int index = 0; index < expectedRgb.Length; index++)
                {
                    Assert.Equal(0xFF000000 | expectedRgb[index], accents[index].Color.ToUInt32());
                    Assert.Equal(0x26000000 | expectedRgb[index], surfaces[index].Color.ToUInt32());
                    Assert.Equal(1d, accents[index].Opacity);
                    Assert.Equal(1d, surfaces[index].Opacity);
                    Assert.Null(accents[index].Transform);
                    Assert.Null(surfaces[index].Transform);
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).ConfigureAwait(true);
    }
}
