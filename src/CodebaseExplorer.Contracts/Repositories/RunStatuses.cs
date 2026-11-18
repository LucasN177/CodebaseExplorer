namespace CodebaseExplorer.Contracts.Repositories;

/// <summary>Analysis run status values used in repository DTOs.</summary>
public static class RunStatuses
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string PartiallySucceeded = "partially-succeeded";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}
