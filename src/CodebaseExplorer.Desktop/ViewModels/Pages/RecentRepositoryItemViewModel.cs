using System.Globalization;
using CodebaseExplorer.Contracts.Repositories;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CodebaseExplorer.Desktop.ViewModels.Pages;

/// <summary>One entry of the start page's recent repositories (REPO-005).</summary>
public sealed partial class RecentRepositoryItemViewModel(RecentRepositoryDto repository) : ViewModelBase
{
    public RecentRepositoryDto Repository { get; } = repository;

    public string Name => Repository.Name;

    public string RootPath => Repository.RootPath;

    /// <summary>True if the stored model can be opened without a new analysis.</summary>
    public bool CanOpen => Repository.HasModel;

    /// <summary>Last run status and date, e.g. <c>Partially succeeded · 18.11.2026 10:30</c>.</summary>
    public string StatusText => Repository.LastRunStatus is null
        ? "Not analyzed yet"
        : Repository.LastRunAt is { } at
            ? $"{RunStatusText.Of(Repository.LastRunStatus)} · {at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}"
            : RunStatusText.Of(Repository.LastRunStatus);

    /// <summary>True while the inline "remove?" confirmation is shown.</summary>
    [ObservableProperty]
    public partial bool IsConfirmingRemove { get; set; }

    /// <summary>Accessible name of the list item.</summary>
    public override string ToString() => Name;
}
