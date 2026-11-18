namespace CodebaseExplorer.Application.Git;

/// <summary>A Git working tree that contains an analyzed folder (<see cref="IGitRepositoryReader.OpenAsync"/>).</summary>
/// <param name="RootPath">The analyzed folder (absolute, as passed to <see cref="IGitRepositoryReader.OpenAsync"/>).</param>
/// <param name="WorkingDirectory">Absolute path of the working tree, without trailing separator; equal to or a parent of <paramref name="RootPath"/>.</param>
/// <param name="RootPrefix">
/// <paramref name="RootPath"/> relative to <paramref name="WorkingDirectory"/> with <c>/</c> separators and no trailing
/// slash; empty when the analyzed folder is the working tree itself. Git paths are mapped to repository files by it.
/// </param>
/// <param name="IsLinkedWorktree">The working tree is a linked worktree (<c>git worktree add</c>) whose <c>.git</c> is a file.</param>
/// <param name="IsShallow">The repository has truncated history (shallow clone); activity statistics are incomplete.</param>
public sealed record GitRepositoryInfo(
    string RootPath,
    string WorkingDirectory,
    string RootPrefix,
    bool IsLinkedWorktree,
    bool IsShallow);
