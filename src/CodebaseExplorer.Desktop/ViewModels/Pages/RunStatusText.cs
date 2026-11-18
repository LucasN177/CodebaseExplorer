using CodebaseExplorer.Contracts.Repositories;

namespace CodebaseExplorer.Desktop.ViewModels.Pages;

/// <summary>Display text of the run status values of <see cref="RunStatuses"/>.</summary>
internal static class RunStatusText
{
    public static string Of(string status) => status switch
    {
        RunStatuses.Pending => "Pending",
        RunStatuses.Running => "Running",
        RunStatuses.Succeeded => "Succeeded",
        RunStatuses.PartiallySucceeded => "Partially succeeded",
        RunStatuses.Failed => "Failed",
        RunStatuses.Cancelled => "Cancelled",
        _ => status,
    };
}
