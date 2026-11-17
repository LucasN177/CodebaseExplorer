namespace CodebaseExplorer.Analysis.Scanning;

/// <summary>Recognizes project and solution markers by file name (REPO-003).</summary>
internal static class ProjectMarkerDetector
{
    /// <summary>Returns the marker a file name stands for, or <see langword="null"/>.</summary>
    /// <remarks>
    /// Extensions match case-insensitively (MSBuild and Windows treat them so); the fixed names of
    /// the Node and Python ecosystems match exactly, as their tools do on case-sensitive systems.
    /// </remarks>
    public static ProjectMarker? Detect(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        var extension = Path.GetExtension(fileName);
        if (extension.Equals(".sln", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectMarker.DotNetSolution;
        }

        if (extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectMarker.DotNetSolutionXml;
        }

        if (extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectMarker.DotNetProject;
        }

        return fileName switch
        {
            "package.json" => ProjectMarker.NodePackage,
            "pyproject.toml" => ProjectMarker.PythonProject,
            "setup.cfg" => ProjectMarker.PythonSetupConfig,
            "setup.py" => ProjectMarker.PythonSetupScript,
            _ when fileName.StartsWith("requirements", StringComparison.Ordinal)
                   && fileName.EndsWith(".txt", StringComparison.Ordinal) => ProjectMarker.PythonRequirements,
            _ => null,
        };
    }

    /// <summary>True for solution markers (classified as <c>SolutionFile</c>).</summary>
    public static bool IsSolution(this ProjectMarker marker) =>
        marker is ProjectMarker.DotNetSolution or ProjectMarker.DotNetSolutionXml;
}
