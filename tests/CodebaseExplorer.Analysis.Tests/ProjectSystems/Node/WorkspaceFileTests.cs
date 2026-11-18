using CodebaseExplorer.Analysis.ProjectSystems.Node;

namespace CodebaseExplorer.Analysis.Tests.ProjectSystems.Node;

public sealed class WorkspaceFileTests
{
    [Theory]
    [InlineData("packages/*", "packages/core", true)]
    [InlineData("packages/*", "packages/core/nested", false)]
    [InlineData("packages/**", "packages/core/nested", true)]
    [InlineData("packages/**/lib", "packages/lib", true)]
    [InlineData("packages/**/lib", "packages/a/b/lib", true)]
    [InlineData("./apps/web", "apps/web", true)]
    [InlineData("apps/web/", "apps/web", true)]
    [InlineData("apps/*", "Apps/web", false)]
    [InlineData("pkg-?", "pkg-a", true)]
    [InlineData("a.b/*", "aXb/c", false)]
    public void Patterns_match_folders(string pattern, string folder, bool expected) =>
        Assert.Equal(expected, new WorkspacePatterns([pattern]).Matches(folder));

    [Fact]
    public void Negated_patterns_exclude() =>
        Assert.False(new WorkspacePatterns(["packages/*", "!packages/legacy"]).Matches("packages/legacy"));

    [Fact]
    public void The_workspace_root_itself_is_never_a_member() =>
        Assert.False(new WorkspacePatterns(["**"]).Matches(string.Empty));

    [Fact]
    public void Pnpm_block_sequence_with_quotes_and_comments()
    {
        const string yaml = "# pnpm\npackages:\n  - 'packages/*'\n  - \"apps/**\"   # apps\n  - '!**/test/**'\n  - tools\ncatalog:\n  react: ^19\n";

        var packages = PnpmWorkspaceFile.ReadPackages(yaml);

        Assert.Equal(["packages/*", "apps/**", "!**/test/**", "tools"], packages.Select(p => p.Value));
        Assert.Equal((3, 3), (packages[0].Line, packages[0].Column));
    }

    [Fact]
    public void Pnpm_flow_sequence() =>
        Assert.Equal(["a/*", "b"], PnpmWorkspaceFile.ReadPackages("packages: ['a/*', b]\n").Select(p => p.Value));

    [Fact]
    public void Pnpm_without_packages_key_yields_nothing() =>
        Assert.Empty(PnpmWorkspaceFile.ReadPackages("catalog:\n  - nope\n"));

    [Fact]
    public void Pnpm_items_of_other_keys_are_ignored() =>
        Assert.Equal(["x"], PnpmWorkspaceFile.ReadPackages("onlyBuiltDependencies:\n  - esbuild\npackages:\n  - x\n").Select(p => p.Value));
}
