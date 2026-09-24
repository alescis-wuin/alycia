using Alicia.Application.Providers;
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

public sealed class ModelsWorkspaceHeadlessTests
{
    [AvaloniaTheory]
    [InlineData(720, 560, "720x560")]
    [InlineData(900, 700, "900x700")]
    [InlineData(1280, 820, "1280x820")]
    [InlineData(1600, 900, "1600x900")]
    public async Task EmptyLibraryRendersAtDeterministicViewport(
        int width,
        int height,
        string snapshotName)
    {
        MainViewModel viewModel = UiTestMainViewModelFactory.Create();
        await viewModel.InitializeAsync().ConfigureAwait(true);

        MainWindow window = CreateModelsWindow(viewModel);
        ShowAndFlush(window);
        ResizeAndFlush(window, width, height);

        ModelWorkspaceView workspace = FindRequired<ModelWorkspaceView>(window, "Models workspace");
        TextBox modelReference = FindRequired<TextBox>(workspace, "Model reference loading draft");
        TextBox contextSize = FindRequired<TextBox>(workspace, "Context size loading draft");
        Button saveModelSettings = FindRequired<Button>(workspace, "Save model loading settings");
        Button saveToLibrary = FindRequired<Button>(workspace, "Save current model configuration to library");

        UiTestArtifactWriter.Capture(window, $"models/empty-{snapshotName}.png");

        Assert.True(workspace.IsEffectivelyVisible);
        Assert.True(modelReference.IsEffectivelyVisible);
        Assert.True(contextSize.IsEffectivelyVisible);
        Assert.True(saveModelSettings.IsEffectivelyVisible);
        Assert.True(saveToLibrary.IsEffectivelyVisible);
        Assert.False(viewModel.HasModelLibraryItems);
        Assert.True(viewModel.ShowModelLibraryEmptyState);
        Assert.False(viewModel.ShowSelectedModelLibraryDetail);

        AssertHorizontallyInside(window, workspace);
        AssertHorizontallyInside(window, modelReference);
        AssertHorizontallyInside(window, contextSize);
        AssertHorizontallyInside(window, saveModelSettings);
        AssertHorizontallyInside(window, saveToLibrary);

        window.Close();
    }

    [AvaloniaFact]
    public async Task KeyboardTraversalMovesForwardAndBackWithoutLosingFocus()
    {
        MainViewModel viewModel = UiTestMainViewModelFactory.Create();
        await viewModel.InitializeAsync().ConfigureAwait(true);

        MainWindow window = CreateModelsWindow(viewModel);
        ShowAndFlush(window);
        ResizeAndFlush(window, 720, 560);

        ModelWorkspaceView workspace = FindRequired<ModelWorkspaceView>(window, "Models workspace");
        TextBox modelReference = FindRequired<TextBox>(workspace, "Model reference loading draft");

        Assert.True(modelReference.Focus());
        Assert.True(modelReference.IsFocused);

        PressAndRelease(window, Key.Tab, RawInputModifiers.None, PhysicalKey.Tab);
        Assert.False(modelReference.IsFocused);
        Assert.Single(GetFocusedInputs(window));

        PressAndRelease(window, Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab);
        UiTestArtifactWriter.Capture(window, "models/keyboard-720x560.png");

        Assert.True(modelReference.IsFocused);
        Assert.Single(GetFocusedInputs(window));
        window.Close();
    }

    [AvaloniaFact]
    public async Task LibraryKeyboardSelectionAndUseActionApplyOnlySelectedLoadingSettings()
    {
        InferenceProviderConfiguration current = new(
            "llama.cpp.cuda",
            "owner/current-GGUF:Q4_K_M",
            contextSize: 2048);
        InferenceProviderConfiguration alternate = new(
            "llama.cpp.cuda",
            "owner/alternate-GGUF:Q6_K",
            contextSize: 4096);
        MainViewModel viewModel = UiTestMainViewModelFactory.Create(
            library: UiTestMainViewModelFactory.CreateLibrary(current, alternate),
            configuration: current);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        MainWindow window = CreateModelsWindow(viewModel);
        ShowAndFlush(window);
        ResizeAndFlush(window, 900, 700);

        ModelWorkspaceView workspace = FindRequired<ModelWorkspaceView>(window, "Models workspace");
        ListBox library = FindRequired<ListBox>(workspace, "Saved model library");
        Button useSettings = FindRequired<Button>(workspace, "Use selected library model loading settings");

        Assert.True(library.IsEffectivelyVisible);

        library.SelectedIndex = 0;
        Flush();
        ListBoxItem firstLibraryItem = Assert.IsType<ListBoxItem>(library.ContainerFromIndex(0));
        Assert.True(firstLibraryItem.Focus());
        Assert.True(firstLibraryItem.IsFocused);
        PressAndRelease(window, Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown);
        Flush();

        Assert.Equal(1, library.SelectedIndex);
        Assert.Equal(alternate.ModelReference, viewModel.SelectedModelLibraryItem?.ModelReference);
        Assert.Equal(current.ModelReference, viewModel.ProviderModelReference);
        Assert.True(useSettings.IsEnabled);

        Assert.True(useSettings.Focus());
        PressAndRelease(window, Key.Space, RawInputModifiers.None, PhysicalKey.Space);
        UiTestArtifactWriter.Capture(window, "models/populated-use-settings-900x700.png");

        Assert.Equal(alternate.ModelReference, viewModel.ProviderModelReference);
        Assert.Equal("4096", viewModel.ProviderContextSizeText);
        window.Close();
    }

    [AvaloniaFact]
    public async Task MinimumViewportDetailPaneScrollsWithoutHorizontalOverflow()
    {
        MainViewModel viewModel = UiTestMainViewModelFactory.Create();
        await viewModel.InitializeAsync().ConfigureAwait(true);

        MainWindow window = CreateModelsWindow(viewModel);
        ShowAndFlush(window);
        ResizeAndFlush(window, 720, 560);

        ModelWorkspaceView workspace = FindRequired<ModelWorkspaceView>(window, "Models workspace");
        TextBlock providerRuntimeHeading = workspace
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(textBlock => string.Equals(
                textBlock.Text,
                "Provider runtime",
                StringComparison.Ordinal));
        ScrollViewer detailScroller = providerRuntimeHeading
            .GetVisualAncestors()
            .OfType<ScrollViewer>()
            .First();
        Vector before = detailScroller.Offset;

        Point? origin = detailScroller.TranslatePoint(new Point(0, 0), window);
        Assert.NotNull(origin);
        Point wheelPoint = new(
            origin.Value.X + Math.Max(8, detailScroller.Bounds.Width / 2),
            origin.Value.Y + Math.Max(8, Math.Min(detailScroller.Bounds.Height / 2, 120)));
        window.MouseWheel(wheelPoint, new Vector(0, -6), RawInputModifiers.None);
        Flush();
        UiTestArtifactWriter.Capture(window, "models/scrolled-720x560.png");

        Assert.True(detailScroller.Extent.Height > detailScroller.Viewport.Height);
        Assert.True(detailScroller.Offset.Y > before.Y);
        Assert.InRange(Math.Abs(detailScroller.Offset.X), 0, 0.001);
        AssertHorizontallyInside(window, detailScroller);
        window.Close();
    }

    [AvaloniaFact]
    public async Task RunningProviderKeepsDraftControlsDisabledInRenderedWorkspace()
    {
        MainViewModel viewModel = UiTestMainViewModelFactory.Create(
            providerState: InferenceProviderState.Running);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        MainWindow window = CreateModelsWindow(viewModel);
        ShowAndFlush(window);
        ResizeAndFlush(window, 900, 700);

        ModelWorkspaceView workspace = FindRequired<ModelWorkspaceView>(window, "Models workspace");
        TextBox modelReference = FindRequired<TextBox>(workspace, "Model reference loading draft");
        Button saveModelSettings = FindRequired<Button>(workspace, "Save model loading settings");

        UiTestArtifactWriter.Capture(window, "models/provider-running-900x700.png");

        Assert.False(modelReference.IsEnabled);
        Assert.False(saveModelSettings.IsEnabled);
        Assert.Equal("Running", viewModel.ProviderStatusText);
        window.Close();
    }

    private static MainWindow CreateModelsWindow(MainViewModel viewModel)
    {
        ShellViewModel shell = new(viewModel);
        shell.NavigateToModelsCommand.Execute(null);

        return new MainWindow
        {
            DataContext = shell,
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

    private static InputElement[] GetFocusedInputs(Visual root)
    {
        return root
            .GetVisualDescendants()
            .OfType<InputElement>()
            .Where(element => element.IsFocused)
            .ToArray();
    }

    private static void AssertHorizontallyInside(Window window, Control control)
    {
        Point? topLeft = control.TranslatePoint(new Point(0, 0), window);
        Assert.NotNull(topLeft);
        Assert.True(topLeft.Value.X >= -0.5, $"{control.GetType().Name} starts outside the viewport at X={topLeft.Value.X}.");
        Assert.True(
            topLeft.Value.X + control.Bounds.Width <= window.ClientSize.Width + 0.5,
            $"{control.GetType().Name} ends outside the viewport: X={topLeft.Value.X}, Width={control.Bounds.Width}, ClientWidth={window.ClientSize.Width}.");
    }
}
