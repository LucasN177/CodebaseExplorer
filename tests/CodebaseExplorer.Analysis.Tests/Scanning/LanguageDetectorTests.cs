using System.Text;
using CodebaseExplorer.Analysis.Scanning;

namespace CodebaseExplorer.Analysis.Tests.Scanning;

public sealed class LanguageDetectorTests
{
    [Theory]
    [InlineData("A.cs", "csharp")]
    [InlineData("script.csx", "csharp")]
    [InlineData("A.fs", "fsharp")]
    [InlineData("A.fsi", "fsharp")]
    [InlineData("A.fsx", "fsharp")]
    [InlineData("A.vb", "vb")]
    [InlineData("Index.razor", "razor")]
    [InlineData("Index.cshtml", "razor")]
    [InlineData("App.xaml", "xaml")]
    [InlineData("App.axaml", "xaml")]
    [InlineData("Shop.sln", "sln")]
    [InlineData("Shop.slnx", "xml")]
    [InlineData("A.csproj", "xml")]
    [InlineData("A.fsproj", "xml")]
    [InlineData("A.vbproj", "xml")]
    [InlineData("Directory.Build.props", "xml")]
    [InlineData("Custom.targets", "xml")]
    [InlineData("Strings.resx", "xml")]
    [InlineData("web.config", "xml")]
    [InlineData("data.xml", "xml")]
    [InlineData("A.nuspec", "xml")]
    [InlineData("app.ts", "typescript")]
    [InlineData("app.mts", "typescript")]
    [InlineData("app.cts", "typescript")]
    [InlineData("App.tsx", "tsx")]
    [InlineData("app.js", "javascript")]
    [InlineData("app.mjs", "javascript")]
    [InlineData("app.cjs", "javascript")]
    [InlineData("App.jsx", "javascript")]
    [InlineData("main.py", "python")]
    [InlineData("stubs.pyi", "python")]
    [InlineData("gui.pyw", "python")]
    [InlineData("package.json", "json")]
    [InlineData("tsconfig.jsonc", "json")]
    [InlineData("ci.yml", "yaml")]
    [InlineData("ci.yaml", "yaml")]
    [InlineData("pyproject.toml", "toml")]
    [InlineData("settings.ini", "ini")]
    [InlineData("setup.cfg", "ini")]
    [InlineData("README.md", "markdown")]
    [InlineData("guide.markdown", "markdown")]
    [InlineData("index.rst", "restructuredtext")]
    [InlineData("requirements.txt", "text")]
    [InlineData("index.html", "html")]
    [InlineData("index.htm", "html")]
    [InlineData("site.css", "css")]
    [InlineData("site.scss", "scss")]
    [InlineData("build.sh", "shell")]
    [InlineData("build.bash", "shell")]
    [InlineData("build.ps1", "powershell")]
    [InlineData("Module.psm1", "powershell")]
    [InlineData("schema.sql", "sql")]
    public void Detect_ByExtension(string fileName, string expected) =>
        Assert.Equal(expected, LanguageDetector.Detect(fileName, []).Value);

    [Theory]
    [InlineData("Dockerfile", "dockerfile")]
    [InlineData("Makefile", "makefile")]
    [InlineData("CMakeLists.txt", "cmake")]
    [InlineData(".gitignore", "gitignore")]
    [InlineData(".editorconfig", "editorconfig")]
    [InlineData("global.json", "json")]
    public void Detect_ByWellKnownFileName(string fileName, string expected) =>
        Assert.Equal(expected, LanguageDetector.Detect(fileName, []).Value);

    [Theory]
    [InlineData("A.CS", "csharp")]
    [InlineData("APP.TS", "typescript")]
    [InlineData("dockerfile", "dockerfile")]
    public void Detect_IsCaseInsensitive(string fileName, string expected) =>
        Assert.Equal(expected, LanguageDetector.Detect(fileName, []).Value);

    [Theory]
    [InlineData("#!/usr/bin/env python3\nprint(1)", "python")]
    [InlineData("#!/usr/bin/python\n", "python")]
    [InlineData("#!/usr/bin/env node\n", "javascript")]
    [InlineData("#!/usr/bin/env -S deno run\n", "typescript")]
    [InlineData("#!/bin/bash\n", "shell")]
    [InlineData("#!/bin/sh", "shell")]
    [InlineData("#!/usr/bin/env pwsh\r\n", "powershell")]
    public void Detect_ShebangForFilesWithoutExtension(string head, string expected) =>
        Assert.Equal(expected, LanguageDetector.Detect("run", Encoding.ASCII.GetBytes(head)).Value);

    [Theory]
    [InlineData("data.xyz", "")]
    [InlineData("LICENSE", "")]
    [InlineData("run", "echo hello")]
    [InlineData("run", "#!/usr/bin/env ruby\n")]
    [InlineData("run", "#!")]
    [InlineData(".env", "")]
    [InlineData("archive.tar.gz", "")]
    public void Detect_UnknownFiles_AreUnknown(string fileName, string head) =>
        Assert.Equal("unknown", LanguageDetector.Detect(fileName, Encoding.ASCII.GetBytes(head)).Value);

    [Fact]
    public void Detect_ShebangIsIgnoredWhenAnExtensionExists() =>
        Assert.Equal("unknown", LanguageDetector.Detect("tool.xyz", "#!/usr/bin/env python3\n"u8).Value);
}
