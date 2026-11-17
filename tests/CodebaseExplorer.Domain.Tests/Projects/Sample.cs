using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.Domain.Modules;
using CodebaseExplorer.Domain.Projects;

namespace CodebaseExplorer.Domain.Tests.Projects;

internal static class Sample
{
    public static readonly RepositoryId Repository = new(Guid.Parse("0193a1b2-c3d4-7e5f-8071-8293a4b5c6d7"));

    public static readonly ContentHash Hash =
        ContentHash.Parse("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");

    public static CodeProject DotNetProject(string projectFile = "src/Billing/Billing.csproj") =>
        CodeProject.Create(
            Repository,
            "Billing",
            ProjectKind.DotNetProject,
            Ecosystem.DotNet,
            LanguageId.CSharp,
            RelativePath.Create(projectFile),
            RelativePath.Create(Path.GetDirectoryName(projectFile)!.Replace('\\', '/') is { Length: > 0 } dir ? dir : "."));
}
