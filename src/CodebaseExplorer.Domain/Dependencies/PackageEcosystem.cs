namespace CodebaseExplorer.Domain.Dependencies;

/// <summary>Package registry or framework family of an external dependency.</summary>
public enum PackageEcosystem
{
    NuGet,
    Npm,
    PyPI,
    DotNetFramework,
    Unknown,
}
