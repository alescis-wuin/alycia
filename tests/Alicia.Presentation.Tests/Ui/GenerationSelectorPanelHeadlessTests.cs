using Alicia.Application.Conversations;
using Alicia.Application.Generations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;
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

public sealed class GenerationSelectorPanelHeadlessTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 2, 14, 0, 0, TimeSpan.Zero);
    private static readonly string[] _compactTexts = ["Models", "Profiles", "alternate-model-GGUF", "owner", "model-GGUF", "owner", "Default"];
    private static readonly string[] _columnTexts = ["Models", "Profiles"];
    private static readonly string[] _iconActions = ["About model selection", "About profile selection", "Close model and profile selector", "Use selected model and profile for active branch"];
    private const string CurrentModel = "owner/model-GGUF:Q4_K_M";
    private const string AlternateModel = "owner/alternate-model-GGUF:Q5_K_M";

    [AvaloniaTheory]
    [InlineData(720, 560)]
    [InlineData(900, 700)]
    [InlineData(1280, 820)]
    [InlineData(1600, 900)]
    public async Task PanelIsContainedAndRestoresTheBranchAtEachViewport(int width, int height)
    {
        Scenario scenario = CreateScenario();
        await scenario.ViewModel.InitializeAsync();
        MainWindow window = CreateWindow(scenario.ViewModel);
        try
        {
            Show(window, width, height);
            Open(window, scenario.ViewModel);
            GenerationDualSelectorView panel = Find<GenerationDualSelectorView>(window, "Model and generation profile selector");
            ListBox models = Find<ListBox>(panel, "Saved models");
            ListBox profiles = Find<ListBox>(panel, "Confirmed generation profiles");
            Button apply = Find<Button>(panel, "Use selected model and profile for active branch");

            Assert.True(models.IsKeyboardFocusWithin);
            Assert.Equal(CurrentModel, scenario.ViewModel.GenerationDualSelector.SelectedModel?.ModelReference);
            Assert.True(scenario.ViewModel.GenerationDualSelector.SelectedProfile?.IsDefault);
            Assert.False(apply.IsEnabled);
            AssertContained(window, panel);
            AssertContained(window, models);
            AssertContained(window, profiles);
            AssertContained(window, apply);
            Assert.True(models.Bounds.Height > 150);
            Assert.True(profiles.Bounds.Height > 150);
            Assert.InRange(panel.Bounds.Width, 519, 521);
            Assert.InRange(panel.Bounds.Height, 100, 340);
            Button trigger = Find<Button>(window, "Choose model and generation profile");
            Point panelPosition = panel.TranslatePoint(default, window)!.Value;
            Point triggerPosition = trigger.TranslatePoint(default, window)!.Value;
            Assert.InRange(Math.Abs(panelPosition.X - triggerPosition.X), 0, 1);
            Assert.InRange(triggerPosition.Y - panelPosition.Y - panel.Bounds.Height, 7, 9);
            UiTestArtifactWriter.Capture(window, $"selector/panel-ready-{width}x{height}.png");
            AssertNoWrites(scenario);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SmallCatalogUsesOnlyNamesAuthorAndIcons()
    {
        Scenario scenario = CreateScenario(boundOutsideLibrary: true);
        await scenario.ViewModel.InitializeAsync();
        MainWindow window = CreateWindow(scenario.ViewModel);
        try
        {
            Show(window, 1600, 900);
            Open(window, scenario.ViewModel);
            GenerationDualSelectorView panel = Find<GenerationDualSelectorView>(window, "Model and generation profile selector");
            string?[] visible = panel.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToArray();
            Assert.Equal(_compactTexts, visible);
            Assert.InRange(panel.Bounds.Height, 100, 240);
            Assert.Single(panel.GetVisualDescendants().OfType<PathIcon>(),
                icon => icon.IsEffectivelyVisible && AutomationProperties.GetName(icon) == "Model used by active branch");
            Assert.Single(panel.GetVisualDescendants().OfType<PathIcon>(),
                icon => icon.IsEffectivelyVisible && AutomationProperties.GetName(icon) == "Profile used by active branch");
            foreach (string name in _iconActions)
            {
                Button button = Find<Button>(panel, name);
                Assert.IsType<PathIcon>(button.Content);
                Assert.Equal(button.Bounds.Width, button.Bounds.Height);
                Assert.Equal(button.Bounds.Width / 2, button.CornerRadius.TopLeft);
                Assert.NotNull(ToolTip.GetTip(button));
            }
            UiTestArtifactWriter.Capture(window, "selector/compact-1600x900.png");
            AssertNoWrites(scenario);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("Escape")]
    [InlineData("Close")]
    [InlineData("Backdrop")]
    public async Task CancellationDiscardsPreviewAndReturnsFocusWithoutWrites(string method)
    {
        Scenario scenario = CreateScenario();
        await scenario.ViewModel.InitializeAsync();
        scenario.ViewModel.MessageDraft = "Keep this draft";
        MainWindow window = CreateWindow(scenario.ViewModel);
        try
        {
            Show(window, 1280, 820);
            Open(window, scenario.ViewModel);
            ListBox models = Find<ListBox>(window, "Saved models");
            models.SelectedItem = scenario.ViewModel.GenerationDualSelector.Models.Single(item => item.ModelReference == AlternateModel);
            Flush();
            Assert.True(scenario.ViewModel.CanApplyGenerationDualSelector);
            if (method == "Escape")
            {
                Press(window, Key.Escape, PhysicalKey.Escape);
            }
            else
            {
                string name = method == "Close"
                    ? "Close model and profile selector"
                    : "Close model and profile selector backdrop";
                Find<Button>(window, name).Command!.Execute(null);
                Flush();
            }

            Assert.False(scenario.ViewModel.IsGenerationDualSelectorVisible);
            Assert.Equal("Keep this draft", scenario.ViewModel.MessageDraft);
            Assert.True(Find<Button>(window, "Choose model and generation profile").IsFocused);
            AssertNoWrites(scenario);
            Open(window, scenario.ViewModel);
            Assert.Equal(CurrentModel, scenario.ViewModel.GenerationDualSelector.SelectedModel?.ModelReference);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task KeyboardCyclesWithinPanelAndRestoresComposerFocus()
    {
        Scenario scenario = CreateScenario();
        await scenario.ViewModel.InitializeAsync();
        MainWindow window = CreateWindow(scenario.ViewModel);
        try
        {
            Show(window, 720, 560);
            Open(window, scenario.ViewModel);
            ListBox models = Find<ListBox>(window, "Saved models");
            ListBox profiles = Find<ListBox>(window, "Confirmed generation profiles");
            Button close = Find<Button>(window, "Close model and profile selector");
            Button apply = Find<Button>(window, "Use selected model and profile for active branch");
            Press(window, Key.Down, PhysicalKey.ArrowDown);
            Assert.Equal(AlternateModel, scenario.ViewModel.GenerationDualSelector.SelectedModel?.ModelReference);
            Press(window, Key.Tab, PhysicalKey.Tab);
            Assert.True(profiles.IsKeyboardFocusWithin);
            Press(window, Key.Tab, PhysicalKey.Tab);
            Assert.True(apply.IsFocused);
            Press(window, Key.Tab, PhysicalKey.Tab);
            Assert.True(Find<Button>(window, "About model selection").IsFocused);
            Press(window, Key.Tab, PhysicalKey.Tab);
            Assert.True(Find<Button>(window, "About profile selection").IsFocused);
            Press(window, Key.Tab, PhysicalKey.Tab);
            Assert.True(close.IsFocused);
            Press(window, Key.Tab, PhysicalKey.Tab);
            Assert.True(models.IsKeyboardFocusWithin);
            Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Shift);
            Assert.True(close.IsFocused);
            UiTestArtifactWriter.Capture(window, "selector/panel-keyboard-720x560.png");
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.True(Find<Button>(window, "Choose model and generation profile").IsFocused);
            AssertNoWrites(scenario);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ApplyPersistsOnlyTheBranchPairAndMissingDefaultCatalog()
    {
        Scenario scenario = CreateScenario();
        await scenario.ViewModel.InitializeAsync();
        scenario.ViewModel.MessageDraft = "Do not use the loaded model";
        MainWindow window = CreateWindow(scenario.ViewModel);
        try
        {
            Show(window, 900, 700);
            Open(window, scenario.ViewModel);
            Find<ListBox>(window, "Saved models").SelectedItem =
                scenario.ViewModel.GenerationDualSelector.Models.Single(item => item.ModelReference == AlternateModel);
            Flush();
            Find<Button>(window, "Use selected model and profile for active branch").Focus();
            Press(window, Key.Enter, PhysicalKey.Enter);
            Task? apply = scenario.ViewModel.ApplyGenerationDualSelectorCommand.ExecutionTask;
            Assert.NotNull(apply);
            await apply;
            Flush();

            Assert.False(scenario.ViewModel.IsGenerationDualSelectorVisible);
            Assert.Equal(1, scenario.Selections.SaveCount);
            Assert.Equal(scenario.FirstConversation.Id, scenario.Selections.LastSavedSelection?.ConversationId);
            Assert.Equal(scenario.FirstConversation.ActiveBranchId, scenario.Selections.LastSavedSelection?.BranchId);
            Assert.Equal(AlternateModel, scenario.Selections.LastSavedSelection?.ModelScope.ModelReference);
            Assert.Equal(1, scenario.Profiles.SaveCount);
            Assert.True(scenario.Profiles.LastSavedCatalog?.DefaultProfile.IsDefault);
            Assert.Equal("Do not use the loaded model", scenario.ViewModel.MessageDraft);
            Assert.False(scenario.ViewModel.CanSendMessage);
            Assert.False(Find<Button>(window, "Send message").IsEnabled);
            Assert.Contains("This branch expects", scenario.ViewModel.GenerationDualSelectorReadinessText);
            Assert.True(Find<Button>(window, "Choose model and generation profile").IsFocused);
            AssertProviderAndLibraryUnchanged(scenario);
            Assert.Equal(0, scenario.Responder.CallCount);
            UiTestArtifactWriter.Capture(window, "selector/confirmed-mismatch-900x700.png");

            Open(window, scenario.ViewModel);
            Assert.Equal(AlternateModel, scenario.ViewModel.GenerationDualSelector.SelectedModel?.ModelReference);
            Assert.False(scenario.ViewModel.CanApplyGenerationDualSelector);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CustomConfirmedProfileKeepsItsWorkingDraftAfterApply()
    {
        Scenario scenario = CreateScenario();
        await scenario.ViewModel.InitializeAsync();
        MainWindow window = CreateWindow(scenario.ViewModel);
        try
        {
            Show(window, 900, 700);
            Open(window, scenario.ViewModel);
            GenerationDualSelectorProfileItemViewModel profile = scenario.ViewModel.GenerationDualSelector.Profiles.Single(item => item.HasWorkingDraft);
            Find<ListBox>(window, "Confirmed generation profiles").SelectedItem = profile;
            Flush();
            Assert.False(profile.IsDefault);
            Assert.Contains("local draft exists", profile.StateText);
            UiTestArtifactWriter.Capture(window, "selector/custom-working-draft-900x700.png");
            await scenario.ViewModel.ApplyGenerationDualSelectorCommand.ExecuteAsync(null);
            Flush();

            Assert.Equal(profile.Id, scenario.Selections.LastSavedSelection?.ProfileId);
            Assert.Equal(0, scenario.Profiles.SaveCount);
            GenerationProfileCatalog? catalog = await scenario.Profiles.LoadAsync(
                new GenerationProfileModelScope("llama.cpp.cuda", CurrentModel));
            Assert.NotNull(catalog?.FindWorkingDraft(profile.Id));
            Assert.Equal("Confirmed instructions", catalog?.FindProfile(profile.Id)?.BaseSystemInstructions);
            Assert.Equal("Unconfirmed local instructions", catalog?.FindWorkingDraft(profile.Id)?.Profile.BaseSystemInstructions);
            AssertProviderAndLibraryUnchanged(scenario);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CompatibilityModelsRemainVisibleWithoutLibraryImport()
    {
        Scenario scenario = CreateScenario(boundOutsideLibrary: true);
        await scenario.ViewModel.InitializeAsync();
        MainWindow window = CreateWindow(scenario.ViewModel);
        try
        {
            Show(window, 900, 700);
            Open(window, scenario.ViewModel);
            Assert.Equal(2, scenario.ViewModel.GenerationDualSelector.Models.Count);
            Assert.Equal(AlternateModel, scenario.ViewModel.GenerationDualSelector.SelectedModel?.ModelReference);
            Assert.All(scenario.ViewModel.GenerationDualSelector.Models, item => Assert.False(item.IsSavedInLibrary));
            Assert.False(scenario.ViewModel.CanSendMessage);
            AssertNoWrites(scenario);
            UiTestArtifactWriter.Capture(window, "selector/compatibility-900x700.png");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task EmptyModelsShowGuidanceAndFocusCloseWithoutCreatingProfiles()
    {
        Scenario scenario = CreateScenario(empty: true);
        await scenario.ViewModel.InitializeAsync();
        MainWindow window = CreateWindow(scenario.ViewModel);
        try
        {
            Show(window, 720, 560);
            scenario.ViewModel.OpenGenerationDualSelectorCommand.Execute(null);
            Flush();
            Assert.True(scenario.ViewModel.IsGenerationDualSelectorVisible);
            Assert.True(Find<Button>(window, "Close model and profile selector").IsFocused);
            Assert.Contains("If this list is empty", ToolTip.GetTip(Find<Button>(window, "About model selection"))?.ToString());
            Assert.Equal(_columnTexts,
                Find<GenerationDualSelectorView>(window, "Model and generation profile selector")
                    .GetVisualDescendants().OfType<TextBlock>()
                    .Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToArray());
            Assert.False(Find<Button>(window, "Use selected model and profile for active branch").IsEnabled);
            AssertNoWrites(scenario);
            UiTestArtifactWriter.Capture(window, "selector/empty-720x560.png");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ConversationChangeClosesPreviewAndRestoresTheNewBranch()
    {
        Scenario scenario = CreateScenario();
        await scenario.ViewModel.InitializeAsync();
        MainWindow window = CreateWindow(scenario.ViewModel);
        try
        {
            Show(window, 1280, 820);
            Open(window, scenario.ViewModel);
            scenario.ViewModel.GenerationDualSelector.SelectedModel =
                scenario.ViewModel.GenerationDualSelector.Models.Single(item => item.ModelReference == AlternateModel);
            await scenario.ViewModel.Conversations.Single(item => item.Id == scenario.SecondConversation.Id)
                .SelectCommand.ExecuteAsync(null);
            Flush();
            Assert.False(scenario.ViewModel.IsGenerationDualSelectorVisible);
            Assert.Equal(scenario.SecondConversation.Id, scenario.ViewModel.SelectedConversation?.Id);
            Open(window, scenario.ViewModel);
            Assert.Equal(CurrentModel, scenario.ViewModel.GenerationDualSelector.SelectedModel?.ModelReference);
            Assert.Equal("Code review profile 1", scenario.ViewModel.GenerationDualSelector.SelectedProfile?.Name);
            AssertNoWrites(scenario);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task LeavingConversationCancelsPreviewWithoutStealingWorkspaceFocus()
    {
        Scenario scenario = CreateScenario();
        await scenario.ViewModel.InitializeAsync();
        ShellViewModel shell = new(scenario.ViewModel);
        MainWindow window = new() { DataContext = shell };
        try
        {
            Show(window, 900, 700);
            Open(window, scenario.ViewModel);
            scenario.ViewModel.GenerationDualSelector.SelectedModel =
                scenario.ViewModel.GenerationDualSelector.Models.Single(item => item.ModelReference == AlternateModel);
            shell.NavigateToModelsCommand.Execute(null);
            Flush();
            Assert.False(scenario.ViewModel.IsGenerationDualSelectorVisible);
            Assert.True(shell.IsModelsSelected);
            Assert.False(Find<Button>(window, "Choose model and generation profile").IsFocused);
            shell.NavigateToConversationsCommand.Execute(null);
            Flush();
            Open(window, scenario.ViewModel);
            Assert.Equal(CurrentModel, scenario.ViewModel.GenerationDualSelector.SelectedModel?.ModelReference);
            AssertNoWrites(scenario);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ResizeKeepsTheOpenPreviewAndContainsLongLabels()
    {
        Scenario scenario = CreateScenario(longLabels: true);
        await scenario.ViewModel.InitializeAsync();
        MainWindow window = CreateWindow(scenario.ViewModel);
        try
        {
            Show(window, 1600, 900);
            Open(window, scenario.ViewModel);
            ListBox models = Find<ListBox>(window, "Saved models");
            object preview = scenario.ViewModel.GenerationDualSelector.Models.Single(item => item.ModelReference.Contains("long-model"));
            models.SelectedItem = preview;
            Flush();
            Show(window, 720, 560);
            models.ScrollIntoView(preview);
            Flush();
            GenerationDualSelectorView panel = Find<GenerationDualSelectorView>(window, "Model and generation profile selector");
            Assert.Same(preview, scenario.ViewModel.GenerationDualSelector.SelectedModel);
            AssertContained(window, panel);
            foreach (TextBlock text in panel.GetVisualDescendants().OfType<TextBlock>()
                .Where(item => item.IsEffectivelyVisible && item.Bounds.Width > 0))
            {
                AssertHorizontallyContained(panel, text);
            }
            AssertContained(window, Find<Button>(panel, "Use selected model and profile for active branch"));
            UiTestArtifactWriter.Capture(window, "selector/long-labels-720x560.png");
            AssertNoWrites(scenario);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ModelAndProfileListsScrollIndependently()
    {
        Scenario scenario = CreateScenario();
        await scenario.ViewModel.InitializeAsync();
        MainWindow window = CreateWindow(scenario.ViewModel);
        try
        {
            Show(window, 720, 560);
            Open(window, scenario.ViewModel);
            ListBox models = Find<ListBox>(window, "Saved models");
            ListBox profiles = Find<ListBox>(window, "Confirmed generation profiles");
            ScrollViewer modelScroll = models.GetVisualDescendants().OfType<ScrollViewer>().Single();
            ScrollViewer profileScroll = profiles.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.True(modelScroll.Extent.Height > modelScroll.Viewport.Height);
            Assert.True(profileScroll.Extent.Height > profileScroll.Viewport.Height);
            Assert.InRange(modelScroll.Extent.Width - modelScroll.Viewport.Width, -1, 1);
            Assert.InRange(profileScroll.Extent.Width - profileScroll.Viewport.Width, -1, 1);
            window.MouseWheel(models.TranslatePoint(new Point(50, 60), window)!.Value, new Vector(0, -4));
            Flush();
            Assert.True(modelScroll.Offset.Y > 0);
            Assert.Equal(0, profileScroll.Offset.Y);
            window.MouseWheel(profiles.TranslatePoint(new Point(50, 60), window)!.Value, new Vector(0, -4));
            Flush();
            Assert.True(profileScroll.Offset.Y > 0);
            AssertNoWrites(scenario);
            UiTestArtifactWriter.Capture(window, "selector/scrolled-720x560.png");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ActiveGenerationKeepsSelectorUnavailable()
    {
        CancellableConversationResponder responder = new();
        Scenario scenario = CreateScenario(streamingResponder: responder);
        await scenario.ViewModel.InitializeAsync();
        MainWindow window = CreateWindow(scenario.ViewModel);
        Task? send = null;
        try
        {
            Show(window, 900, 700);
            scenario.ViewModel.MessageDraft = "Keep streaming";
            send = scenario.ViewModel.SendMessageCommand.ExecuteAsync(null);
            await responder.Started.WaitAsync(TimeSpan.FromSeconds(5));
            Flush();
            Assert.True(scenario.ViewModel.IsGeneratingResponse);
            Assert.False(Find<Button>(window, "Choose model and generation profile").IsEnabled);
            scenario.ViewModel.OpenGenerationDualSelectorCommand.Execute(null);
            Flush();
            Assert.False(scenario.ViewModel.IsGenerationDualSelectorVisible);
            AssertNoWrites(scenario);
        }
        finally
        {
            scenario.ViewModel.StopResponseCommand.Execute(null);
            if (send is not null)
            {
                await send.WaitAsync(TimeSpan.FromSeconds(5));
            }
            window.Close();
        }
    }

    private static Scenario CreateScenario(
        bool empty = false,
        bool boundOutsideLibrary = false,
        bool longLabels = false,
        IStreamingConversationResponder? streamingResponder = null)
    {
        InMemoryConversationRepository repository = new();
        Conversation first = new(ConversationId.New(), "Selection panel conversation", _now, _now);
        Conversation second = new(ConversationId.New(), "Other branch selection", _now.AddMinutes(-1), _now.AddMinutes(-1));
        repository.Seed(first);
        repository.Seed(second);
        StubInferenceProviderConfigurationStore configuration = empty
            ? new()
            : new(new InferenceProviderConfiguration("llama.cpp.cuda", CurrentModel, 2048));
        StubInferenceProviderRuntime runtime = new(empty ? InferenceProviderState.Ready : InferenceProviderState.Running);
        InMemoryGenerationProfileCatalogStore profiles = new();
        InMemoryConversationGenerationSelectionStore selections = new();
        List<InferenceModelLibraryEntry> entries = [];
        if (!empty)
        {
            GenerationProfileModelScope scope = new("llama.cpp.cuda", CurrentModel);
            GenerationProfile defaultProfile = GenerationProfile.CreateDefault(GenerationProfileId.New());
            List<GenerationProfileRevision> revisions = [];
            GenerationProfileWorkingDraft? workingDraft = null;
            for (int index = 0; index < 12; index++)
            {
                GenerationProfile profile = new(
                    GenerationProfileId.New(),
                    $"Code review profile {index + 1}",
                    "Confirmed instructions");
                GenerationProfileRevision revision = new(
                    GenerationProfileRevisionId.New(), null, _now, profile);
                revisions.Add(revision);
                if (index == 2)
                {
                    workingDraft = new GenerationProfileWorkingDraft(
                        new GenerationProfile(profile.Id, profile.Name, "Unconfirmed local instructions"),
                        revision.Id, _now.AddMinutes(1));
                }
            }
            profiles.Seed(new GenerationProfileCatalog(scope, defaultProfile, revisions, [workingDraft!]));
            GenerationProfileModelScope alternateScope = new("llama.cpp.cuda", AlternateModel);
            GenerationProfileCatalog alternateCatalog = GenerationProfileCatalog.CreateEmpty(alternateScope, GenerationProfileId.New());
            selections.Seed(new ConversationGenerationSelection(
                first.Id, first.ActiveBranchId,
                boundOutsideLibrary ? alternateScope : scope,
                boundOutsideLibrary ? alternateCatalog.DefaultProfile.Id : defaultProfile.Id));
            selections.Seed(new ConversationGenerationSelection(
                second.Id, second.ActiveBranchId, scope, revisions[0].Profile.Id));
            if (boundOutsideLibrary)
            {
                profiles.Seed(alternateCatalog);
            }
            else
            {
                entries.Add(new InferenceModelLibraryEntry(new InferenceProviderConfiguration("llama.cpp.cuda", CurrentModel, 2048), _now));
                entries.Add(new InferenceModelLibraryEntry(new InferenceProviderConfiguration("llama.cpp.cuda", AlternateModel, 4096), _now));
                for (int index = 0; index < 18; index++)
                {
                    entries.Add(new InferenceModelLibraryEntry(
                        new InferenceProviderConfiguration("llama.cpp.cuda", $"owner/saved-model-{index:D2}-GGUF:Q4_K_M"), _now));
                }
            }
            if (longLabels)
            {
                string reference = "very-long-owner/long-model-with-a-very-detailed-name-for-development-and-scientific-writing-GGUF:Q5_K_M";
                GenerationProfileModelScope longScope = new("llama.cpp.cuda", reference);
                GenerationProfile longProfile = new(GenerationProfileId.New(),
                    "A very detailed confirmed profile name for careful code review and scientific writing");
                profiles.Seed(new GenerationProfileCatalog(
                    longScope, GenerationProfile.CreateDefault(GenerationProfileId.New()),
                    [new GenerationProfileRevision(GenerationProfileRevisionId.New(), null, _now, longProfile)]));
                entries.Add(new InferenceModelLibraryEntry(new InferenceProviderConfiguration("llama.cpp.cuda", reference), _now));
            }
        }
        InMemoryInferenceModelLibraryStore library = new(new InferenceModelLibrary(entries));
        MutableTimeProvider time = new(_now);
        DeterministicConversationResponder responder = new("Headless panel response");
        MainViewModel viewModel = new(
            new CreateConversationUseCase(repository, time),
            new AppendMessageUseCase(repository, time),
            new StreamConversationTurnUseCase(repository, streamingResponder ?? responder, time),
            new LoadConversationUseCase(repository),
            new ListConversationsUseCase(repository),
            new RenameConversationUseCase(repository, time),
            new DeleteConversationUseCase(repository),
            new StubInferenceProviderRegistry(runtime), configuration,
            TimeSpan.Zero, TimeSpan.Zero,
            isReducedMotionEnabled: true,
            generationProfileCatalogStore: profiles,
            conversationGenerationSelectionStore: selections,
            generationProfileTimeProvider: time,
            generationProfileDraftAutosaveDelay: TimeSpan.Zero,
            inferenceModelLibraryStore: library);
        return new Scenario(viewModel, configuration, runtime, profiles, selections, library, first, second, responder);
    }

    private static MainWindow CreateWindow(MainViewModel viewModel) => new() { DataContext = new ShellViewModel(viewModel) };

    private static void Show(Window window, int width, int height)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }
        window.Width = width;
        window.Height = height;
        Flush();
    }

    private static void Open(Window window, MainViewModel viewModel)
    {
        Find<Button>(window, "Choose model and generation profile").Command!.Execute(null);
        Flush();
        Assert.True(viewModel.IsGenerationDualSelectorVisible);
    }

    private static void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Press(Window window, Key key, PhysicalKey physicalKey, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, physicalKey, null);
        window.KeyRelease(key, modifiers, physicalKey, null);
        Flush();
    }

    private static T Find<T>(Visual root, string name) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(), item =>
            Avalonia.Automation.AutomationProperties.GetName(item) == name);

    private static void AssertContained(Visual container, Control control)
    {
        Point point = control.TranslatePoint(default, container)!.Value;
        Assert.InRange(point.X, -0.5, container.Bounds.Width + 0.5);
        Assert.InRange(point.Y, -0.5, container.Bounds.Height + 0.5);
        Assert.True(point.X + control.Bounds.Width <= container.Bounds.Width + 0.5);
        Assert.True(point.Y + control.Bounds.Height <= container.Bounds.Height + 0.5);
    }

    private static void AssertHorizontallyContained(Visual container, Control control)
    {
        Point point = control.TranslatePoint(default, container)!.Value;
        Assert.InRange(point.X, -0.5, container.Bounds.Width + 0.5);
        Assert.True(point.X + control.Bounds.Width <= container.Bounds.Width + 0.5);
    }

    private static void AssertNoWrites(Scenario scenario)
    {
        Assert.Equal(0, scenario.Selections.SaveCount);
        Assert.Equal(0, scenario.Profiles.SaveCount);
        AssertProviderAndLibraryUnchanged(scenario);
    }

    private static void AssertProviderAndLibraryUnchanged(Scenario scenario)
    {
        Assert.Equal(0, scenario.Configuration.SaveCount);
        Assert.Equal(0, scenario.Library.SaveCount);
        Assert.Equal(0, scenario.Runtime.StartCount);
        Assert.Equal(0, scenario.Runtime.StopCount);
    }

    private sealed record Scenario(
        MainViewModel ViewModel,
        StubInferenceProviderConfigurationStore Configuration,
        StubInferenceProviderRuntime Runtime,
        InMemoryGenerationProfileCatalogStore Profiles,
        InMemoryConversationGenerationSelectionStore Selections,
        InMemoryInferenceModelLibraryStore Library,
        Conversation FirstConversation,
        Conversation SecondConversation,
        DeterministicConversationResponder Responder);
}
