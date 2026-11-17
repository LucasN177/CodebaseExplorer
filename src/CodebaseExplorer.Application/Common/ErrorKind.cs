namespace CodebaseExplorer.Application.Common;

/// <summary>Category of an expected failure; lets callers react without parsing error codes.</summary>
public enum ErrorKind
{
    /// <summary>The input is invalid (e.g. a path that is not a directory).</summary>
    Validation,

    /// <summary>A requested item does not exist (e.g. unknown repository id).</summary>
    NotFound,

    /// <summary>The operation conflicts with the current state (e.g. an analysis is already running).</summary>
    Conflict,

    /// <summary>The operation could not be completed for an expected external reason (e.g. authentication failed).</summary>
    Failure,
}
