using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Analysis.ProjectSystems.DotNet;

/// <summary>Where an MSBuild element was declared: file and 1-based position of its <c>&lt;</c>.</summary>
internal sealed record MsBuildItemLocation(RelativePath File, int Line, int Column)
{
    public override string ToString() => $"{File}:{Line}:{Column}";
}
