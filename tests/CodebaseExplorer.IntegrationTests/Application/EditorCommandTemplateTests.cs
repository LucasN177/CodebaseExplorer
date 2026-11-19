using CodebaseExplorer.Application.Settings;
using CodebaseExplorer.Desktop.Services;

namespace CodebaseExplorer.IntegrationTests.Application;

/// <summary>
/// UI-009 acceptance: the editor is started without a shell; a path with special characters is always exactly one
/// argument and is never interpreted (SECURITY.md §4).
/// </summary>
public sealed class EditorCommandTemplateTests
{
    [Theory]
    [InlineData("/home/u/my repo/src/a b.cs")]
    [InlineData("C:\\Users\\x\\repo\\it's \"quoted\".cs")]
    [InlineData("/tmp/repo/a; rm -rf ~; .cs")]
    [InlineData("/tmp/repo/$(whoami)`id`&calc|more>out.cs")]
    [InlineData("/tmp/repo/%PATH% ^& !x! {line}.cs")]
    public void PathWithSpecialCharacters_StaysOneLiteralArgument(string path)
    {
        var template = EditorCommandTemplate.Parse("code --goto \"{path}:{line}\" --reuse-window").Value;

        var launch = template.Build(path, 42);
        var info = EditorLauncher.CreateStartInfo(launch);

        Assert.Equal("code", launch.FileName);
        Assert.Equal(["--goto", $"{path}:42", "--reuse-window"], launch.Arguments);
        Assert.False(info.UseShellExecute);
        Assert.Equal("code", info.FileName);
        Assert.Equal(launch.Arguments, info.ArgumentList);
        Assert.Empty(info.Arguments); // never a pre-joined command line
    }

    [Theory]
    [InlineData("rider --line {line} \"{path}\"", "rider", new[] { "--line", "7", "/r/a.cs" })]
    [InlineData("notepad++ -n{line} -c{column} {path}", "notepad++", new[] { "-n7", "-c1", "/r/a.cs" })]
    [InlineData("\"C:\\Program Files\\Editor\\ed.exe\" {path}", "C:\\Program Files\\Editor\\ed.exe", new[] { "/r/a.cs" })]
    public void Templates_AreSplitWithoutAShell(string text, string program, string[] arguments)
    {
        var launch = EditorCommandTemplate.Parse(text).Value.Build("/r/a.cs", 7);

        Assert.Equal(program, launch.FileName);
        Assert.Equal(arguments, launch.Arguments);
    }

    [Theory]
    [InlineData("")]
    [InlineData("code")]
    [InlineData("code --goto \"{path}")]
    [InlineData("{path}")]
    [InlineData("code {file}")]
    [InlineData("code {path}\nrm -rf /")]
    public void InvalidTemplates_AreRejected(string text) =>
        Assert.Equal(EditorErrorCodes.TemplateInvalid, EditorCommandTemplate.Parse(text).Error.Code);
}
