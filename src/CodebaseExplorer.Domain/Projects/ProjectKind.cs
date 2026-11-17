namespace CodebaseExplorer.Domain.Projects;

/// <summary>Kind of a project (a unit of build or packaging).</summary>
public enum ProjectKind
{
    DotNetProject,
    NodePackage,
    NodeWorkspaceRoot,
    PythonProject,
    PythonPackageFolder,
    Unknown,
}
