using System.Collections.ObjectModel;
using System.Globalization;
using Alicia.Application.Conversations;
using Alicia.Domain.Conversations;
using Alicia.Presentation.State;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;

namespace Alicia.Presentation.ViewModels;

public sealed class ConversationListItemViewModel : ViewModelBase
{
    private readonly Func<bool> _canInteract;
    private readonly Func<ConversationListItemViewModel, ConversationVisualIdentity, Task> _changeIdentityAsync;
    private readonly Func<
        ConversationListItemViewModel,
        CancellationToken,
        Task<IReadOnlyList<ConversationPreviewMessageViewModel>>> _loadPreviewAsync;
    private bool _isPreviewLoaded;
    private bool _isPreviewLoading;
    private bool _isPreviewUnavailable;
    private bool _isRenaming;
    private bool _isSelected;
    private int _previewGeneration;
    private Task? _previewLoadTask;
    private string _renameTitle;
    private ConversationVisualIdentity _identity;

    public ConversationListItemViewModel(
        ConversationSummary summary,
        Func<ConversationListItemViewModel, Task> selectAsync,
        Func<ConversationListItemViewModel, Task> beginRenameAsync,
        Func<ConversationListItemViewModel, Task> saveRenameAsync,
        Func<ConversationListItemViewModel, Task> deleteAsync,
        Func<bool> canInteract)
        : this(
            summary,
            selectAsync,
            beginRenameAsync,
            saveRenameAsync,
            deleteAsync,
            static (_, _) =>
                Task.FromResult<IReadOnlyList<ConversationPreviewMessageViewModel>>([]),
            ConversationVisualIdentity.Default,
            static (_, _) => Task.CompletedTask,
            canInteract)
    {
    }

    public ConversationListItemViewModel(
        ConversationSummary summary,
        Func<ConversationListItemViewModel, Task> selectAsync,
        Func<ConversationListItemViewModel, Task> beginRenameAsync,
        Func<ConversationListItemViewModel, Task> saveRenameAsync,
        Func<ConversationListItemViewModel, Task> deleteAsync,
        Func<
            ConversationListItemViewModel,
            CancellationToken,
            Task<IReadOnlyList<ConversationPreviewMessageViewModel>>> loadPreviewAsync,
        Func<bool> canInteract)
        : this(
            summary,
            selectAsync,
            beginRenameAsync,
            saveRenameAsync,
            deleteAsync,
            loadPreviewAsync,
            ConversationVisualIdentity.Default,
            static (_, _) => Task.CompletedTask,
            canInteract)
    {
    }

    public ConversationListItemViewModel(
        ConversationSummary summary,
        Func<ConversationListItemViewModel, Task> selectAsync,
        Func<ConversationListItemViewModel, Task> beginRenameAsync,
        Func<ConversationListItemViewModel, Task> saveRenameAsync,
        Func<ConversationListItemViewModel, Task> deleteAsync,
        Func<
            ConversationListItemViewModel,
            CancellationToken,
            Task<IReadOnlyList<ConversationPreviewMessageViewModel>>> loadPreviewAsync,
        ConversationVisualIdentity identity,
        Func<ConversationListItemViewModel, ConversationVisualIdentity, Task> changeIdentityAsync,
        Func<bool> canInteract)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(selectAsync);
        ArgumentNullException.ThrowIfNull(beginRenameAsync);
        ArgumentNullException.ThrowIfNull(saveRenameAsync);
        ArgumentNullException.ThrowIfNull(deleteAsync);
        ArgumentNullException.ThrowIfNull(loadPreviewAsync);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(changeIdentityAsync);
        ArgumentNullException.ThrowIfNull(canInteract);

        _loadPreviewAsync = loadPreviewAsync;
        _changeIdentityAsync = changeIdentityAsync;
        _canInteract = canInteract;
        _identity = identity;

        Id = summary.Id;
        Title = summary.Title;
        _renameTitle = summary.Title;
        UpdatedAt = summary.UpdatedAt;
        MessageCount = summary.MessageCount;
        UpdatedAtLabel = summary.UpdatedAt
            .ToLocalTime()
            .ToString("g", CultureInfo.CurrentCulture);
        MessageCountLabel = $"{MessageCount} message{(MessageCount == 1 ? string.Empty : "s")}";
        MetadataLabel = $"{UpdatedAtLabel} • {MessageCountLabel}";

        SelectionAutomationName = $"Open conversation {Title}";
        RenameAutomationName = $"Rename conversation {Title}";
        DeleteAutomationName = $"Delete conversation {Title}";
        RenameInputAutomationName = $"New title for conversation {Title}";
        SaveRenameAutomationName = $"Save new title for conversation {Title}";
        CancelRenameAutomationName = $"Cancel renaming conversation {Title}";

        SelectionToolTip = $"Open “{Title}”";
        RenameToolTip = $"Rename “{Title}”";
        DeleteToolTip = $"Delete “{Title}”";
        RenameInputToolTip = "Type a new title. Enter saves; Escape cancels.";

        IconChoices = ConversationIdentityPresentationCatalog.Icons
            .Select(choice => new ConversationIdentityChoiceViewModel(
                choice.Label,
                () => ChangeIconAsync(choice.Value),
                canInteract))
            .ToArray();
        ColorChoices = ConversationIdentityPresentationCatalog.Colors
            .Select(choice => new ConversationIdentityChoiceViewModel(
                choice.Label,
                () => ChangeColorAsync(choice.Value),
                canInteract))
            .ToArray();
        UpdateIdentityChoiceSelection();

        SelectCommand = new AsyncRelayCommand(() => selectAsync(this), canInteract);
        RenameCommand = new AsyncRelayCommand(() => beginRenameAsync(this), canInteract);
        SaveRenameCommand = new AsyncRelayCommand(() => saveRenameAsync(this), CanExecuteSaveRename);
        CancelRenameCommand = new RelayCommand(CancelRename);
        DeleteCommand = new AsyncRelayCommand(() => deleteAsync(this), canInteract);
    }

    public ConversationId Id { get; }

    public string Title { get; }

    public DateTimeOffset UpdatedAt { get; }

    public int MessageCount { get; }

    public string UpdatedAtLabel { get; }

    public string MessageCountLabel { get; }

    public string MetadataLabel { get; }

    public string SelectionAutomationName { get; }

    public string RenameAutomationName { get; }

    public string DeleteAutomationName { get; }

    public string RenameInputAutomationName { get; }

    public string SaveRenameAutomationName { get; }

    public string CancelRenameAutomationName { get; }

    public string SelectionToolTip { get; }

    public string RenameToolTip { get; }

    public string DeleteToolTip { get; }

    public string RenameInputToolTip { get; }

    public ConversationVisualIdentity Identity => _identity;

    public string IdentityGlyph => ConversationIdentityPresentationCatalog.GetGlyph(_identity.Icon);

    public string IdentityDescription =>
        $"{ConversationIdentityPresentationCatalog.GetIconLabel(_identity.Icon)} icon • "
        + ConversationIdentityPresentationCatalog.GetColorLabel(_identity.Color);

    public IBrush IdentityColorBrush =>
        ConversationIdentityPresentationCatalog.GetAccentBrush(_identity.Color);

    public IBrush IdentitySurfaceBrush =>
        ConversationIdentityPresentationCatalog.GetSurfaceBrush(_identity.Color);

    public IReadOnlyList<ConversationIdentityChoiceViewModel> IconChoices { get; }

    public IReadOnlyList<ConversationIdentityChoiceViewModel> ColorChoices { get; }

    public ObservableCollection<ConversationPreviewMessageViewModel> PreviewMessages { get; } = [];

    public IAsyncRelayCommand SelectCommand { get; }

    public IAsyncRelayCommand RenameCommand { get; }

    public IAsyncRelayCommand SaveRenameCommand { get; }

    public IRelayCommand CancelRenameCommand { get; }

    public IAsyncRelayCommand DeleteCommand { get; }

    public string RenameTitle
    {
        get => _renameTitle;
        set
        {
            if (SetProperty(ref _renameTitle, value))
            {
                OnPropertyChanged(nameof(CanSaveRename));
                SaveRenameCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsRenaming
    {
        get => _isRenaming;
        private set
        {
            if (SetProperty(ref _isRenaming, value))
            {
                OnPropertyChanged(nameof(IsTitleVisible));
                OnPropertyChanged(nameof(CanSaveRename));
                SaveRenameCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsTitleVisible => !IsRenaming;

    public bool CanSaveRename => CanExecuteSaveRename();

    public bool IsSelected
    {
        get => _isSelected;
        private set => SetProperty(ref _isSelected, value);
    }

    public bool IsPreviewLoading
    {
        get => _isPreviewLoading;
        private set
        {
            if (SetProperty(ref _isPreviewLoading, value))
            {
                RaisePreviewStateChanged();
            }
        }
    }

    public bool HasPreviewMessages => PreviewMessages.Count > 0;

    public bool HasPreviewStatusText => IsPreviewLoading
        || _isPreviewUnavailable
        || (_isPreviewLoaded && PreviewMessages.Count == 0);

    public string PreviewStatusText => IsPreviewLoading
        ? "Loading preview…"
        : _isPreviewUnavailable
            ? "Preview unavailable"
            : _isPreviewLoaded && PreviewMessages.Count == 0
                ? "No messages yet"
                : string.Empty;

    public Task EnsurePreviewLoadedAsync()
    {
        if (_isPreviewLoaded)
        {
            return Task.CompletedTask;
        }

        if (_previewLoadTask is not null)
        {
            return _previewLoadTask;
        }

        int generation = _previewGeneration;
        IsPreviewLoading = true;
        _previewLoadTask = LoadPreviewCoreAsync(generation);
        return _previewLoadTask;
    }

    internal void BeginRename()
    {
        RenameTitle = Title;
        IsRenaming = true;
    }

    internal void CancelRename()
    {
        IsRenaming = false;
        RenameTitle = Title;
    }

    internal void NotifyInteractionStateChanged()
    {
        SelectCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        SaveRenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();

        foreach (ConversationIdentityChoiceViewModel choice in IconChoices)
        {
            choice.NotifyCanSelectChanged();
        }

        foreach (ConversationIdentityChoiceViewModel choice in ColorChoices)
        {
            choice.NotifyCanSelectChanged();
        }

        OnPropertyChanged(nameof(CanSaveRename));
    }

    internal void ResetPreview()
    {
        _previewGeneration++;
        _previewLoadTask = null;
        _isPreviewLoaded = false;
        _isPreviewUnavailable = false;
        _isPreviewLoading = false;
        PreviewMessages.Clear();
        RaisePreviewStateChanged();
    }

    internal void SetSearchPreview(
        IReadOnlyList<ConversationPreviewMessageViewModel> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        _previewGeneration++;
        _previewLoadTask = null;
        _isPreviewLoading = false;
        _isPreviewUnavailable = false;
        _isPreviewLoaded = true;
        ReplacePreviewMessages(messages);
        RaisePreviewStateChanged();
    }

    internal void SetSelected(bool isSelected)
    {
        IsSelected = isSelected;
    }

    private async Task ChangeIconAsync(ConversationIdentityIcon icon)
    {
        if (!_canInteract())
        {
            return;
        }

        ConversationVisualIdentity updated = _identity.WithIcon(icon);

        if (updated == _identity)
        {
            return;
        }

        SetIdentity(updated);
        await _changeIdentityAsync(this, updated).ConfigureAwait(true);
    }

    private async Task ChangeColorAsync(ConversationIdentityColor color)
    {
        if (!_canInteract())
        {
            return;
        }

        ConversationVisualIdentity updated = _identity.WithColor(color);

        if (updated == _identity)
        {
            return;
        }

        SetIdentity(updated);
        await _changeIdentityAsync(this, updated).ConfigureAwait(true);
    }

    private void SetIdentity(ConversationVisualIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (_identity == identity)
        {
            return;
        }

        _identity = identity;
        OnPropertyChanged(nameof(Identity));
        OnPropertyChanged(nameof(IdentityGlyph));
        OnPropertyChanged(nameof(IdentityDescription));
        OnPropertyChanged(nameof(IdentityColorBrush));
        OnPropertyChanged(nameof(IdentitySurfaceBrush));
        UpdateIdentityChoiceSelection();
    }

    private void UpdateIdentityChoiceSelection()
    {
        for (int index = 0; index < IconChoices.Count; index++)
        {
            IconChoices[index].SetSelected(
                ConversationIdentityPresentationCatalog.Icons[index].Value == _identity.Icon);
        }

        for (int index = 0; index < ColorChoices.Count; index++)
        {
            ColorChoices[index].SetSelected(
                ConversationIdentityPresentationCatalog.Colors[index].Value == _identity.Color);
        }
    }

    private async Task LoadPreviewCoreAsync(int generation)
    {
        try
        {
            IReadOnlyList<ConversationPreviewMessageViewModel> messages =
                await _loadPreviewAsync(this, CancellationToken.None).ConfigureAwait(true);

            if (generation != _previewGeneration)
            {
                return;
            }

            ReplacePreviewMessages(messages);
            _isPreviewLoaded = true;
            _isPreviewUnavailable = false;
        }
        catch (InvalidDataException)
        {
            MarkPreviewUnavailable(generation);
        }
        catch (IOException)
        {
            MarkPreviewUnavailable(generation);
        }
        catch (UnauthorizedAccessException)
        {
            MarkPreviewUnavailable(generation);
        }
        catch (KeyNotFoundException)
        {
            MarkPreviewUnavailable(generation);
        }
        finally
        {
            if (generation == _previewGeneration)
            {
                _previewLoadTask = null;
                IsPreviewLoading = false;
                RaisePreviewStateChanged();
            }
        }
    }

    private void MarkPreviewUnavailable(int generation)
    {
        if (generation != _previewGeneration)
        {
            return;
        }

        PreviewMessages.Clear();
        _isPreviewLoaded = true;
        _isPreviewUnavailable = true;
    }

    private void ReplacePreviewMessages(
        IReadOnlyList<ConversationPreviewMessageViewModel> messages)
    {
        PreviewMessages.Clear();

        foreach (ConversationPreviewMessageViewModel message in messages)
        {
            PreviewMessages.Add(message);
        }
    }

    private void RaisePreviewStateChanged()
    {
        OnPropertyChanged(nameof(HasPreviewMessages));
        OnPropertyChanged(nameof(HasPreviewStatusText));
        OnPropertyChanged(nameof(PreviewStatusText));
    }

    private bool CanExecuteSaveRename()
    {
        if (!IsRenaming || !_canInteract())
        {
            return false;
        }

        string normalizedTitle = RenameTitle.Trim();

        return normalizedTitle.Length > 0
            && normalizedTitle.Length <= Conversation.MaxTitleLength
            && !string.Equals(normalizedTitle, Title, StringComparison.Ordinal);
    }
}
