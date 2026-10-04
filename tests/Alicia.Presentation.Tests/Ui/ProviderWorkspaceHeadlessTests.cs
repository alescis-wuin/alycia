using Alicia.Application.Providers;
using Alicia.Presentation.Tests.ViewModels;
using Alicia.Presentation.ViewModels;
using Alicia.Presentation.Views;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Alicia.Presentation.Tests.Ui;

public sealed partial class ProviderWorkspaceHeadlessTests
{
    [AvaloniaTheory]
    [InlineData(720, 560)]
    [InlineData(900, 700)]
    [InlineData(1280, 820)]
    [InlineData(1600, 900)]
    public async Task CompactActionsAndInformationFitEachViewport(int width, int height)
    {
        StubInferenceProviderRuntime runtime = new(InferenceProviderState.Ready);
        MainViewModel vm = UiTestMainViewModelFactory.Create(runtime: runtime);
        await vm.InitializeAsync();
        MainWindow window = CreateWindow(vm);
        try
        {
            Show(window, width, height);
            ProviderWorkspaceView view = Find<ProviderWorkspaceView>(window, "Provider workspace");
            Grid rows = view.FindControl<Grid>("ProviderActionRows")!;
            Button[] actions = rows.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.Equal(4, actions.Length);
            Button primary = Find<Button>(view, "Provider lifecycle action");
            Button detect = Find<Button>(view, "Detect selected provider");
            Button update = Find<Button>(view, "Update provider to Alicia validated release");
            Button check = Find<Button>(view, "Check provider update");
            Assert.Equal("Start", primary.Content);
            Assert.False(update.IsEnabled);
            Assert.IsType<PathIcon>(detect.Content);
            Assert.IsType<PathIcon>(check.Content);
            Assert.NotEqual(ToolTip.GetTip(detect), ToolTip.GetTip(check));
            Assert.InRange(Math.Abs(detect.Bounds.Width - detect.Bounds.Height), 0, 0.1);
            Assert.Equal(Position(primary, view).X, Position(update, view).X);
            Assert.Equal(Position(detect, view).X, Position(check, view).X);
            Assert.True(Position(update, view).Y > Position(primary, view).Y);
            Border panel = view.FindControl<Border>("RuntimePanel")!;
            Expander maintenance = view.FindControl<Expander>("MaintenancePanel")!;
            Assert.InRange(Math.Abs(panel.Bounds.Width - maintenance.Bounds.Width), 0, 1);
            StackPanel commands = view.FindControl<StackPanel>("ProviderCommands")!;
            StackPanel information = view.FindControl<StackPanel>("ProviderInformation")!;
            if (width >= 1280)
            {
                Assert.True(Position(information, view).X > Position(commands, view).X + commands.Bounds.Width);
                Assert.Equal(Position(commands, view).Y, Position(information, view).Y);
            }
            else
            {
                Assert.True(Position(information, view).Y > Position(commands, view).Y + commands.Bounds.Height);
            }

            foreach (Button action in actions)
            {
                AssertHorizontalContainment(window, action);
            }

            string text = string.Join("\n", view.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.IsEffectivelyVisible).Select(t => t.Text));
            Assert.DoesNotContain("Enter a Hugging Face", text);
            Assert.DoesNotContain("Managed release not checked", text);
            Assert.DoesNotContain("Runtime updates", text);
            Assert.Equal(1, text.Split("llama.cpp CUDA", StringSplitOptions.None).Length - 1);
            Assert.Equal(0, runtime.CheckUpdateCount);
            Assert.Equal(0, runtime.InspectStorageCount);
            UiTestArtifactWriter.Capture(window, $"provider/ready-{width}x{height}.png");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(InferenceProviderState.Missing, "Install", "Start")]
    [InlineData(InferenceProviderState.Ready, "Start", "Stop")]
    [InlineData(InferenceProviderState.Running, "Stop", "Start")]
    public async Task PrimaryButtonDispatchesExactlyOneLifecycleAction(
        InferenceProviderState state, string before, string after)
    {
        StubInferenceProviderRuntime runtime = new(state);
        MainViewModel vm = UiTestMainViewModelFactory.Create(runtime: runtime);
        await vm.InitializeAsync();
        MainWindow window = CreateWindow(vm);
        try
        {
            Show(window, 1280, 820);
            Button action = Find<Button>(window, "Provider lifecycle action");
            Assert.Equal(before, action.Content);
            Assert.True(action.IsEnabled);
            await vm.ProviderPrimaryCommand.ExecuteAsync(null);
            Flush();
            Assert.Equal(after, action.Content);
            Assert.Equal(1, runtime.InstallCount + runtime.StartCount + runtime.StopCount);
            UiTestArtifactWriter.Capture(window, $"provider/after-{before.ToLowerInvariant()}-1280x820.png");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task UpdateRequiresCheckAndStoppedEngineAndShowsTargetOnce()
    {
        StubInferenceProviderRuntime runtime = new(InferenceProviderState.Running)
        {
            UpdateInfo = new InferenceProviderUpdateInfo("b10000", "b10435", "b10500", true, true, false, "Validated update available."),
        };
        MainViewModel vm = UiTestMainViewModelFactory.Create(runtime: runtime);
        await vm.InitializeAsync();
        MainWindow window = CreateWindow(vm);
        try
        {
            Show(window, 1280, 820);
            Button update = Find<Button>(window, "Update provider to Alicia validated release");
            Assert.False(update.IsEnabled);
            await vm.CheckProviderUpdateCommand.ExecuteAsync(null);
            Flush();
            Assert.Equal("Update available", Find<TextBlock>(window, "Provider update result").Text);
            Assert.Equal("b10435", Find<TextBlock>(window, "Validated update version").Text);
            Assert.False(update.IsEnabled);
            Assert.Contains("Stop", vm.ProviderUpdateHelp);
            await vm.ProviderPrimaryCommand.ExecuteAsync(null);
            Flush();
            Assert.True(update.IsEnabled);
            UiTestArtifactWriter.Capture(window, "provider/update-1280x820.png");
            await vm.UpdateProviderCommand.ExecuteAsync(null);
            Flush();
            Assert.False(update.IsEnabled);
            Assert.Equal(1, runtime.UpdateCount);
            Assert.False(Find<TextBlock>(window, "Validated update version").IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task StorageRefreshAndMaintenanceConfirmationRemainExplicit()
    {
        StubInferenceProviderRuntime runtime = new(InferenceProviderState.Ready);
        MainViewModel vm = UiTestMainViewModelFactory.Create(runtime: runtime);
        await vm.InitializeAsync();
        MainWindow window = CreateWindow(vm);
        try
        {
            Show(window, 1280, 820);
            Expander maintenance = Find<Expander>(window, "Managed provider maintenance");
            maintenance.IsExpanded = true;
            Flush();
            Button uninstall = Find<Button>(window, "Request managed runtime uninstall");
            Assert.False(uninstall.IsEnabled);
            Assert.Equal(0, runtime.InspectStorageCount);
            await vm.InspectProviderStorageCommand.ExecuteAsync(null);
            Flush();
            Assert.True(uninstall.IsEnabled);
            Assert.Equal($"{4.0:0.000} KiB", vm.Provider.WorkspaceRuntimeStorage);
            Assert.Equal(1, runtime.InspectStorageCount);
            vm.RequestUninstallProviderRuntimeCommand.Execute(null);
            Flush();
            Assert.True(vm.IsProviderMaintenanceConfirmationVisible);
            Assert.False(Find<Button>(window, "Provider lifecycle action").IsEnabled);
            Assert.Equal(0, runtime.UninstallCount);
            UiTestArtifactWriter.Capture(window, "provider/maintenance-1280x820.png");
            vm.CancelProviderMaintenanceCommand.Execute(null);
            Assert.Equal(0, runtime.UninstallCount);
            vm.RequestUninstallProviderRuntimeCommand.Execute(null);
            await vm.ConfirmProviderMaintenanceCommand.ExecuteAsync(null);
            Flush();
            Assert.Equal(1, runtime.UninstallCount);
            Assert.Equal("Install", Find<Button>(window, "Provider lifecycle action").Content);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("detect")]
    [InlineData("update")]
    [InlineData("storage")]
    public async Task OperationFailuresStayVisible(string operation)
    {
        StubInferenceProviderRuntime runtime = new(InferenceProviderState.Ready);
        MainViewModel vm = UiTestMainViewModelFactory.Create(runtime: runtime);
        await vm.InitializeAsync();
        MainWindow window = CreateWindow(vm);
        try
        {
            Show(window, 1280, 820);
            const string Failure = "Test operation failed; retry explicitly.";
            InferenceProviderException error = new(InferenceProviderFailureKind.Network, Failure);
            if (operation == "detect")
            {
                runtime.DetectException = error;
                await vm.DetectProviderCommand.ExecuteAsync(null);
            }
            else if (operation == "update")
            {
                runtime.CheckUpdateException = error;
                await vm.CheckProviderUpdateCommand.ExecuteAsync(null);
            }
            else
            {
                runtime.InspectStorageException = error;
                await vm.InspectProviderStorageCommand.ExecuteAsync(null);
            }

            Flush();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text?.Contains(Failure, StringComparison.Ordinal) == true);
            UiTestArtifactWriter.Capture(window, $"provider/failure-{operation}-1280x820.png");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task KeyboardAndResizePreserveFocusedActionAndOpenMaintenance()
    {
        MainViewModel vm = UiTestMainViewModelFactory.Create();
        await vm.InitializeAsync();
        MainWindow window = CreateWindow(vm);
        try
        {
            Show(window, 1600, 900);
            Button action = Find<Button>(window, "Provider lifecycle action");
            Button detect = Find<Button>(window, "Detect selected provider");
            Assert.True(action.Focus());
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Flush();
            Assert.True(detect.IsFocused);
            Expander maintenance = Find<Expander>(window, "Managed provider maintenance");
            maintenance.IsExpanded = true;
            Show(window, 720, 560);
            Assert.True(detect.IsFocused);
            Assert.True(maintenance.IsExpanded);
            AssertHorizontalContainment(window, maintenance);
            Show(window, 1600, 900);
            Assert.True(detect.IsFocused);
            Assert.True(maintenance.IsExpanded);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task UnsavedModelDisablesStartAndExplainsHowToProceed()
    {
        MainViewModel vm = UiTestMainViewModelFactory.Create();
        await vm.InitializeAsync();
        MainWindow window = CreateWindow(vm);
        try
        {
            Show(window, 900, 700);
            vm.ProviderModelReference = "owner/unsaved-model";
            Flush();
            Button action = Find<Button>(window, "Provider lifecycle action");
            Assert.False(action.IsEnabled);
            Assert.Contains("Models", AutomationProperties.GetHelpText(action));
        }
        finally
        {
            window.Close();
        }
    }

    private static MainWindow CreateWindow(MainViewModel vm)
    {
        ShellViewModel shell = new(vm);
        shell.NavigateToProvidersCommand.Execute(null);
        return new MainWindow { DataContext = shell };
    }

    private static void Show(Window window, int width, int height)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        window.Width = width;
        window.Height = height;
        Flush();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Flush();
    }

    private static void Flush() => Dispatcher.UIThread.RunJobs();

    private static T Find<T>(Visual root, string name) where T : Control => Assert.Single(
        root.GetVisualDescendants().OfType<T>(), c => AutomationProperties.GetName(c) == name);

    private static Point Position(Control control, Visual relative) => control.TranslatePoint(default, relative)!.Value;

    private static void AssertHorizontalContainment(Window window, Control control)
    {
        Point point = Position(control, window);
        Assert.InRange(point.X, -0.5, window.ClientSize.Width);
        Assert.True(point.X + control.Bounds.Width <= window.ClientSize.Width + 0.5);
    }
}
