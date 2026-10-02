using Alicia.Presentation.ViewModels;
using Alicia.Presentation.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Alicia.Presentation.Tests.Ui;

public sealed class ConversationComposerHeadlessTests
{
    [AvaloniaTheory]
    [InlineData(720, 560, "720x560")]
    [InlineData(900, 700, "900x700")]
    [InlineData(1280, 820, "1280x820")]
    [InlineData(1600, 900, "1600x900")]
    public async Task ComposerUsesTwoContainedSemanticLines(
        int width,
        int height,
        string snapshotName)
    {
        MainViewModel viewModel = UiTestMainViewModelFactory.CreateComposerScenario();
        await viewModel.InitializeAsync().ConfigureAwait(true);
        viewModel.MessageDraft = "Headless composer message";

        MainWindow window = CreateConversationWindow(viewModel);
        try
        {
            ShowAndFlush(window);
            ResizeAndFlush(window, width, height);

            Border composer = FindRequired<Border>(window, "Message composer");
            Grid messageLine = FindRequired<Grid>(composer, "Message and response actions");
            Grid readinessLine = FindRequired<Grid>(composer, "Branch model and profile readiness");
            TextBox message = FindRequired<TextBox>(composer, "Message text");
            Button send = FindRequired<Button>(composer, "Send message");
            Button selector = FindRequired<Button>(composer, "Choose model and generation profile");
            TextBlock readiness = FindRequired<TextBlock>(composer, "Branch model and profile readiness status");

            UiTestArtifactWriter.Capture(window, $"conversation/composer-ready-{snapshotName}.png");

            Assert.True(viewModel.ShowMessageComposer);
            Assert.True(viewModel.CanSendMessage);
            Assert.True(composer.IsEffectivelyVisible);
            Assert.True(message.IsEffectivelyVisible);
            Assert.True(send.IsEffectivelyVisible);
            Assert.True(selector.IsEffectivelyVisible);
            Assert.True(readiness.IsEffectivelyVisible);

            AssertHorizontallyInside(window, composer);
            AssertHorizontallyInside(window, message);
            AssertHorizontallyInside(window, send);
            AssertHorizontallyInside(window, selector);
            AssertHorizontallyInside(window, readiness);
            AssertRowsAreOrdered(window, messageLine, readinessLine);
            AssertVerticalRangesOverlap(window, message, send);
            AssertVerticalRangesOverlap(window, selector, readiness);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task BranchMismatchKeepsSelectorReachableAndSendDisabledAtMinimumViewport()
    {
        MainViewModel viewModel = UiTestMainViewModelFactory.CreateComposerScenario(branchMismatch: true);
        await viewModel.InitializeAsync().ConfigureAwait(true);
        viewModel.MessageDraft = "Do not send with the wrong branch model";

        MainWindow window = CreateConversationWindow(viewModel);
        try
        {
            ShowAndFlush(window);
            ResizeAndFlush(window, 720, 560);

            Border composer = FindRequired<Border>(window, "Message composer");
            Button send = FindRequired<Button>(composer, "Send message");
            Button selector = FindRequired<Button>(composer, "Choose model and generation profile");
            TextBlock readiness = FindRequired<TextBlock>(composer, "Branch model and profile readiness status");

            UiTestArtifactWriter.Capture(window, "conversation/composer-mismatch-720x560.png");

            Assert.True(viewModel.ShowMessageComposer);
            Assert.True(composer.IsEffectivelyVisible);
            Assert.True(selector.IsEffectivelyVisible);
            Assert.True(selector.IsEnabled);
            Assert.False(send.IsEnabled);
            Assert.False(viewModel.CanSendMessage);
            string readinessText = Assert.IsType<string>(readiness.Text);
            Assert.Contains("branch-model-GGUF:Q5_K_M", readinessText, StringComparison.Ordinal);
            AssertHorizontallyInside(window, selector);
            AssertHorizontallyInside(window, readiness);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task KeyboardTraversalFollowsMessageThenSendThenBranchSelector()
    {
        MainViewModel viewModel = UiTestMainViewModelFactory.CreateComposerScenario();
        await viewModel.InitializeAsync().ConfigureAwait(true);
        viewModel.MessageDraft = "Keyboard traversal";

        MainWindow window = CreateConversationWindow(viewModel);
        try
        {
            ShowAndFlush(window);
            ResizeAndFlush(window, 720, 560);

            Border composer = FindRequired<Border>(window, "Message composer");
            TextBox message = FindRequired<TextBox>(composer, "Message text");
            Button send = FindRequired<Button>(composer, "Send message");
            Button selector = FindRequired<Button>(composer, "Choose model and generation profile");

            Assert.True(message.Focus());
            Assert.True(message.IsFocused);

            PressAndRelease(window, Key.Tab, RawInputModifiers.None, PhysicalKey.Tab);
            Assert.True(send.IsFocused);

            PressAndRelease(window, Key.Tab, RawInputModifiers.None, PhysicalKey.Tab);
            UiTestArtifactWriter.Capture(window, "conversation/composer-keyboard-720x560.png");

            Assert.True(selector.IsFocused);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task GlobalConfigurationGateStillReplacesComposer()
    {
        MainViewModel viewModel = UiTestMainViewModelFactory.CreateConfigurationGateScenario();
        await viewModel.InitializeAsync().ConfigureAwait(true);

        MainWindow window = CreateConversationWindow(viewModel);
        try
        {
            ShowAndFlush(window);
            ResizeAndFlush(window, 900, 700);

            ConversationConfigurationGateView gate = FindVisibleRequired<ConversationConfigurationGateView>(
                window,
                "Local AI configuration gate");

            UiTestArtifactWriter.Capture(window, "conversation/configuration-gate-900x700.png");

            Assert.True(viewModel.ShowInlineConfigurationGate);
            Assert.False(viewModel.ShowMessageComposer);
            Assert.True(gate.IsEffectivelyVisible);
            AssertHorizontallyInside(window, gate);
        }
        finally
        {
            window.Close();
        }
    }

    private static MainWindow CreateConversationWindow(MainViewModel viewModel)
    {
        return new MainWindow
        {
            DataContext = new ShellViewModel(viewModel),
        };
    }

    private static void ShowAndFlush(Window window)
    {
        window.Show();
        Flush();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Flush();
    }

    private static void ResizeAndFlush(Window window, double width, double height)
    {
        window.Width = width;
        window.Height = height;
        Flush();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Flush();

        Assert.InRange(Math.Abs(window.ClientSize.Width - width), 0, 0.5);
        Assert.InRange(Math.Abs(window.ClientSize.Height - height), 0, 0.5);
    }

    private static void Flush()
    {
        Dispatcher.UIThread.RunJobs();
    }

    private static void PressAndRelease(
        Window window,
        Key key,
        RawInputModifiers modifiers,
        PhysicalKey physicalKey)
    {
        window.KeyPress(key, modifiers, physicalKey, null);
        window.KeyRelease(key, modifiers, physicalKey, null);
        Flush();
    }

    private static T FindRequired<T>(Visual root, string automationName)
        where T : Control
    {
        T? control = root
            .GetVisualDescendants()
            .OfType<T>()
            .SingleOrDefault(candidate => string.Equals(
                Avalonia.Automation.AutomationProperties.GetName(candidate),
                automationName,
                StringComparison.Ordinal));

        return Assert.IsType<T>(control);
    }

    private static T FindVisibleRequired<T>(Visual root, string automationName)
        where T : Control
    {
        T? control = root
            .GetVisualDescendants()
            .OfType<T>()
            .SingleOrDefault(candidate => candidate.IsEffectivelyVisible
                && string.Equals(
                    Avalonia.Automation.AutomationProperties.GetName(candidate),
                    automationName,
                    StringComparison.Ordinal));

        return Assert.IsType<T>(control);
    }

    private static void AssertHorizontallyInside(Window window, Control control)
    {
        Point? topLeft = control.TranslatePoint(new Point(0, 0), window);
        Assert.NotNull(topLeft);
        Assert.True(
            topLeft.Value.X >= -0.5,
            $"{control.GetType().Name} starts outside the viewport at X={topLeft.Value.X}.");
        Assert.True(
            topLeft.Value.X + control.Bounds.Width <= window.ClientSize.Width + 0.5,
            $"{control.GetType().Name} ends outside the viewport: X={topLeft.Value.X}, Width={control.Bounds.Width}, ClientWidth={window.ClientSize.Width}.");
    }

    private static void AssertRowsAreOrdered(Window window, Control first, Control second)
    {
        Point? firstTopLeft = first.TranslatePoint(new Point(0, 0), window);
        Point? secondTopLeft = second.TranslatePoint(new Point(0, 0), window);
        Assert.NotNull(firstTopLeft);
        Assert.NotNull(secondTopLeft);
        Assert.True(
            secondTopLeft.Value.Y >= firstTopLeft.Value.Y + first.Bounds.Height - 0.5,
            $"Second composer line starts before the first line ends: firstY={firstTopLeft.Value.Y}, firstHeight={first.Bounds.Height}, secondY={secondTopLeft.Value.Y}.");
    }

    private static void AssertVerticalRangesOverlap(Window window, Control left, Control right)
    {
        Point? leftTopLeft = left.TranslatePoint(new Point(0, 0), window);
        Point? rightTopLeft = right.TranslatePoint(new Point(0, 0), window);
        Assert.NotNull(leftTopLeft);
        Assert.NotNull(rightTopLeft);

        double overlapTop = Math.Max(leftTopLeft.Value.Y, rightTopLeft.Value.Y);
        double overlapBottom = Math.Min(
            leftTopLeft.Value.Y + left.Bounds.Height,
            rightTopLeft.Value.Y + right.Bounds.Height);
        Assert.True(
            overlapBottom >= overlapTop,
            $"Controls expected on the same semantic line do not vertically overlap: {left.GetType().Name} and {right.GetType().Name}.");
    }
}
