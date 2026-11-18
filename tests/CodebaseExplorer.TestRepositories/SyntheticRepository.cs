using System.Globalization;
using System.Text;

namespace CodebaseExplorer.TestRepositories;

/// <summary>
/// Generates large synthetic repositories for performance benchmarks (QA-002). Output goes to a caller-provided
/// (temporary) folder and is never committed. Content is deterministic for the same arguments, structured like real
/// code (projects reference lower-numbered projects, types use types of referenced projects, files import each other),
/// and contains nothing executable that the analysis could be tempted to run.
/// </summary>
public static class SyntheticRepository
{
    /// <summary>
    /// A .NET solution with <paramref name="projects"/> SDK-style projects in <c>src/P0000 …</c>. Project <c>i</c> references
    /// up to <paramref name="referencesPerProject"/> lower-numbered projects; every project has
    /// <paramref name="filesPerProject"/> C# files with two types each that use types of its own and of referenced
    /// projects (inheritance, fields, object creation, calls), so the Roslyn analyzer finds code relationships.
    /// </summary>
    /// <returns>Number of files written.</returns>
    public static int WriteDotNet(string root, int projects, int filesPerProject = 5, int referencesPerProject = 3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var random = new Random(projects * 31 + filesPerProject);
        var files = 0;
        var solution = new StringBuilder("<Solution>\n  <Folder Name=\"/src/\">\n");

        Write(root, "Directory.Build.props",
            "<Project>\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n    <Nullable>enable</Nullable>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n</Project>\n");
        files++;

        for (var p = 0; p < projects; p++)
        {
            var name = ProjectName(p);
            var references = References(random, p, referencesPerProject);
            var csproj = new StringBuilder("<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n");
            foreach (var reference in references)
            {
                csproj.Append(CultureInfo.InvariantCulture, $"    <ProjectReference Include=\"../{ProjectName(reference)}/{ProjectName(reference)}.csproj\" />\n");
            }

            csproj.Append(CultureInfo.InvariantCulture, $"    <PackageReference Include=\"Package{p % 40:D2}\" Version=\"1.0.{p % 7}\" />\n");
            csproj.Append("  </ItemGroup>\n</Project>\n");
            Write(root, $"src/{name}/{name}.csproj", csproj.ToString());
            solution.Append(CultureInfo.InvariantCulture, $"    <Project Path=\"src/{name}/{name}.csproj\" />\n");
            files++;

            for (var f = 0; f < filesPerProject; f++)
            {
                var code = new StringBuilder();
                code.Append(CultureInfo.InvariantCulture, $"namespace {name}.Area{f % 3};\n\n");
                var baseType = references.Count > 0 && f == 0 ? $" : {ProjectName(references[0])}.Area0.Service0" : string.Empty;
                code.Append(CultureInfo.InvariantCulture, $"public class Service{f}{baseType}\n{{\n");
                code.Append(CultureInfo.InvariantCulture, $"    private readonly Model{f} _model = new();\n\n");
                foreach (var reference in references.Take(2))
                {
                    code.Append(CultureInfo.InvariantCulture, $"    public {ProjectName(reference)}.Area1.Model1? Remote{reference} {{ get; set; }}\n");
                }

                code.Append(CultureInfo.InvariantCulture, $"\n    public int Compute{f}(int value) => _model.Value + value;\n");
                if (f > 0)
                {
                    code.Append(CultureInfo.InvariantCulture, $"\n    public int Delegate() => new {name}.Area{(f - 1) % 3}.Service{f - 1}().Compute{f - 1}(1);\n");
                }

                code.Append("}\n\n");
                code.Append(CultureInfo.InvariantCulture, $"public record Model{f}(int Value = {f});\n");
                Write(root, $"src/{name}/Area{f % 3}/Service{f}.cs", code.ToString());
                files++;
            }
        }

        solution.Append("  </Folder>\n</Solution>\n");
        Write(root, "Synthetic.slnx", solution.ToString());
        return files + 1;
    }

    /// <summary>
    /// An npm workspace with <paramref name="packages"/> TypeScript packages of <paramref name="filesPerPackage"/> files
    /// each (<c>packages/pkg0000 …</c>). Files import two files of their package (relative), one lower-numbered workspace
    /// package and an npm package; every package declares its dependencies.
    /// </summary>
    /// <returns>Number of files written.</returns>
    public static int WriteTypeScript(string root, int packages, int filesPerPackage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var random = new Random(packages * 17 + filesPerPackage);
        Write(root, "package.json", "{\n  \"name\": \"synthetic-monorepo\",\n  \"private\": true,\n  \"workspaces\": [\"packages/*\"]\n}\n");
        Write(root, "tsconfig.json", "{\n  \"compilerOptions\": { \"strict\": true, \"baseUrl\": \".\" }\n}\n");
        var files = 2;

        for (var p = 0; p < packages; p++)
        {
            var name = $"pkg{p:D4}";
            var dependency = p > 0 ? $"pkg{random.Next(p):D4}" : null;
            var dependencies = dependency is null
                ? $"\"lodash\": \"^4.17.21\""
                : $"\"@synthetic/{dependency}\": \"workspace:*\", \"lodash\": \"^4.17.21\"";
            Write(root, $"packages/{name}/package.json",
                $"{{\n  \"name\": \"@synthetic/{name}\",\n  \"version\": \"1.0.0\",\n  \"main\": \"src/index.ts\",\n  \"dependencies\": {{ {dependencies} }}\n}}\n");
            Write(root, $"packages/{name}/src/index.ts", $"export * from './feature0/file0';\n");
            files += 2;

            for (var f = 0; f < filesPerPackage; f++)
            {
                var code = new StringBuilder();
                if (f > 0)
                {
                    code.Append(CultureInfo.InvariantCulture, $"import {{ value{f - 1} }} from '../feature{(f - 1) / 20}/file{f - 1}';\n");
                }

                if (f > 1)
                {
                    code.Append(CultureInfo.InvariantCulture, $"import type {{ Shape{f - 2} }} from '../feature{(f - 2) / 20}/file{f - 2}';\n");
                }

                if (dependency is not null && f % 10 == 0)
                {
                    code.Append(CultureInfo.InvariantCulture, $"import {{ value0 as remote }} from '@synthetic/{dependency}';\n");
                }

                if (f % 25 == 0)
                {
                    code.Append("import groupBy from 'lodash/groupBy';\n");
                }

                code.Append(CultureInfo.InvariantCulture, $"\nexport interface Shape{f} {{ id: number; name: string }}\n\n");
                code.Append(CultureInfo.InvariantCulture, $"export const value{f} = {f};\n\n");
                code.Append(CultureInfo.InvariantCulture, $"export class Component{f} {{\n  render(shape: Shape{f}): string {{\n    return `${{shape.name}}-${{shape.id}}`;\n  }}\n}}\n\n");
                code.Append(CultureInfo.InvariantCulture, $"function helper{f}(input: number): number {{\n  return input * {f % 7 + 1};\n}}\n\nexport default helper{f};\n");
                Write(root, $"packages/{name}/src/feature{f / 20}/file{f}.ts", code.ToString());
                files++;
            }
        }

        return files;
    }

    private static string ProjectName(int index) => $"P{index:D4}";

    private static List<int> References(Random random, int project, int count)
    {
        var result = new SortedSet<int>();
        for (var i = 0; i < count && project > 0; i++)
        {
            result.Add(random.Next(project));
        }

        return [.. result];
    }

    private static void Write(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal));
    }
}
