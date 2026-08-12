using System.Collections.ObjectModel;
using System.Globalization;
using Alicia.Domain.Conversations;

namespace Alicia.Presentation.ViewModels;

public sealed class MessageViewModel : ViewModelBase
{
    private ReasoningStepViewModel? _activeReasoningStep;
    private string _content;
    private bool _isReasoningExpanded;
    private bool _isWaitingForFirstDelta;
    private int _pendingReasoningNewLines;
    private int _reasoningRevision;
    private string _thinkingIndicatorText;

    public MessageViewModel(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        RoleLabel = message.Role switch
        {
            MessageRole.System => "System",
            MessageRole.User => "You",
            MessageRole.Assistant => "Alicia",
            _ => throw new ArgumentOutOfRangeException(nameof(message), message.Role, "Unsupported message role."),
        };

        _content = message.Content;
        _thinkingIndicatorText = string.Empty;
        CreatedAtLabel = message.CreatedAt
            .ToLocalTime()
            .ToString("g", CultureInfo.CurrentCulture);

        IsSystem = message.Role == MessageRole.System;
        IsUser = message.Role == MessageRole.User;
        IsAssistant = message.Role == MessageRole.Assistant;
        AutomationName = $"{RoleLabel} message at {CreatedAtLabel}";
    }

    private MessageViewModel()
    {
        RoleLabel = "Alicia";
        _content = string.Empty;
        _thinkingIndicatorText = "Alicia réfléchit.";
        CreatedAtLabel = "Streaming…";
        IsAssistant = true;
        IsStreaming = true;
        _isWaitingForFirstDelta = true;
        _isReasoningExpanded = true;
        AutomationName = "Alicia response in progress";
    }

    public string RoleLabel { get; }

    public string Content
    {
        get => _content;
        private set
        {
            if (SetProperty(ref _content, value))
            {
                OnPropertyChanged(nameof(HasContent));
            }
        }
    }

    public string CreatedAtLabel { get; }

    public bool IsSystem { get; }

    public bool IsUser { get; }

    public bool IsAssistant { get; }

    public bool IsStreaming { get; }

    public string AutomationName { get; }

    public ObservableCollection<ReasoningStepViewModel> ReasoningSteps { get; } = [];

    public bool HasContent => Content.Length > 0;

    public bool HasReasoning => ReasoningSteps.Count > 0;

    public int ReasoningRevision => _reasoningRevision;

    public bool IsWaitingForFirstDelta
    {
        get => _isWaitingForFirstDelta;
        private set => SetProperty(ref _isWaitingForFirstDelta, value);
    }

    public bool IsReasoningExpanded
    {
        get => _isReasoningExpanded;
        set => SetProperty(ref _isReasoningExpanded, value);
    }

    public string ThinkingIndicatorText
    {
        get => _thinkingIndicatorText;
        private set => SetProperty(ref _thinkingIndicatorText, value);
    }

    public string ReasoningHeader => IsStreaming
        ? "Raisonnement en cours"
        : "Raisonnement";

    public static MessageViewModel CreateStreamingAssistant()
    {
        return new MessageViewModel();
    }

    public void AppendContentDelta(string contentDelta)
    {
        ArgumentException.ThrowIfNullOrEmpty(contentDelta);
        EnsureStreaming();
        MarkStreamActivity();
        Content += contentDelta;
    }

    public void AppendReasoningDelta(string reasoningDelta)
    {
        ArgumentException.ThrowIfNullOrEmpty(reasoningDelta);
        EnsureStreaming();
        MarkStreamActivity();

        string normalized = reasoningDelta
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        foreach (char character in normalized)
        {
            if (character == '\n')
            {
                _pendingReasoningNewLines++;
                continue;
            }

            FlushPendingReasoningNewLines();

            if (_activeReasoningStep is null)
            {
                _activeReasoningStep = new ReasoningStepViewModel(character.ToString());
                ReasoningSteps.Add(_activeReasoningStep);
                OnPropertyChanged(nameof(HasReasoning));
            }
            else
            {
                _activeReasoningStep.Append(character.ToString());
            }
        }

        _reasoningRevision++;
        OnPropertyChanged(nameof(ReasoningRevision));
    }

    public string[] CaptureReasoningSteps()
    {
        return ReasoningSteps
            .Select(step => step.Content.Trim())
            .Where(content => content.Length > 0)
            .ToArray();
    }

    public void SetReasoningSnapshot(IReadOnlyList<string> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        if (IsStreaming)
        {
            throw new InvalidOperationException(
                "A streaming message cannot replace its reasoning snapshot.");
        }

        ReasoningSteps.Clear();

        foreach (string step in steps)
        {
            if (!string.IsNullOrWhiteSpace(step))
            {
                ReasoningSteps.Add(new ReasoningStepViewModel(step.Trim()));
            }
        }

        IsReasoningExpanded = false;
        OnPropertyChanged(nameof(HasReasoning));
        OnPropertyChanged(nameof(ReasoningHeader));
    }

    public void SetThinkingIndicatorText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        EnsureStreaming();

        if (IsWaitingForFirstDelta)
        {
            ThinkingIndicatorText = text;
        }
    }

    private void EnsureStreaming()
    {
        if (!IsStreaming)
        {
            throw new InvalidOperationException(
                "Only a streaming assistant projection can accept response deltas.");
        }
    }

    private void MarkStreamActivity()
    {
        if (IsWaitingForFirstDelta)
        {
            IsWaitingForFirstDelta = false;
        }
    }

    private void FlushPendingReasoningNewLines()
    {
        if (_pendingReasoningNewLines == 0)
        {
            return;
        }

        if (_pendingReasoningNewLines >= 2)
        {
            _activeReasoningStep = null;
        }
        else if (_activeReasoningStep is not null)
        {
            _activeReasoningStep.Append("\n");
        }

        _pendingReasoningNewLines = 0;
    }
}
