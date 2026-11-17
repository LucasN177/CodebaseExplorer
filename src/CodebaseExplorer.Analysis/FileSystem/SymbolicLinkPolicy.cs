namespace CodebaseExplorer.Analysis.FileSystem;

/// <summary>How the file reader treats symbolic links and junctions inside a repository (SECURITY.md).</summary>
internal enum SymbolicLinkPolicy
{
    /// <summary>Default: any link on the path is refused.</summary>
    DoNotFollow,

    /// <summary>Links are followed if their final target lies inside the repository root.</summary>
    FollowWithinRoot,
}
