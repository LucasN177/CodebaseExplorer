namespace CodebaseExplorer.Analysis.Scanning;

/// <summary>Files that identify a solution or project of a supported ecosystem.</summary>
internal enum ProjectMarker
{
    /// <summary><c>*.sln</c> (text format).</summary>
    DotNetSolution,

    /// <summary><c>*.slnx</c> (XML format).</summary>
    DotNetSolutionXml,

    /// <summary><c>*.csproj</c>, <c>*.fsproj</c>, <c>*.vbproj</c>.</summary>
    DotNetProject,

    /// <summary><c>package.json</c>.</summary>
    NodePackage,

    /// <summary><c>pyproject.toml</c>.</summary>
    PythonProject,

    /// <summary><c>requirements*.txt</c>.</summary>
    PythonRequirements,

    /// <summary><c>setup.cfg</c>.</summary>
    PythonSetupConfig,

    /// <summary><c>setup.py</c> — detected and reported, never executed.</summary>
    PythonSetupScript,
}
