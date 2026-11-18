using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace Alycia.Client.Avalonia;

public partial class ChatWindow
{
    private static readonly ProjectChoice Outside = new(null, "Hors projet");
    private ProjectChoice _project = Outside;
    private ProjectChoice[] _projects = [Outside];
    private Control? _inspectorOrigin;
    private ChatMessageItem? _evidence;
    private bool _choosingProject;

    private void InitializeNavigation()
    {
        C<Button>("ImportLimitsButton").Click += async (_, _) => await RunAsync(EditImportLimitsAsync);
        var selector = C<ComboBox>("ProjectSelector");
        selector.ItemsSource = _projects;
        selector.SelectedItem = Outside;
        selector.SelectionChanged += async (_, _) =>
        {
            if (_choosingProject || _busy || selector.SelectedItem is not ProjectChoice selected || selected.Id == _project.Id) return;
            await RunAsync(() => SwitchProjectAsync(selected));
        };
        C<Button>("AddProjectButton").Click += async (_, _) => await RunAsync(async () =>
        {
            var name = Input("", 100);
            name.PlaceholderText = "Nom du projet";
            if (!await EditDialogAsync("Créer un projet", name, "Créer", () => string.IsNullOrWhiteSpace(name.Text) ? "Saisissez un nom." : null)) return;
            var created = await _client.NewProjectAsync(name.Text!, _lifetime.Token);
            await RefreshProjectsAsync(created.Id);
            await SwitchProjectAsync(created);
        });
        C<Button>("FocusButton").Click += (_, _) => ToggleConcentration();
        C<Button>("MoveButton").Click += async (_, _) => await RunAsync(MoveConversationAsync);
        C<Button>("MoreButton").Click += (_, _) =>
        {
            var menu = C<Border>("ActionMenu");
            menu.IsVisible = !menu.IsVisible;
            if (menu.IsVisible) C<TextBox>("TitleInput").Focus();
        };
        C<Button>("AppearanceButton").Click += (_, _) => ShowPreferences();
        C<Button>("SaveAppearanceButton").Click += async (_, _) =>
        {
            if (!_busy) { await RunAsync(SaveAppearanceAsync); return; }
            if (_generation is not null)
            {
                if ((C<TextBox>("ApiUrlInput").Text?.Trim() ?? "") != Appearance.Current.ApiUrl)
                { Feedback("Terminez la génération avant de changer de connexion.", true); return; }
                try { await SaveAppearanceAsync(); }
                catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or ChatApiException) { Feedback(e.Message, true); }
            }
        };
        C<Button>("BackButton").Click += (_, _) => ShowChat();
        C<Button>("CloseInspector").Click += (_, _) => HideInspector();
        C<Button>("ContextTab").Click += async (_, _) => await RunAsync(() => OpenInspectorAsync("context"));
        C<Button>("SourcesTab").Click += async (_, _) => await RunAsync(() => OpenInspectorAsync("sources"));
        C<Button>("MemoryTab").Click += async (_, _) => await RunAsync(() => OpenInspectorAsync("memory"));
        C<Button>("InstructionsButton").Click += async (_, _) => await RunAsync(ProjectPreferencesAsync);
        C<ComboBox>("ThemeChoice").ItemsSource = new[] { "Système", "Aube", "Nuit" };
        C<ComboBox>("SizeChoice").ItemsSource = new[] { 14, 16, 17, 18, 20, 24, 28, 32, 36 };
    }

    private bool _concentrating;
    private bool _sidebarBeforeFocus;
    private void ToggleConcentration()
    {
        var side = C<Border>("Sidebar");
        if (!_concentrating) _sidebarBeforeFocus = side.IsVisible;
        _concentrating = !_concentrating;
        _sidebarManual = true;
        side.IsVisible = !_concentrating && _sidebarBeforeFocus;
        HideInspector();
        ShowChat();
        C<Button>("FocusButton").Content = _concentrating ? "Quitter la concentration" : "Mode concentration";
        C<TextBox>("MessageInput").Focus();
    }

    private async Task RefreshProjectsAsync(Guid? selectedId)
    {
        var choices = await _client.ProjectsAsync(_lifetime.Token);
        _projects = new[] { Outside }.Concat(choices).ToArray();
        _project = _projects.FirstOrDefault(p => p.Id == selectedId) ?? Outside;
        _choosingProject = true;
        try
        {
            var selector = C<ComboBox>("ProjectSelector");
            selector.ItemsSource = _projects;
            selector.SelectedItem = _project;
            C<TextBlock>("ProjectHeading").Text = _project.ToString();
        }
        finally { _choosingProject = false; }
    }

    private async Task SwitchProjectAsync(ProjectChoice selection)
    {
        _indexingOwner = selection.Id; _indexingProject = true;
        SaveDraft();
        _current = null;
        _messages.Clear();
        C<ListBox>("ConversationList").ItemsSource = Array.Empty<ConversationSummary>();
        C<TextBox>("MessageInput").Text = "";
        C<TextBlock>("ViewHeading").Text = "Conversation";
        C<Button>("ActiveModelButton").Content = "";
        C<Border>("PendingUserPanel").IsVisible = false;
        C<Expander>("StreamingPanel").IsVisible = false;
        _project = selection;
        _evidence = null;
        C<TextBlock>("ProjectHeading").Text = selection.ToString();
        C<TextBox>("SearchInput").Text = "";
        HideInspector();
        await RefreshConversationsAsync(null);
        if (_current is not null) await LoadAsync(_current);
        else ShowChat();
        Feedback("");
    }

    private async Task MoveConversationAsync()
    {
        if (_current is not { } current) return;
        var picker = new ComboBox { ItemsSource = _projects.Where(p => !p.Archived).ToArray(), SelectedItem = _project, HorizontalAlignment = HorizontalAlignment.Stretch };
        var panel = new StackPanel
        {
            Spacing = 12,
            Children =
        {
            Label("Les pièces jointes et la mémoire suivent cette conversation. Les sources et consignes partagées seront celles du projet de destination."),
            picker
        }
        };
        if (!await EditDialogAsync("Changer de projet", panel, "Déplacer") || picker.SelectedItem is not ProjectChoice target || target.Id == _project.Id) return;
        await _client.MoveAsync(current, target.Id, _lifetime.Token);
        await RefreshProjectsAsync(target.Id);
        C<TextBox>("SearchInput").Text = "";
        await RefreshConversationsAsync(current.Id);
        if (_current is not null) await LoadAsync(_current);
    }

    private void ShowChat()
    {
        C<Grid>("ChatPage").IsVisible = true;
        C<ScrollViewer>("PageScroll").IsVisible = false;
        C<ScrollViewer>("PreferencesPage").IsVisible = false;
        C<Border>("ActionMenu").IsVisible = false;
        C<TextBlock>("ViewHeading").Text = _current?.Title ?? "Conversation";
    }

    private void ShowPreferences()
    {
        HideInspector();
        var value = Appearance.Current;
        C<ComboBox>("ThemeChoice").SelectedIndex = value.Theme switch { "light" => 1, "dark" => 2, _ => 0 };
        C<ComboBox>("SizeChoice").SelectedItem = value.ReadingSize;
        C<CheckBox>("WrapChoice").IsChecked = value.WrapCode;
        C<TextBox>("ApiUrlInput").Text = value.ApiUrl;
        C<TextBlock>("ViewHeading").Text = "Paramètres";
        C<Grid>("ChatPage").IsVisible = false;
        C<Border>("ActionMenu").IsVisible = false;
        C<ScrollViewer>("PageScroll").IsVisible = false;
        C<ScrollViewer>("PreferencesPage").IsVisible = true;
        Feedback("");
    }

    private async Task SaveAppearanceAsync()
    {
        var api = C<TextBox>("ApiUrlInput").Text?.Trim() ?? "";
        if (api.Length > 0 && (!Uri.TryCreate(api, UriKind.Absolute, out var uri) || !uri.IsLoopback
            || uri.Scheme is not ("http" or "https") || uri.AbsolutePath != "/" || uri.UserInfo.Length + uri.Query.Length + uri.Fragment.Length > 0))
            throw new ChatApiException("Saisissez une adresse HTTP ou HTTPS locale, sans chemin ni identifiant.");
        string theme = C<ComboBox>("ThemeChoice").SelectedIndex switch { 1 => "light", 2 => "dark", _ => "system" };
        var previousApi = Appearance.Current.ApiUrl;
        Appearance.Save(new(theme, C<ComboBox>("SizeChoice").SelectedItem as int? ?? 18,
            C<ComboBox>("ModelSelector").SelectedItem as string ?? "", C<CheckBox>("WrapChoice").IsChecked == true, api));
        if (api != previousApi && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ALYCIA_API_BASE_URL")))
        {
            SaveDraft();
            _current = null;
            _project = Outside;
            await InitializeAsync();
        }
        Feedback("Paramètres enregistrés.");
    }

    private StackPanel ShowPage(string title)
    {
        HideInspector();
        C<TextBlock>("ViewHeading").Text = title;
        C<Grid>("ChatPage").IsVisible = false;
        C<Border>("ActionMenu").IsVisible = false;
        C<ScrollViewer>("PreferencesPage").IsVisible = false;
        C<ScrollViewer>("PageScroll").IsVisible = true;
        var body = C<StackPanel>("PageBody");
        body.Children.Clear();
        body.Children.Add(ActionButton("Revenir au fil", () => { ShowChat(); return Task.CompletedTask; }));
        Feedback("");
        return body;
    }

    private Button ActionButton(string title, Func<Task> action)
    {
        var button = new Button { Content = title };
        button.Click += async (_, _) => await RunAsync(action);
        return button;
    }

    private void LayoutInspector()
    {
        var inspector = C<Border>("Inspector");
        var compact = Bounds.Width < 1180;
        Grid.SetColumn(inspector, compact ? 1 : 2);
        inspector.Width = compact ? double.NaN : 350;
        C<Grid>("MainContent").IsVisible = !compact || !inspector.IsVisible;
    }

    private void HideInspector()
    {
        var panel = C<Border>("Inspector");
        if (!panel.IsVisible) return;
        panel.IsVisible = false;
        LayoutInspector();
        _inspectorOrigin?.Focus();
        _inspectorOrigin = null;
    }

    private async void EvidenceClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: ChatMessageItem selected } button) return;
        _inspectorOrigin = button;
        _evidence = selected;
        await RunAsync(() => OpenInspectorAsync("sources"));
    }

    private async Task OpenInspectorAsync(string tab)
    {
        if (_current is null) return;
        var panel = C<Border>("Inspector");
        if (!panel.IsVisible) _inspectorOrigin ??= C<Button>("ContextButton");
        panel.IsVisible = true;
        C<Border>("ActionMenu").IsVisible = false;
        LayoutInspector();
        var body = C<StackPanel>("InspectorBody");
        body.Children.Clear();
        await FillInspectorAsync(body, tab);
        C<Button>("CloseInspector").Focus();
        Feedback("");
    }

    private async Task ProjectPreferencesAsync()
    {
        if (_project.Id is not Guid id) return;
        var project = await _client.ProjectAsync(id, _lifetime.Token);
        var body = ShowPage("Consignes du projet");
        var instructions = Input(project.Instructions, 4000, true);
        var model = Input(project.DefaultModel, 200);
        body.Children.Add(Label("Consignes pour les prochains échanges"));
        body.Children.Add(instructions);
        body.Children.Add(Label("Modèle des nouvelles conversations · vide pour utiliser le modèle de l’application"));
        body.Children.Add(model);
        body.Children.Add(ActionButton("Enregistrer", async () =>
        {
            await _client.SaveProjectAsync(project with { Instructions = instructions.Text ?? "", DefaultModel = model.Text ?? "" }, _lifetime.Token);
            await ProjectPreferencesAsync();
            Feedback("Consignes enregistrées.");
        }));
        AddProjectNotes(body, project, ProjectPreferencesAsync);
        body.Children.Add(ActionButton(project.Archived ? "Restaurer le projet" : "Archiver le projet", async () =>
        {
            var next = await _client.SaveProjectAsync(project with { Archived = !project.Archived }, _lifetime.Token);
            await RefreshProjectsAsync(next.Id);
            await ProjectPreferencesAsync();
        }));
    }
}
