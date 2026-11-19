using System.Collections.ObjectModel;
using CodebaseExplorer.Application.AI;
using CodebaseExplorer.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CodebaseExplorer.Desktop.ViewModels.Pages;

/// <summary>
/// "Explain with AI" in the Component Inspector (AI-008): generates or reuses the cached explanation of the inspected
/// element (<see cref="ExplainComponent"/>), streams it, and shows it like an assistant answer — plain-text blocks,
/// "AI-generated" label and clickable, validated citations.
/// </summary>
public sealed partial class ComponentExplanationViewModel : ViewModelBase
{
    private readonly ExplainComponent _explain;
    private readonly AiSettingsStore _settings;
    private readonly RepositorySession _session;
    private readonly ShellStatus _shell;
    private readonly ILogger<ComponentExplanationViewModel> _logger;
    private CancellationTokenSource? _running;
    private int _elementNumber;

    public ComponentExplanationViewModel(
        ExplainComponent explain,
        AiSettingsStore settings,
        RepositorySession session,
        ShellStatus shell,
        ILogger<ComponentExplanationViewModel> logger)
    {
        _explain = explain;
        _settings = settings;
        _session = session;
        _shell = shell;
        _logger = logger;
    }

    /// <summary>Raised for a clicked citation; the explorer shows its lines or focuses its element.</summary>
    public event EventHandler<AiCitationItem>? CitationRequested;

    /// <summary>The element to explain (set by the explorer when the inspector shows a model element).</summary>
    public string? ElementId { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExplainCommand), nameof(RegenerateCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExplainCommand), nameof(RegenerateCommand))]
    public partial bool CanExplainElement { get; private set; }

    /// <summary>The explanation (streamed text while it is generated), or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExplanation))]
    public partial AiMessageItem? Explanation { get; private set; }

    public bool HasExplanation => Explanation is not null;

    /// <summary>"Cached for this analysis run", an error, or that AI is off.</summary>
    [ObservableProperty]
    public partial string? Status { get; private set; }

    /// <summary>Called by the explorer whenever the inspected element changes (null: nothing to explain).</summary>
    public void SetElement(string? elementId)
    {
        ++_elementNumber;
        _running?.Cancel();
        ElementId = elementId;
        Explanation = null;
        Status = null;
        CanExplainElement = elementId is not null;
    }

    [RelayCommand(CanExecute = nameof(CanExplain))]
    private Task ExplainAsync() => RunAsync(regenerate: false);

    [RelayCommand(CanExecute = nameof(CanRegenerate))]
    private Task RegenerateAsync() => RunAsync(regenerate: true);

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _running?.Cancel();

    [RelayCommand]
    private void OpenCitation(AiCitationItem? citation)
    {
        if (citation is not null)
        {
            if (citation.CanOpenSource || citation.CanShowInGraph)
            {
                CitationRequested?.Invoke(this, citation);
            }
            else
            {
                Status = citation.ToolTip;
            }
        }
    }

    private bool CanExplain() => CanExplainElement && !IsBusy;

    private bool CanRegenerate() => CanExplainElement && !IsBusy && HasExplanation;

    partial void OnExplanationChanged(AiMessageItem? value) => RegenerateCommand.NotifyCanExecuteChanged();

    private async Task RunAsync(bool regenerate)
    {
        if (ElementId is not { } elementId || _session.Current is not { } repository)
        {
            return;
        }

        var number = _elementNumber;
        if (!(await _settings.LoadAsync(CancellationToken.None)).Enabled)
        {
            Status = "The AI assistant is off. Enable it and choose a model in Settings.";
            return;
        }

        var pending = AiMessageItem.Pending();
        Explanation = pending;
        Status = "Explaining…";
        IsBusy = true;
        using var running = new CancellationTokenSource();
        _running = running;
        using var operation = _shell.BeginOperation("Explaining the component with AI");
        try
        {
            var result = await _explain.ExecuteAsync(repository.RepositoryId, elementId, regenerate, new Progress<string>(pending.Append), running.Token);
            if (number != _elementNumber)
            {
                return; // the selection changed meanwhile
            }

            if (!result.TryGetValue(out var explanation, out var error))
            {
                Explanation = null;
                Status = error.Code == AiErrorCodes.ConsentRequired
                    ? error.Message + " Allow it on the Assistant page first."
                    : error.Message;
                return;
            }

            pending.Complete(explanation.Answer);
            Status = explanation.FromCache ? "Explained earlier for this analysis run." : null;
        }
        catch (OperationCanceledException)
        {
            if (number == _elementNumber)
            {
                Explanation = null;
                Status = "Cancelled.";
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Explaining a component failed unexpectedly");
            if (number == _elementNumber)
            {
                Explanation = null;
                Status = "The explanation could not be generated. Details are in the application log.";
            }
        }
        finally
        {
            if (ReferenceEquals(_running, running))
            {
                _running = null;
            }

            IsBusy = false;
        }
    }
}
