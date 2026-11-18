using CodebaseExplorer.Analysis.Languages.TypeScript;
using CodebaseExplorer.Domain.Entities;
using CodebaseExplorer.Domain.Runs;
using CodebaseExplorer.TestRepositories;

namespace CodebaseExplorer.Analysis.Tests.Languages.TypeScript;

/// <summary>ANALYSIS-010 edge cases with temporary repositories.</summary>
public sealed class TypeScriptAnalyzerTests : IDisposable
{
    private readonly TempDirectory _repository = new();

    public void Dispose() => _repository.Dispose();

    [Fact]
    public async Task Declarations_cover_kinds_modifiers_export_forms_and_overloads()
    {
        _repository.WriteText("package.json", "{ \"name\": \"p\" }");
        _repository.WriteText("src/a.ts", string.Join('\n',
            "export abstract class Base<T> {}",
            "export enum Color { Red }",
            "export async function load(): Promise<void> {}",
            "function over(a: string): void;",
            "function over(a: number): void;",
            "function over(a: unknown): void {}",
            "const local = 1;",
            "let counter = 0;",
            "export { over, counter as count };",
            "export const VERSION = '1', handler = async () => {};",
            "export let mutable = 2;",
            "declare const ambient: number;",
            "export default class {}"));

        var model = (await TypeScriptFixtureTests.AnalyzeAsync(_repository.FullPath)).Model;

        Assert.Equal(
            [
                "p Base Class Exported src/a.ts#Base src/a.ts:1:23",
                "p Color Enum Exported src/a.ts#Color src/a.ts:2:13",
                "p VERSION Constant Exported src/a.ts#VERSION src/a.ts:10:14",
                "p default Class Exported src/a.ts#default src/a.ts:13:16",
                "p handler Function Exported src/a.ts#handler src/a.ts:10:29",
                "p load Function Exported src/a.ts#load src/a.ts:3:23",
                "p mutable Variable Exported src/a.ts#mutable src/a.ts:11:12",
                "p over Function Exported src/a.ts#over src/a.ts:6:10",
            ],
            TypeScriptFixtureTests.Entities(model));
        Assert.Equal(EntityModifiers.Abstract | EntityModifiers.Generic, model.Entities.Single(e => e.Name == "Base").Modifiers);
        Assert.Equal(EntityModifiers.Async, model.Entities.Single(e => e.Name == "load").Modifiers);
        Assert.Equal(EntityModifiers.Async, model.Entities.Single(e => e.Name == "handler").Modifiers);
    }

    [Fact]
    public async Task Tsconfig_extends_base_url_and_emitted_js_specifiers_resolve()
    {
        _repository.WriteText("package.json", "{ \"name\": \"app\" }");
        _repository.WriteText("configs/base.json", "{ \"compilerOptions\": { \"baseUrl\": \"../src\" } }");
        _repository.WriteText("tsconfig.json", "{ \"extends\": \"./configs/base\" }");
        _repository.WriteText("src/util/math.ts", "export const add = (a: number, b: number) => a + b;");
        _repository.WriteText("src/main.ts", "import { add } from 'util/math';\nimport './util/math.js';\nimport fs from 'node:fs';\nimport path from 'path/posix';\nimport x from 'https://example.invalid/x.js';");

        var analysis = await TypeScriptFixtureTests.AnalyzeAsync(_repository.FullPath);

        Assert.Equal(
            [
                "src/main.ts -> src/util/math.ts ts.import.path-alias Confirmed 1:1",
                "src/main.ts -> src/util/math.ts ts.import.relative Confirmed 2:1",
            ],
            TypeScriptFixtureTests.Imports(analysis.Model));
        Assert.Equal([TypeScriptCodes.ImportUnresolved], analysis.Diagnostics.Select(d => d.Code)); // the URL import
    }

    [Fact]
    public async Task Workspace_subpaths_scoped_packages_and_dynamic_computed_imports()
    {
        _repository.WriteText("libs/ui/package.json", "{ \"name\": \"@org/ui\", \"main\": \"./dist/index.js\" }");
        _repository.WriteText("libs/ui/index.tsx", "export function Button() { return null; }");
        _repository.WriteText("libs/ui/forms/input.tsx", "export function Input() { return null; }");
        _repository.WriteText("app/package.json", "{ \"name\": \"app\", \"dependencies\": { \"@org/other\": \"1\" } }");
        _repository.WriteText("app/main.ts", string.Join('\n',
            "import { Button } from '@org/ui';",
            "import { Input } from '@org/ui/forms/input';",
            "import { Thing } from '@org/other/deep/path';",
            "const name = 'x';",
            "import(name);",
            "import(`./static`);"));

        var analysis = await TypeScriptFixtureTests.AnalyzeAsync(_repository.FullPath);

        Assert.Equal(
            [
                "app/main.ts -> libs/ui/forms/input.tsx ts.import.workspace Confirmed 2:1",
                "app/main.ts -> libs/ui/index.tsx ts.import.workspace Confirmed 1:1",
                "app/main.ts -> npm:@org/other ts.import.package Syntactic 3:1",
            ],
            TypeScriptFixtureTests.Imports(analysis.Model));
        Assert.Equal(
            [TypeScriptCodes.ImportComputed, TypeScriptCodes.ImportUnresolved],
            analysis.Diagnostics.Select(d => d.Code).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Hoisted_root_dependencies_count_as_declared_for_workspace_members()
    {
        _repository.WriteText("package.json", "{ \"name\": \"root\", \"workspaces\": [\"pkgs/*\"], \"devDependencies\": { \"jest\": \"29\" } }");
        _repository.WriteText("pkgs/a/package.json", "{ \"name\": \"a\" }");
        _repository.WriteText("pkgs/a/a.test.js", "const { expect } = require('jest');");

        var model = (await TypeScriptFixtureTests.AnalyzeAsync(_repository.FullPath)).Model;

        Assert.Equal(["pkgs/a/a.test.js -> npm:jest ts.require.package Syntactic 1:20"], TypeScriptFixtureTests.Imports(model));
    }

    [Fact]
    public async Task Syntax_errors_and_oversized_files_are_reported_and_still_become_modules()
    {
        _repository.WriteText("package.json", "{ \"name\": \"p\" }");
        _repository.WriteText("broken.ts", "export class {{{ import x from './y'");
        _repository.WriteText("huge.js", "// " + new string('x', 2_200_000));

        var analysis = await TypeScriptFixtureTests.AnalyzeAsync(_repository.FullPath);

        Assert.Equal(["broken.ts", "huge.js"], analysis.Model.Modules.Select(m => m.QualifiedName).Order(StringComparer.Ordinal));
        Assert.Contains(analysis.Diagnostics, d => d is { Code: TypeScriptCodes.SyntaxErrors, Severity: DiagnosticSeverity.Info });
        Assert.Contains(analysis.Diagnostics, d => d is { Code: TypeScriptCodes.FileNotRead, Severity: DiagnosticSeverity.Info });
    }

    [Fact]
    public async Task Imports_of_files_outside_any_project_are_reported()
    {
        _repository.WriteText("shared/helpers.js", "module.exports = {};");
        _repository.WriteText("app/package.json", "{ \"name\": \"app\" }");
        _repository.WriteText("app/index.js", "require('../shared/helpers');\nrequire('./index');");

        var analysis = await TypeScriptFixtureTests.AnalyzeAsync(_repository.FullPath);

        Assert.Empty(TypeScriptFixtureTests.Imports(analysis.Model)); // the self-import produces nothing
        Assert.Equal(TypeScriptCodes.ImportOutsideProjects, Assert.Single(analysis.Diagnostics).Code);
    }
}
