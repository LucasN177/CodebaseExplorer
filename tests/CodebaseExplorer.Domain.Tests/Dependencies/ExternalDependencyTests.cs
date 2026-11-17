using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Dependencies;
using CodebaseExplorer.Domain.Tests.Projects;

namespace CodebaseExplorer.Domain.Tests.Dependencies;

public sealed class ExternalDependencyTests
{
    [Theory]
    [InlineData(PackageEcosystem.NuGet, "Serilog", "serilog")]
    [InlineData(PackageEcosystem.PyPI, "PyYAML", "pyyaml")]
    [InlineData(PackageEcosystem.PyPI, "zope.interface", "zope-interface")]
    [InlineData(PackageEcosystem.Npm, "@angular/core", "@angular/core")]
    [InlineData(PackageEcosystem.DotNetFramework, "Microsoft.AspNetCore.App", "microsoft.aspnetcore.app")]
    public void Create_NormalizesNameAndKeepsDisplayName(PackageEcosystem ecosystem, string displayName, string expectedName)
    {
        var dependency = ExternalDependency.Create(Sample.Repository, ecosystem, displayName);

        Assert.Equal(expectedName, dependency.Name);
        Assert.Equal(displayName, dependency.DisplayName);
        Assert.Equal(NaturalKey.ForExternalDependency(Sample.Repository, ecosystem.ToKeyToken(), displayName), dependency.NaturalKey);
    }

    [Fact]
    public void Create_SpellingVariants_ShareIdentity()
    {
        var first = ExternalDependency.Create(Sample.Repository, PackageEcosystem.NuGet, "Newtonsoft.Json");
        var second = ExternalDependency.Create(Sample.Repository, PackageEcosystem.NuGet, "newtonsoft.json");

        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public void Create_SameNameInDifferentEcosystems_DiffersInIdentity() =>
        Assert.NotEqual(
            ExternalDependency.Create(Sample.Repository, PackageEcosystem.Npm, "yaml").Id,
            ExternalDependency.Create(Sample.Repository, PackageEcosystem.PyPI, "yaml").Id);

    [Fact]
    public void Create_RejectsInvalidInput()
    {
        Assert.Throws<ArgumentException>(() => ExternalDependency.Create(default, PackageEcosystem.NuGet, "A"));
        Assert.ThrowsAny<ArgumentException>(() => ExternalDependency.Create(Sample.Repository, PackageEcosystem.NuGet, " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExternalDependency.Create(Sample.Repository, (PackageEcosystem)9, "A"));
    }

    [Theory]
    [InlineData(PackageEcosystem.NuGet, "nuget")]
    [InlineData(PackageEcosystem.Npm, "npm")]
    [InlineData(PackageEcosystem.PyPI, "pypi")]
    [InlineData(PackageEcosystem.DotNetFramework, "dotnet-framework")]
    [InlineData(PackageEcosystem.Unknown, "unknown")]
    public void PackageEcosystem_HasStableKeyToken(PackageEcosystem ecosystem, string token) =>
        Assert.Equal(token, ecosystem.ToKeyToken());
}
