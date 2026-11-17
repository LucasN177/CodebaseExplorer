# Fixture `dotnet-layered` — expected analysis results

Oracle for ANALYSIS-002 … ANALYSIS-004 (and the type list for ANALYSIS-005/006). Tests assert
against this document, not against current analyzer output. Paths are repository-relative with `/`.
Locations are `line:column` (1-based) of the first character (`<`) of the XML element. All files
use LF line endings and are stored byte-exact (`.gitattributes`: `-text`).

## 1. Files with project markers

| Path | Marker | Classification |
|---|---|---|
| `Shop.sln` | `DotNetSolution` | `SolutionFile` |
| `Shop.slnx` | `DotNetSolutionXml` | `SolutionFile` |
| `src/Shop.Domain/Shop.Domain.csproj` | `DotNetProject` | `ProjectManifest` |
| `src/Shop.Application/Shop.Application.csproj` | `DotNetProject` | `ProjectManifest` |
| `src/Shop.Infrastructure/Shop.Infrastructure.csproj` | `DotNetProject` | `ProjectManifest` |
| `src/Shop.Web/Shop.Web.csproj` | `DotNetProject` | `ProjectManifest` |
| `tests/Shop.Tests/Shop.Tests.csproj` | `DotNetProject` | `ProjectManifest` |

`Directory.Build.props` and `Directory.Packages.props` are `Configuration` files (no marker).
`EXPECTED.md` itself is `Documentation`; analyzers must not treat it specially.

## 2. Solutions and solution folders

Both solution files describe the same layout; parsing either must give identical memberships.

| Project file | Solution folder in `Shop.slnx` | Solution folder in `Shop.sln` |
|---|---|---|
| `src/Shop.Domain/Shop.Domain.csproj` | `/src/` (line 3) | `src` (line 10, nested via `NestedProjects`) |
| `src/Shop.Application/Shop.Application.csproj` | `/src/` (line 4) | `src` (line 12) |
| `src/Shop.Infrastructure/Shop.Infrastructure.csproj` | `/src/` (line 5) | `src` (line 14) |
| `src/Shop.Web/Shop.Web.csproj` | `/src/` (line 6) | `src` (line 16) |
| `tests/Shop.Tests/Shop.Tests.csproj` | `/tests/` (line 9) | `tests` (line 18) |

The `.sln` contains two solution-folder entries (`src`, `tests`, lines 6 and 8) that are **not**
projects. `Shop.Legacy` is in neither solution.

## 3. Projects

All projects: ecosystem `DotNet`, kind `DotNetProject`, primary language `csharp`, SDK
`Microsoft.NET.Sdk`. Properties after static resolution of `Directory.Build.props`:

| Project | Root directory | Target framework(s) | Other properties |
|---|---|---|---|
| `Shop.Domain` | `src/Shop.Domain` | `net10.0;net8.0` (`TargetFrameworks`, line 4 — overrides the inherited `TargetFramework`) | `RootNamespace=Shop.Domain` |
| `Shop.Application` | `src/Shop.Application` | `net10.0` (inherited, `Directory.Build.props` line 3) | — |
| `Shop.Infrastructure` | `src/Shop.Infrastructure` | `net10.0` (inherited) | `UseSqlServer=false` (inherited, `Directory.Build.props` line 6) |
| `Shop.Web` | `src/Shop.Web` | `net10.0` (inherited) | `OutputType=Exe` (line 4), `AssemblyName=ShopWeb` (line 5) |
| `Shop.Tests` | `tests/Shop.Tests` | `net10.0` (inherited) | `IsPackable=false` (line 4); classified as a test project by location |

Inherited `Nullable=enable` and `ImplicitUsings=enable` apply to all projects.

## 4. Project references (`ProjectReference`)

| Source project | Target project | Evidence location | Level |
|---|---|---|---|
| `Shop.Application` | `Shop.Domain` | `src/Shop.Application/Shop.Application.csproj:4:5` | `Confirmed` |
| `Shop.Infrastructure` | `Shop.Application` | `src/Shop.Infrastructure/Shop.Infrastructure.csproj:4:5` | `Confirmed` |
| `Shop.Infrastructure` | `Shop.Domain` | `src/Shop.Infrastructure/Shop.Infrastructure.csproj:5:5` | `Confirmed` |
| `Shop.Web` | `Shop.Application` | `src/Shop.Web/Shop.Web.csproj:13:5` | `Confirmed` |
| `Shop.Web` | `Shop.Infrastructure` | `src/Shop.Web/Shop.Web.csproj:14:5` | `Confirmed` |
| `Shop.Tests` | `Shop.Domain` | `tests/Shop.Tests/Shop.Tests.csproj:13:5` | `Confirmed` |
| `Shop.Tests` | `Shop.Application` | `tests/Shop.Tests/Shop.Tests.csproj:14:5` | `Confirmed` |

Rule id for all: `csproj.ProjectReference`; analyzer `dotnet.msbuild-static`.

**Negative case — missing project:** `src/Shop.Infrastructure/Shop.Infrastructure.csproj:6:5`
references `../Shop.Legacy/Shop.Legacy.csproj`, which does not exist. Expected: the reference is
kept as `Syntactic` (target resolved by path only; no `CodeProject` for `Shop.Legacy` exists, so the
graph shows it as an unresolved target) **and** a `Warning` diagnostic naming the missing file and
the evidence location. ANALYSIS-004 decides how the unresolved target is represented (an
`ExternalDependency` with ecosystem `Unknown` is *not* acceptable — it is not a package) and records
that decision in ANALYSIS_ENGINE.md; this fixture only fixes the observable outcome above.

## 5. Package references (`PackageReference`) with central package management

`Directory.Packages.props` enables CPM (line 3). Versions resolve from `PackageVersion` items unless
a `VersionOverride` is given.

| Project | Package (NuGet) | Version spec | Version source | Evidence location |
|---|---|---|---|---|
| `Shop.Application` | `Microsoft.Extensions.DependencyInjection.Abstractions` | `10.0.15` | `Directory.Packages.props:6:5` | `src/Shop.Application/Shop.Application.csproj:8:5` |
| `Shop.Infrastructure` | `Microsoft.EntityFrameworkCore.Sqlite` | `10.0.15` | `Directory.Packages.props:7:5` | `src/Shop.Infrastructure/Shop.Infrastructure.csproj:10:5` |
| `Shop.Tests` | `xunit.v3` | `4.0.3` | `Directory.Packages.props:9:5` | `tests/Shop.Tests/Shop.Tests.csproj:8:5` |
| `Shop.Tests` | `NSubstitute` | `5.3.0` | `VersionOverride` (same element) | `tests/Shop.Tests/Shop.Tests.csproj:9:5` |

All: relationship kind `PackageReference`, level `Confirmed`, rule `csproj.PackageReference`,
attribute `versionSpec` as listed, `devOnly` not set (NuGet has no dev-dependency flag here).

**Conditional reference — must not appear:** `Microsoft.EntityFrameworkCore.SqlServer` in
`src/Shop.Infrastructure/Shop.Infrastructure.csproj:14:5` is inside
`<ItemGroup Condition="'$(UseSqlServer)' == 'true'">` (line 13). `UseSqlServer` statically resolves
to `false` (`Directory.Build.props:6`), so the condition is false and **no** relationship to
`Microsoft.EntityFrameworkCore.SqlServer` is produced. The `PackageVersion` for it
(`Directory.Packages.props:8`) alone does not create a dependency.

Expected external dependencies (NuGet, normalized names): `microsoft.extensions.dependencyinjection.abstractions`,
`microsoft.entityframeworkcore.sqlite`, `xunit.v3`, `nsubstitute`.

## 6. Framework references (`FrameworkReference`)

| Project | Framework | Evidence location | Level |
|---|---|---|---|
| `Shop.Web` | `Microsoft.AspNetCore.App` | `src/Shop.Web/Shop.Web.csproj:9:5` | `Confirmed` |

External dependency: ecosystem `DotNetFramework`, normalized name `microsoft.aspnetcore.app`;
relationship kind `FrameworkReference`, rule `csproj.FrameworkReference`.

## 7. Expected diagnostics (project-system analysis)

| Severity | About | Location |
|---|---|---|
| `Warning` | Referenced project file `src/Shop.Legacy/Shop.Legacy.csproj` does not exist | `src/Shop.Infrastructure/Shop.Infrastructure.csproj:6:5` |

No other warnings or errors are expected from ANALYSIS-002 … ANALYSIS-004.

## 8. Types (for ANALYSIS-005/006, informational for M1)

| Project | Namespace | Type | Kind | Declaration |
|---|---|---|---|---|
| `Shop.Domain` | `Shop.Domain.Orders` | `Order` | class | `src/Shop.Domain/Orders/Order.cs:3` |
| `Shop.Domain` | `Shop.Domain.Orders` | `OrderLine` | record | `src/Shop.Domain/Orders/OrderLine.cs:3` |
| `Shop.Domain` | `Shop.Domain.Orders` | `IOrderRepository` | interface | `src/Shop.Domain/Orders/IOrderRepository.cs:3` |
| `Shop.Application` | `Shop.Application.Orders` | `PlaceOrderHandler` | class | `src/Shop.Application/Orders/PlaceOrderHandler.cs:5` |
| `Shop.Application` | `Shop.Application` | `DependencyInjection` | static class | `src/Shop.Application/DependencyInjection.cs:5` |
| `Shop.Infrastructure` | `Shop.Infrastructure.Persistence` | `SqliteOrderRepository` | class | `src/Shop.Infrastructure/Persistence/SqliteOrderRepository.cs:5` |
| `Shop.Infrastructure` | `Shop.Infrastructure.Persistence` | `ShopDbContext` | class | `src/Shop.Infrastructure/Persistence/ShopDbContext.cs:6` |
| `Shop.Web` | (global) | `Program` | class (top-level statements) | `src/Shop.Web/Program.cs:1` |
| `Shop.Tests` | `Shop.Tests` | `PlaceOrderHandlerTests` | class | `tests/Shop.Tests/PlaceOrderHandlerTests.cs:8` |

Key type relationships (detailed expectations are added by ANALYSIS-006): `SqliteOrderRepository`
implements `IOrderRepository`; `PlaceOrderHandler` uses `IOrderRepository` and instantiates `Order`;
`ShopDbContext` inherits `Microsoft.EntityFrameworkCore.DbContext` (external, unresolved without
restored packages); `DependencyInjection.AddApplication` registers `PlaceOrderHandler` (heuristic,
ANALYSIS-007).
