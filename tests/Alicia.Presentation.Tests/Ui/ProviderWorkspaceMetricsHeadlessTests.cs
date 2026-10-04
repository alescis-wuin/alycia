using System.Globalization;
using Alicia.Application.Providers;
using Alicia.Presentation.Tests.ViewModels;
using Alicia.Presentation.ViewModels;
using Alicia.Presentation.Views;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Alicia.Presentation.Tests.Ui;

public sealed partial class ProviderWorkspaceHeadlessTests
{
    [AvaloniaTheory]
    [InlineData(720, 560)]
    [InlineData(900, 700)]
    [InlineData(1280, 820)]
    [InlineData(1600, 900)]
    public async Task StructuredMetricsAlignNumbersAndUnitsAtEachViewport(int width, int height)
    {
        StubInferenceProviderRuntime runtime = new(InferenceProviderState.Running)
        {
            LatestGenerationObservation = MetricsObservation(),
        };
        MainViewModel vm = UiTestMainViewModelFactory.Create(runtime: runtime);
        await vm.InitializeAsync();
        MainWindow window = CreateWindow(vm);
        try
        {
            Show(window, width, height);
            ProviderWorkspaceView view = Find<ProviderWorkspaceView>(window, "Provider workspace");
            Grid table = view.FindControl<Grid>("GenerationMetricsTable")!;
            table.BringIntoView();
            Flush();
            TextBlock input = Find<TextBlock>(view, "Input tokens");

            double DecimalX(TextBlock value) => Position(value, table).X + value.TextLayout.HitTestTextPosition(
                value.Text!.IndexOf(CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator, StringComparison.Ordinal)).X;
            double LastDigitRight(TextBlock value) => Position(value, table).X
                + value.TextLayout.HitTestTextPosition(value.Text!.Length - 1).Right;
            foreach (string direction in new[] { "Input", "Output" })
            {
                TextBlock tokens = Find<TextBlock>(view, $"{direction} tokens");
                TextBlock duration = Find<TextBlock>(view, $"{direction} duration");
                TextBlock rate = Find<TextBlock>(view, $"{direction} rate");
                Assert.InRange(Math.Abs(DecimalX(duration) - DecimalX(rate)), 0, 0.1);
                Assert.InRange(Math.Abs(LastDigitRight(tokens) - LastDigitRight(duration)), 0, 0.1);
                Assert.InRange(Math.Abs(LastDigitRight(tokens) - LastDigitRight(rate)), 0, 0.1);
                Assert.True(LastDigitRight(tokens) > DecimalX(duration) + 30);
                foreach (TextBlock value in new[] { tokens, duration, rate })
                {
                    Assert.InRange(Math.Abs(LastDigitRight(value) - Position(value, table).X - value.Bounds.Width), 0, 0.1);
                }
            }

            double firstDigitWidth = input.TextLayout.HitTestTextPosition(0).Width;
            for (int index = 1; index < input.Text!.Length; index++)
            {
                Assert.InRange(Math.Abs(input.TextLayout.HitTestTextPosition(index).Width - firstDigitWidth), 0, 0.1);
            }

            TextBlock[] tableUnits = table.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("provider-unit")).ToArray();
            Assert.Equal(2, tableUnits.Length);
            Assert.Contains(tableUnits, t => t.Text == "(s)");
            Assert.Contains(tableUnits, t => t.Text == "(tok/s)");
            Assert.All(tableUnits, unit => Assert.Equal(0, Grid.GetColumn(Assert.IsType<StackPanel>(unit.Parent))));
            Assert.Equal(5, table.Children.OfType<Border>().Count(b => b.Classes.Contains("provider-grid-line")));
            TextBlock[] allUnits = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("provider-unit")).ToArray();
            Assert.Equal(7, allUnits.Length);
            foreach (TextBlock unit in allUnits)
            {
                Assert.Equal(FontWeight.Bold, unit.FontWeight);
                Assert.Equal(16, unit.FontSize);
                Assert.Equal(Color.Parse("#8C7CFF"), Assert.IsAssignableFrom<ISolidColorBrush>(unit.Foreground).Color);
                Assert.NotEqual(Assert.IsAssignableFrom<ISolidColorBrush>(input.Foreground).Color,
                    Assert.IsAssignableFrom<ISolidColorBrush>(unit.Foreground).Color);
            }

            Assert.Equal("Version", view.FindControl<TextBlock>("RuntimeVersionLabel")!.Text);
            foreach (TextBlock number in table.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("provider-number")))
            {
                Assert.True(number.FontSize >= 18);
                Assert.NotEmpty(number.FontFeatures!);
                Assert.Contains("DejaVu Sans Mono", number.FontFamily.Name);
                if (number.Classes.Contains("provider-integer"))
                {
                    Assert.DoesNotContain(CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator, number.Text!);
                }
                else
                {
                    Assert.Equal(3, number.Text!.Split(CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator)[1].Length);
                }
                Assert.True(number.TextLayout.Width <= number.Bounds.Width + 0.5);
                AssertHorizontalContainment(window, number);
            }

            Border state = view.FindControl<Border>("RuntimeStateBadge")!;
            Border version = view.FindControl<Border>("RuntimeVersionBadge")!;
            Border fallback = view.FindControl<Border>("RuntimeFallbackBadge")!;
            foreach (Border badge in new[] { version, fallback })
            {
                Assert.Equal(Position(state, view).X, Position(badge, view).X);
                Assert.Equal(state.Bounds.Height, badge.Bounds.Height);
                Assert.Equal(state.Padding, badge.Padding);
                Assert.Equal(16, Assert.IsType<TextBlock>(badge.Child).FontSize);
            }

            Assert.Equal("MiniCPM5-2B-GGUF", Find<TextBlock>(window, "Last generation model").Text);
            Assert.Equal("openbmb", Find<TextBlock>(window, "Last generation author").Text);
            Assert.Equal("Completed", Find<TextBlock>(window, "Last generation outcome").Text);
            Assert.False(Find<TextBlock>(window, "Last generation runtime version").IsEffectivelyVisible);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Last generation completed");
            Assert.Equal(0, runtime.CheckUpdateCount);
            Assert.Equal(0, runtime.InspectStorageCount);
            UiTestArtifactWriter.Capture(window, $"provider/metrics-{width}x{height}.png");

            await vm.InspectProviderStorageCommand.ExecuteAsync(null);
            Flush();
            Assert.Equal("KiB", Find<TextBlock>(window, "Runtime storage unit").Text);
            Assert.Equal($"{4.0:0.000}", Find<TextBlock>(window, "Runtime storage value").Text);
            Assert.Equal(Position(Find<TextBlock>(window, "Runtime storage unit"), view).X,
                Position(Find<TextBlock>(window, "Model cache unit"), view).X);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("fr-FR", "1376", "0,457", "3007,700", "4,000")]
    [InlineData("en-US", "1376", "0.457", "3007.700", "4.000")]
    public async Task MetricsUseLocalCultureAndRefreshAfterGeneration(
        string culture, string tokens, string duration, string rate, string storage)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        MainWindow? window = null;
        try
        {
            StubInferenceProviderRuntime runtime = new(InferenceProviderState.Running);
            MainViewModel vm = UiTestMainViewModelFactory.Create(runtime: runtime);
            await vm.InitializeAsync();
            window = CreateWindow(vm);
            Show(window, 1280, 820);
            Assert.True(Find<TextBlock>(window, "No generation observed").IsEffectivelyVisible);
            Assert.False(Find<StackPanel>(window, "Last generation metrics").IsEffectivelyVisible);
            runtime.LatestGenerationObservation = MetricsObservation(version: "b10000");
            vm.Provider.RefreshObservability();
            await vm.InspectProviderStorageCommand.ExecuteAsync(null);
            Flush();
            Assert.Equal(tokens, Find<TextBlock>(window, "Input tokens").Text);
            Assert.Equal(duration, Find<TextBlock>(window, "Input duration").Text);
            Assert.Equal(rate, Find<TextBlock>(window, "Input rate").Text);
            Assert.Equal(storage, Find<TextBlock>(window, "Runtime storage value").Text);
            Assert.True(Find<TextBlock>(window, "Last generation runtime version").IsEffectivelyVisible);
            Assert.False(Find<TextBlock>(window, "No generation observed").IsEffectivelyVisible);
            Assert.Equal("b10000", Find<TextBlock>(window, "Last generation runtime version").Text);
            Assert.Contains("b10000", vm.Provider.WorkspaceGenerationHelp);
            runtime.LatestGenerationObservation = MetricsObservation();
            vm.Provider.RefreshObservability();
            Flush();
            Assert.False(Find<TextBlock>(window, "Last generation runtime version").IsEffectivelyVisible);
        }
        finally
        {
            window?.Close();
            CultureInfo.CurrentCulture = previous;
        }
    }

    [AvaloniaTheory]
    [InlineData(InferenceProviderGenerationOutcome.Completed)]
    [InlineData(InferenceProviderGenerationOutcome.Cancelled)]
    [InlineData(InferenceProviderGenerationOutcome.Failed)]
    public async Task PartialGenerationPreservesOutcomeAndUnavailableValues(InferenceProviderGenerationOutcome outcome)
    {
        StubInferenceProviderRuntime runtime = new(InferenceProviderState.Running)
        {
            LatestGenerationObservation = MetricsObservation(outcome, partial: true, version: "b10000"),
        };
        MainViewModel vm = UiTestMainViewModelFactory.Create(runtime: runtime);
        await vm.InitializeAsync();
        MainWindow window = CreateWindow(vm);
        try
        {
            Show(window, 1280, 820);
            Find<Grid>(window, "Input and output metrics").BringIntoView();
            Flush();
            Assert.Equal(outcome.ToString(), Find<TextBlock>(window, "Last generation outcome").Text);
            Assert.Equal("—", Find<TextBlock>(window, "Output tokens").Text);
            Assert.Equal("—", Find<TextBlock>(window, "Output rate").Text);
            Assert.Equal($"{0:0.000}", Find<TextBlock>(window, "Cached input").Text);
            Assert.Equal(outcome == InferenceProviderGenerationOutcome.Failed,
                Find<TextBlock>(window, "Last generation failure").IsEffectivelyVisible);
            UiTestArtifactWriter.Capture(window, $"provider/metrics-{outcome.ToString().ToLowerInvariant()}-1280x820.png");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task HelpAndRoundedMaintenanceRemainKeyboardOperable()
    {
        StubInferenceProviderRuntime runtime = new(InferenceProviderState.Running)
        {
            LatestGenerationObservation = MetricsObservation(),
        };
        MainViewModel vm = UiTestMainViewModelFactory.Create(runtime: runtime);
        await vm.InitializeAsync();
        MainWindow window = CreateWindow(vm);
        try
        {
            Show(window, 1280, 820);
            Button help = Find<Button>(window, "About generation metrics");
            Button primary = Find<Button>(window, "Provider lifecycle action");
            TextBlock actionLabel = Assert.Single(primary.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == vm.ProviderPrimaryLabel);
            Assert.True(actionLabel.FontSize >= 18);
            ComboBox selector = Find<ComboBox>(window, "Inference provider");
            Border selectorBackground = Assert.Single(selector.GetVisualDescendants().OfType<Border>(), b => b.Name == "Background");
            Assert.Equal(Avalonia.Media.Color.Parse("#1A2540"), Assert.IsType<Avalonia.Media.SolidColorBrush>(selectorBackground.Background).Color);
            help.BringIntoView();
            Flush();
            Assert.IsType<PathIcon>(help.Content);
            Assert.Equal(32, help.Width);
            Assert.All(window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("provider-icon") && !b.Classes.Contains("provider-help")), b => Assert.Equal(38, b.Width));
            Assert.NotNull(ToolTip.GetTip(help));
            Assert.Equal(PlacementMode.Bottom, ToolTip.GetPlacement(help));
            Assert.True(help.Focus());
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Flush();
            Assert.True(ToolTip.GetIsOpen(help));
            UiTestArtifactWriter.Capture(window, "provider/metrics-help-1280x820.png");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Flush();
            Assert.False(ToolTip.GetIsOpen(help));
            Assert.True(help.IsFocused);
            Expander maintenance = Find<Expander>(window, "Managed provider maintenance");
            ToggleButton header = Assert.Single(maintenance.GetVisualDescendants().OfType<ToggleButton>(), b => b.Name == "ExpanderHeader");
            Assert.Equal(0, header.BorderThickness.Left);
            Assert.True(header.CornerRadius.TopLeft >= 14);
            Assert.True(header.FontSize >= 20);
            Assert.True(Assert.IsType<TextBlock>(header.Content).FontSize >= 20);
            header.BringIntoView();
            Flush();
            Assert.True(header.Focus());
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            Flush();
            Assert.True(maintenance.IsExpanded);
            Show(window, 720, 560);
            Assert.True(maintenance.IsExpanded);
            Assert.True(header.IsFocused);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task LargeMetricsAndLongModelStayReadableAcrossBreakpoint()
    {
        StubInferenceProviderRuntime runtime = new(InferenceProviderState.Running)
        {
            LatestGenerationObservation = MetricsObservation(large: true),
        };
        MainViewModel vm = UiTestMainViewModelFactory.Create(runtime: runtime);
        await vm.InitializeAsync();
        MainWindow window = CreateWindow(vm);
        try
        {
            foreach (int width in new[] { 1120, 720 })
            {
                Show(window, width, 820);
                Grid table = Find<Grid>(window, "Input and output metrics");
                table.BringIntoView();
                Flush();
                foreach (TextBlock value in table.GetVisualDescendants().OfType<TextBlock>())
                {
                    Assert.True(value.TextLayout.Width <= value.Bounds.Width + 0.5, $"Clipped {value.Text} at {width}: {value.TextLayout.Width} > {value.Bounds.Width}");
                    AssertHorizontalContainment(window, value);
                }

                AssertHorizontalContainment(window, Find<TextBlock>(window, "Last generation model"));
                if (width == 1120)
                {
                    UiTestArtifactWriter.Capture(window, "provider/metrics-large-1120x820.png");
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task OldReleasesDistinguishUninspectedNoneAndPositiveCount()
    {
        StubInferenceProviderRuntime runtime = new(InferenceProviderState.Running);
        MainViewModel vm = UiTestMainViewModelFactory.Create(runtime: runtime);
        await vm.InitializeAsync();
        MainWindow window = CreateWindow(vm);
        try
        {
            Show(window, 1280, 820);
            TextBlock status = Find<TextBlock>(window, "Old releases status");
            TextBlock count = Find<TextBlock>(window, "Old releases count");
            status.BringIntoView();
            Flush();
            Assert.True(status.IsEffectivelyVisible);
            Assert.False(count.IsEffectivelyVisible);
            Assert.Equal("Unknown", status.Text);
            Assert.Equal(0, runtime.InspectStorageCount);
            UiTestArtifactWriter.Capture(window, "provider/storage-unknown-1280x820.png");
            foreach (int retained in new[] { 0, 3 })
            {
                runtime.StorageInfo = new InferenceProviderStorageInfo(true, "test", retained, 4096, 2048);
                await vm.InspectProviderStorageCommand.ExecuteAsync(null);
                Flush();
                Assert.Equal(retained == 0, status.IsEffectivelyVisible);
                Assert.Equal(retained > 0, count.IsEffectivelyVisible);
                Assert.Equal(retained == 0 ? "None detected" : "3", vm.Provider.WorkspaceRetainedReleases);
                TextBlock size = Find<TextBlock>(window, "Runtime storage value");
                TextBlock unit = Find<TextBlock>(window, "Runtime storage unit");
                double unitGap = Position(unit, window).X - Position(size, window).X - size.Bounds.Width;
                Assert.InRange(unitGap, 5, 7);
                UiTestArtifactWriter.Capture(window, $"provider/storage-{(retained == 0 ? "none" : "positive")}-1280x820.png");
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static InferenceProviderGenerationObservation MetricsObservation(
        InferenceProviderGenerationOutcome outcome = InferenceProviderGenerationOutcome.Completed,
        bool partial = false,
        string version = "test",
        bool large = false)
    {
        DateTimeOffset start = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
        return new InferenceProviderGenerationObservation(
            "llama.cpp.cuda", "llama.cpp CUDA",
            large ? "openbmb/A-long-model-name-with-quantization-and-extra-context-GGUF:Q4_K_M" : "openbmb/MiniCPM5-2B-GGUF", version,
            start, start.AddSeconds(3.02), outcome,
            outcome == InferenceProviderGenerationOutcome.Failed ? InferenceProviderFailureKind.Network : null,
            TimeSpan.FromSeconds(3.02), partial ? null : TimeSpan.FromMilliseconds(474),
            large ? int.MaxValue : 1376, partial ? null : 229, large || partial ? null : 1605, 0,
            TimeSpan.FromMilliseconds(457), partial ? null : TimeSpan.FromSeconds(2.55),
            large ? 1234567.89 : 3007.7, partial ? null : 89.6);
    }
}
