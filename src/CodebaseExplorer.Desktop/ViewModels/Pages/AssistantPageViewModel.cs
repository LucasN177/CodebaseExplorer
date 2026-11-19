using System.Collections.ObjectModel;
using CodebaseExplorer.Application.AI;
using CodebaseExplorer.Application.Source;
using CodebaseExplorer.Contracts.AI;
using CodebaseExplorer.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CodebaseExplorer.Desktop.ViewModels.Pages;

/// <summary>
/// The AI assistant page (AI-006): questions about the open repository with streamed answers, clickable citations
/// (source lines or graph node), the context manifest of every answer, an "AI-generated" label, cancellation, the
/// conversation history, and the consent step for remote endpoints. All work happens in Application use cases.
/// </summary>
public sealed partial class AssistantPageViewModel : PageViewModel
{
    private readonly RepositorySession _session;
    private readonly AskRepositoryQuestion _ask;
    private readonly AiConversations _conversations;
    private readonly AiSettingsStore _settings;
    private readonly GetSourceSnippet _getSnippet;
    private readonly ArchitectureExplorerPageViewModel _explorer;
    private readonly ShellStatus _shell;
    private readonly ILogger<AssistantPageViewModel> _logger;
    private CancellationTokenSource? _running;
    private bool _selectingProgrammatically;
    private int _snippetNumber;

    public AssistantPageViewModel(
        RepositorySession session,
        AskRepositoryQuestion ask,
        AiConversations conversations,
        AiSettingsStore settings,
        GetSourceSnippet getSnippet,
        ArchitectureExplorerPageViewModel explorer,
        ShellStatus shell,
        ILogger<AssistantPageViewModel> logger)
    {
        _session = session;
        _ask = ask;
        _conversations = conversations;
        _settings = settings;
        _getSnippet = getSnippet;
        _explorer = explorer;
        _shell = shell;
        _logger = logger;
        _session.CurrentChanged += (_, _) => _ = ActivateAsync();
        _explorer.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ArchitectureExplorerPageViewModel.InspectedElementId))
            {
                OnPropertyChanged(nameof(SelectionText));
                OnPropertyChanged(nameof(HasExplorerSelection));
            }
        };
        SnippetLines.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSnippet));
    }

    public override string Title => "Assistant";

    public override string Glyph => "✦";

    /// <summary>Raised when a citation should be shown in the graph; the shell switches to the explorer.</summary>
    public event EventHandler<string>? ElementRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRepository), nameof(ShowEmptyState))]
    public partial string? RepositoryName { get; private set; }

    public bool HasRepository => RepositoryName is not null;

    public bool ShowEmptyState => !HasRepository;

    /// <summary>Which model answers and whether context leaves the machine, or that AI is off.</summary>
    [ObservableProperty]
    public partial string? AiStatus { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AskCommand))]
    public partial bool IsAiEnabled { get; private set; }

    /// <summary>The endpoint is not on this machine: shown as a permanent indicator.</summary>
    [ObservableProperty]
    public partial bool IsRemote { get; private set; }

    public ObservableCollection<AiConversationItem> Conversations { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteConversationCommand))]
    public partial AiConversationItem? SelectedConversation { get; set; }

    public ObservableCollection<AiMessageItem> Messages { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AskCommand))]
    public partial string? Question { get; set; }

    /// <summary>Ask about the element inspected in the Architecture Explorer instead of the whole repository.</summary>
    [ObservableProperty]
    public partial bool AboutSelection { get; set; }

    public bool HasExplorerSelection => _explorer.InspectedElementId is not null;

    public string? SelectionText => _explorer.InspectedElementName is { } name ? $"About the selected element: {name}" : null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AskCommand), nameof(CancelCommand), nameof(AllowRemoteCommand))]
    public partial bool IsBusy { get; private set; }

    /// <summary>Progress, an error, or why nothing happened.</summary>
    [ObservableProperty]
    public partial string? StatusMessage { get; private set; }

    /// <summary>Shown when the endpoint is remote and this repository has no consent yet.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AllowRemoteCommand))]
    public partial string? ConsentText { get; private set; }

    public ObservableCollection<SnippetLine> SnippetLines { get; } = [];

    public bool HasSnippet => SnippetLines.Count > 0;

    [ObservableProperty]
    public partial string? SnippetPath { get; private set; }

    [ObservableProperty]
    public partial string? SnippetMessage { get; private set; }

    public override async Task ActivateAsync()
    {
        try
        {
            var settings = await _settings.LoadAsync(CancellationToken.None);
            IsAiEnabled = settings.Enabled;
            IsRemote = settings.Enabled && settings.IsRemoteEndpoint;
            AiStatus = !settings.Enabled
                ? "The AI assistant is off. Enable it and choose a model in Settings; until then nothing is sent anywhere."
                : settings.ModelId is null
                    ? "No model selected. Choose one in Settings."
                    : settings.IsRemoteEndpoint
                        ? $"Model {settings.ModelId} at {settings.Endpoint} — not on this machine: code and facts of this repository are sent there."
                        : $"Model {settings.ModelId} at {settings.Endpoint} (this machine).";

            var repository = _session.Current;
            RepositoryName = repository?.Name;
            if (repository is null)
            {
                Conversations.Clear();
                Messages.Clear();
                return;
            }

            await RefreshConversationsAsync(repository.RepositoryId, SelectedConversation?.Id);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Loading the assistant page failed");
            StatusMessage = "The assistant could not be loaded. Details are in the application log.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanAsk))]
    private async Task AskAsync()
    {
        if (_session.Current is not { } repository || string.IsNullOrWhiteSpace(Question))
        {
            return;
        }

        var text = Question.Trim();
        var elementId = AboutSelection ? _explorer.InspectedElementId : null;
        var question = AiMessageItem.Question(text);
        var pending = AiMessageItem.Pending();
        Messages.Add(question);
        Messages.Add(pending);
        ConsentText = null;
        StatusMessage = "Thinking… (the first question of a repository also indexes its files)";
        IsBusy = true;
        using var running = new CancellationTokenSource();
        _running = running;
        using var operation = _shell.BeginOperation("Asking the AI assistant");
        try
        {
            var progress = new Progress<string>(pending.Append);
            var result = await _ask.ExecuteAsync(repository.RepositoryId, SelectedConversation?.Id, text, elementId, progress, running.Token);
            if (!result.TryGetValue(out var exchange, out var error))
            {
                Messages.Remove(pending);
                Messages.Remove(question);
                if (error.Code == AiErrorCodes.ConsentRequired)
                {
                    ConsentText = error.Message;
                    StatusMessage = null;
                }
                else
                {
                    StatusMessage = error.Message;
                }

                return;
            }

            pending.Complete(exchange.Answer);
            Question = null;
            StatusMessage = null;
            await RefreshConversationsAsync(repository.RepositoryId, exchange.Conversation.Id, reloadMessages: false);
        }
        catch (OperationCanceledException)
        {
            Messages.Remove(pending);
            Messages.Remove(question);
            StatusMessage = "Cancelled. Nothing was saved.";
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Asking the AI assistant failed unexpectedly");
            Messages.Remove(pending);
            Messages.Remove(question);
            StatusMessage = "The question could not be answered. Details are in the application log.";
        }
        finally
        {
            _running = null;
            IsBusy = false;
        }
    }

    private bool CanAsk() => !IsBusy && IsAiEnabled && !string.IsNullOrWhiteSpace(Question);

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _running?.Cancel();

    /// <summary>Allows sending this repository's context to the configured remote endpoint, then asks again.</summary>
    [RelayCommand(CanExecute = nameof(CanAllowRemote))]
    private async Task AllowRemoteAsync()
    {
        if (_session.Current is not { } repository)
        {
            return;
        }

        await _settings.GrantRemoteConsentAsync(repository.RepositoryId, CancellationToken.None);
        ConsentText = null;
        await AskAsync();
    }

    private bool CanAllowRemote() => !IsBusy && ConsentText is not null;

    [RelayCommand]
    private void NewConversation()
    {
        SelectedConversation = null;
        Messages.Clear();
        StatusMessage = null;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteConversation))]
    private async Task DeleteConversationAsync()
    {
        if (SelectedConversation is not { } conversation || _session.Current is not { } repository)
        {
            return;
        }

        var result = await _conversations.DeleteAsync(conversation.Id, CancellationToken.None);
        StatusMessage = result.IsFailure ? result.Error.Message : null;
        Messages.Clear();
        await RefreshConversationsAsync(repository.RepositoryId, null);
    }

    private bool CanDeleteConversation() => SelectedConversation is not null && !IsBusy;

    /// <summary>Shows the cited lines (verified file citations) or the cited element in the graph.</summary>
    [RelayCommand]
    private async Task OpenCitationAsync(AiCitationItem? citation)
    {
        if (citation is null)
        {
            return;
        }

        if (citation.CanOpenSource)
        {
            await LoadSnippetAsync(citation.Citation);
        }
        else if (citation.CanShowInGraph)
        {
            ElementRequested?.Invoke(this, citation.Citation.ElementId!);
        }
        else
        {
            StatusMessage = citation.ToolTip;
        }
    }

    partial void OnSelectedConversationChanged(AiConversationItem? value)
    {
        if (!_selectingProgrammatically && value is not null)
        {
            _ = LoadMessagesAsync(value.Id);
        }
    }

    private async Task RefreshConversationsAsync(string repositoryId, string? select, bool reloadMessages = true)
    {
        var result = await _conversations.ListAsync(repositoryId, CancellationToken.None);
        if (!result.TryGetValue(out var list, out var error))
        {
            StatusMessage = error.Message;
            return;
        }

        _selectingProgrammatically = true;
        try
        {
            Conversations.Clear();
            foreach (var conversation in list)
            {
                Conversations.Add(new AiConversationItem(conversation));
            }

            SelectedConversation = Conversations.FirstOrDefault(c => c.Id == select);
        }
        finally
        {
            _selectingProgrammatically = false;
        }

        if (SelectedConversation is null)
        {
            Messages.Clear();
        }
        else if (reloadMessages)
        {
            await LoadMessagesAsync(SelectedConversation.Id);
        }
    }

    private async Task LoadMessagesAsync(string conversationId)
    {
        var result = await _conversations.GetMessagesAsync(conversationId, CancellationToken.None);
        Messages.Clear();
        if (!result.TryGetValue(out var messages, out var error))
        {
            StatusMessage = error.Message;
            return;
        }

        foreach (var message in messages)
        {
            Messages.Add(AiMessageItem.From(message));
        }
    }

    private async Task LoadSnippetAsync(AiCitationDto citation)
    {
        var number = ++_snippetNumber;
        SnippetLines.Clear();
        SnippetPath = citation.Path;
        SnippetMessage = null;
        if (_session.Current is not { } repository)
        {
            return;
        }

        var result = await _getSnippet.ExecuteAsync(repository.RepositoryId, citation.Path!, citation.StartLine!.Value, citation.EndLine, CancellationToken.None);
        if (number != _snippetNumber)
        {
            return;
        }

        if (!result.TryGetValue(out var snippet, out var error))
        {
            SnippetMessage = error.Message;
            return;
        }

        foreach (var line in snippet.Lines)
        {
            SnippetLines.Add(new SnippetLine(line.Number, line.Text, line.IsEvidence));
        }

        SnippetMessage = snippet.ChangedSinceAnalysis
            ? "The file changed since the analysis; the cited lines may have moved."
            : null;
    }
}
