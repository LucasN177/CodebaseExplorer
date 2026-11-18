using System.Globalization;
using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Git;
using CodebaseExplorer.Contracts.Repositories;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CodebaseExplorer.Desktop.ViewModels.Pages;

/// <summary>
/// Clone form of the start page (UI-008, USER_WORKFLOWS.md WF-01): HTTPS address, optional branch and access token,
/// progress and cancel through the shared progress panel, and the choice between updating and re-cloning an existing
/// clone. The token is held only until the clone finishes: the masked field is emptied as soon as the clone starts,
/// and the token is never shown, logged or put into a message (OPD-03: no persistence).
/// </summary>
public sealed partial class StartPageViewModel
{
    private string? _pendingToken;

    /// <summary>True while the clone form is shown.</summary>
    [ObservableProperty]
    public partial bool IsCloneFormOpen { get; set; }

    /// <summary>HTTPS address of the remote repository.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloneCommand))]
    public partial string CloneUrl { get; set; } = string.Empty;

    /// <summary>Branch to analyze; the remote's default branch when empty.</summary>
    [ObservableProperty]
    public partial string CloneBranch { get; set; } = string.Empty;

    /// <summary>Personal access token for private repositories, bound to a masked field; emptied when the clone starts.</summary>
    [ObservableProperty]
    public partial string CloneToken { get; set; } = string.Empty;

    /// <summary>Set when the repository was cloned before: the user chooses update or re-clone.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExistingClone))]
    public partial string? ExistingClonePath { get; private set; }

    public bool HasExistingClone => ExistingClonePath is not null;

    [RelayCommand]
    private void ShowCloneForm()
    {
        IsCloneFormOpen = true;
        ErrorMessage = null;
        StatusMessage = null;
    }

    [RelayCommand]
    private void CloseCloneForm()
    {
        IsCloneFormOpen = false;
        ExistingClonePath = null;
        CloneToken = string.Empty;
        _pendingToken = null;
    }

    /// <summary>Clones and analyzes the remote; <c>CloneCancelCommand</c> cancels it.</summary>
    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanClone))]
    private Task CloneAsync(CancellationToken cancellationToken)
    {
        // Take the token out of the bound property at once: the field is empty while and after cloning.
        _pendingToken = string.IsNullOrWhiteSpace(CloneToken) ? null : CloneToken;
        CloneToken = string.Empty;
        var url = CloneUrl;

        // Credentials typed into the address are not shown any longer either; the use case strips and reports them.
        if (Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && uri.UserInfo.Length > 0)
        {
            CloneUrl = GitRemoteUrls.Sanitize(url);
        }

        ExistingClonePath = null;
        return CloneRemoteAsync(url, ExistingCloneAction.Ask, cancellationToken);
    }

    /// <summary>Resolves an existing clone: <c>update</c> (fetch + checkout) or <c>reclone</c>.</summary>
    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanStartImport))]
    private Task ResolveExistingCloneAsync(string? choice, CancellationToken cancellationToken)
    {
        ExistingClonePath = null;
        var action = choice == "reclone" ? ExistingCloneAction.Reclone : ExistingCloneAction.Update;
        return CloneRemoteAsync(CloneUrl, action, cancellationToken);
    }

    private bool CanClone() => !IsImporting && !string.IsNullOrWhiteSpace(CloneUrl);

    private async Task CloneRemoteAsync(string url, ExistingCloneAction existing, CancellationToken cancellationToken)
    {
        var importNumber = ++_importNumber;
        var display = GitRemoteUrls.Sanitize(url);
        StartImport(display, $"Cloning {display}");

        var gitProgress = new Progress<GitProgress>(report =>
        {
            if (importNumber == _importNumber && IsImporting)
            {
                ApplyCloneProgress(report);
            }
        });
        var analysisProgress = new Progress<AnalysisProgress>(report =>
        {
            if (importNumber == _importNumber && IsImporting)
            {
                ApplyProgress(report);
            }
        });

        var request = new RemoteImportRequest(url, CloneBranch, _pendingToken, existing);
        var keepToken = false;
        try
        {
            var result = await Task.Run(() => _importRemote.ExecuteAsync(request, gitProgress, analysisProgress, cancellationToken), cancellationToken);
            if (!result.TryGetValue(out var imported, out var error))
            {
                if (error.Code == GitErrorCodes.CloneExists)
                {
                    // Keep the token for the update/re-clone choice; it is still not shown anywhere.
                    keepToken = true;
                    ExistingClonePath = error.Details.GetValueOrDefault("path") ?? display;
                    FinishImport(statusMessage: "This repository was cloned before. Update the existing clone or clone it again.");
                    return;
                }

                // Error codes only: messages of the Git adapter never contain the token, but the code is enough.
                _logger.LogWarning("Clone of a remote repository failed with {ErrorCode}", error.Code);
                FinishImport(errorMessage: error.Message);
                return;
            }

            var summary = imported.Summary;
            FinishImport(statusMessage: CloneStatus(imported));
            foreach (var stage in Stages)
            {
                stage.State = ImportStageState.Done;
            }

            IsCloneFormOpen = false;
            CloneUrl = string.Empty;
            CloneBranch = string.Empty;
            _session.SetCurrent(new OpenRepositoryDto
            {
                RepositoryId = summary.RepositoryId,
                Name = summary.Name,
                RootPath = summary.RootPath,
                AnalysisRunId = summary.AnalysisRunId,
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            FinishImport(statusMessage: "Clone cancelled. Nothing was kept.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The exception may come from libgit2; log its type only, never its message (it could echo input).
            _logger.LogError("Clone of a remote repository failed unexpectedly with {ExceptionType}", exception.GetType().Name);
            FinishImport(errorMessage: "The clone failed unexpectedly. Details are in the application log.");
        }
        finally
        {
            if (!keepToken)
            {
                _pendingToken = null;
            }
        }

        await LoadRecentRepositoriesAsync();
    }

    private static string CloneStatus(RemoteRepositoryImportDto imported)
    {
        var summary = imported.Summary;
        var text = $"{(imported.Updated ? "Updated" : "Cloned")} and analyzed {summary.Name}: {summary.ProjectCount} projects, {summary.FileCount} files, " +
            $"{RunItemViewModel.Count(summary.WarningCount, "warning")}, {RunItemViewModel.Count(summary.ErrorCount, "error")}.";
        if (imported.CredentialsRemovedFromUrl)
        {
            text += " The address contained credentials; they were removed — use the token field for private repositories.";
        }

        if (imported.HasSubmodules)
        {
            text += " Submodules were not cloned and are not analyzed.";
        }

        return text;
    }

    private void ApplyCloneProgress(GitProgress report)
    {
        IsProgressIndeterminate = report.Total <= 0;
        ProgressPercent = report.Total > 0 ? 100.0 * report.Completed / report.Total : 0;
        var stage = report.Stage == GitProgressStage.CheckingOut ? "Checking out files" : "Receiving objects";
        ProgressText = report.Total > 0
            ? string.Create(CultureInfo.CurrentCulture, $"{stage} {report.Completed} / {report.Total}")
            : stage;
        if (_operation is not null)
        {
            _operation.Detail = ProgressText;
        }
    }
}
