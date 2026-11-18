# Fixture `dotnet-advanced` — expected analysis results

Oracle for ANALYSIS-005 (modules, entities), ANALYSIS-006 (semantic relationships), ANALYSIS-007 (DI
heuristics) and additional regression input for the M1 project-system analysis (ANALYSIS-002 …
ANALYSIS-004). Tests assert against this document, not against current analyzer output. Paths are
repository-relative with `/`. Locations are `line:column` (1-based). All files use LF line endings and
are stored byte-exact (`.gitattributes`: `-text`).

**Location conventions.** Manifest items (§3–§6): first character (`<`) of the XML element. Entity
declarations (§8): first character of the declared **identifier** (for constructors the type name, for
partial types every part). Relationship evidence (§9): first character of the type syntax for
`Inherits`, `Implements`, `UsesType` and `Instantiates` (the type name after `new`, not `new` itself; the
`new` keyword only for target-typed `new()`), of the invoked member name for `Calls` (`base`/`this` for
constructor initializers). §10: start of the invocation.

## 1. Files with project markers

| Path | Marker | Classification |
|---|---|---|
| `Advanced.slnx` | `DotNetSolutionXml` | `SolutionFile` |
| `src/Advanced.Core/Advanced.Core.csproj` | `DotNetProject` | `ProjectManifest` |
| `src/Advanced.Services/Advanced.Services.csproj` | `DotNetProject` | `ProjectManifest` |

`Directory.Build.props` and `Directory.Packages.props` are `Configuration`, `EXPECTED.md` is
`Documentation`; the 10 `.cs` files are `Source`. Total: 16 files.

## 2. Solution

| Project file | Solution folder in `Advanced.slnx` |
|---|---|
| `src/Advanced.Core/Advanced.Core.csproj` | `/src/` (line 3) |
| `src/Advanced.Services/Advanced.Services.csproj` | `/src/` (line 4) |

## 3. Projects

Both real projects: ecosystem `DotNet`, kind `DotNetProject`, primary language `csharp`, SDK
`Microsoft.NET.Sdk`; inherited from `Directory.Build.props`: `Nullable=enable`, `ImplicitUsings=enable`,
`EnableLogging=true` (line 6), `EnableTelemetry=false` (line 7).

| Project | Root directory | Target framework(s) | Other properties |
|---|---|---|---|
| `Advanced.Core` | `src/Advanced.Core` | `net10.0;netstandard2.0` (`TargetFrameworks`, line 4 — multi-targeting; the inherited `TargetFramework=net10.0` stays as a property) | `LangVersion=latest` (line 5) |
| `Advanced.Services` | `src/Advanced.Services` | `net10.0` (inherited, `Directory.Build.props` line 3) | — |

**Missing project.** `src/Advanced.Services/Advanced.Services.csproj:5:5` references
`../Advanced.Plugins/Advanced.Plugins.csproj`, which does not exist. As in `dotnet-layered` (ANALYSIS-004):
a placeholder project `Advanced.Plugins`, kind `Unknown`, ecosystem `DotNet`, root directory
`src/Advanced.Plugins`, no project file, properties `resolution=missing` and
`referencedPath=src/Advanced.Plugins/Advanced.Plugins.csproj`. The model contains **3** projects (2 real +
1 placeholder).

## 4. Project references

| Source project | Target project | Evidence location | Level |
|---|---|---|---|
| `Advanced.Services` | `Advanced.Core` | `src/Advanced.Services/Advanced.Services.csproj:4:5` | `Confirmed` |
| `Advanced.Services` | `Advanced.Plugins` (placeholder) | `src/Advanced.Services/Advanced.Services.csproj:5:5` | `Syntactic` |

## 5. Package references (central package management)

`Directory.Packages.props` enables CPM (line 3).

| Project | Package (NuGet) | Version spec | Version source | Evidence location |
|---|---|---|---|---|
| `Advanced.Core` | `System.Memory` | `4.6.3` | `Directory.Packages.props:10:5` | `src/Advanced.Core/Advanced.Core.csproj:9:5` |
| `Advanced.Services` | `Microsoft.Extensions.DependencyInjection.Abstractions` | `10.0.15` | `Directory.Packages.props:6:5` | `src/Advanced.Services/Advanced.Services.csproj:9:5` |
| `Advanced.Services` | `Newtonsoft.Json` | `13.0.3` | `VersionOverride` (same element; central version `13.0.4` is not used) | `src/Advanced.Services/Advanced.Services.csproj:10:5` |
| `Advanced.Services` | `Serilog` | `4.3.0` | `Directory.Packages.props:8:5` | `src/Advanced.Services/Advanced.Services.csproj:14:5` |

All: kind `PackageReference`, level `Confirmed`, rule `csproj.PackageReference`, analyzer
`dotnet.msbuild-static`.

## 6. Conditions

| Element | Condition | Static result | Effect |
|---|---|---|---|
| `src/Advanced.Services/Advanced.Services.csproj:13:3` | `'$(EnableLogging)' == 'true'` | true (`Directory.Build.props:6`) | `Serilog` reference is kept (§5). |
| `src/Advanced.Services/Advanced.Services.csproj:17:3` | `'$(EnableTelemetry)' == 'true'` | false (`Directory.Build.props:7`) | No `OpenTelemetry` reference. |
| `src/Advanced.Services/Advanced.Services.csproj:21:3` | `Exists('local.packages.props')` | not evaluated (functions are unsupported) | Element skipped, `Warning` `dotnet.msbuild.unsupported-condition` (§7). No `OpenTelemetry` reference. |

The `PackageVersion` entry for `OpenTelemetry` (`Directory.Packages.props:9`) alone does not create a
dependency. Expected external dependencies (NuGet, normalized): `system.memory`,
`microsoft.extensions.dependencyinjection.abstractions`, `newtonsoft.json`, `serilog` — **4**. The
project-system analysis produces **6** relationships (2 project references, 4 package references).

## 7. Expected diagnostics (project-system analysis)

| Severity | Code | About | Location |
|---|---|---|---|
| `Warning` | `dotnet.project.missing` | `src/Advanced.Plugins/Advanced.Plugins.csproj` does not exist | `src/Advanced.Services/Advanced.Services.csproj:5:5` |
| `Warning` | `dotnet.msbuild.unsupported-condition` | `Exists('local.packages.props')` is not evaluated | `src/Advanced.Services/Advanced.Services.csproj:21:3` |

No other warnings or errors are expected from ANALYSIS-002 … ANALYSIS-004.

## 8. Modules and entities (ANALYSIS-005)

### 8.1 Modules

Every namespace of a project's declarations, **including the enclosing namespaces** (hierarchy via
`ParentModuleId`), is a `Namespace` module of that project. The same namespace in two projects gives two
modules (module keys include the project).

| Project | Module (qualified name) | Parent |
|---|---|---|
| `Advanced.Core` | `Advanced` | — |
| `Advanced.Core` | `Advanced.Core` | `Advanced` |
| `Advanced.Core` | `Advanced.Core.Abstractions` | `Advanced.Core` |
| `Advanced.Core` | `Advanced.Core.Entities` | `Advanced.Core` |
| `Advanced.Services` | `Advanced` | — |
| `Advanced.Services` | `Advanced.Services` | `Advanced` |
| `Advanced.Services` | `Advanced.Services.Customers` | `Advanced.Services` |

The placeholder `Advanced.Plugins` has no modules.

### 8.2 Entity rules

- Entities are the types and the **members declared in source**: methods, constructors, properties,
  fields, events, enum members. Implicit members (default constructors, accessors, backing fields,
  record-generated members, a delegate's `Invoke`) are not entities.
- `SymbolKey` = Roslyn documentation comment ID. Nested types use `.` (`T:Outer.Inner`), generic arity
  uses `` `n `` / ``` ``n ```, type parameters in member signatures use `` `0 ``.
- `ContainingEntity`: the declaring type for members and nested types; none for top-level types.
- `Module`: the namespace module of the (outermost) type.
- Partial types are **one** entity with one declaration per part.
- Kinds: enum members are `Constant`; `readonly` / `const` / `virtual` have no modifier flag.
- Modifiers: `Abstract` only for abstract classes and abstract class members (interfaces and their
  members carry none), `Static`, `Sealed`, `Partial`, `Generic` (type or method has type parameters).
- Visibility as declared; members of interfaces without an access modifier are `Public`; types without
  one are `Internal`, class members without one `Private`.

### 8.3 Entities of `Advanced.Core` (27)

| Symbol key | Kind | Visibility | Modifiers | Declaration(s) |
|---|---|---|---|---|
| ``T:Advanced.Core.Entities.Entity`1`` | Class | Public | Abstract, Generic | `src/Advanced.Core/Entities/Entity.cs:3:23` |
| ``M:Advanced.Core.Entities.Entity`1.#ctor(`0)`` | Constructor | Protected | — | `src/Advanced.Core/Entities/Entity.cs:6:15` |
| ``P:Advanced.Core.Entities.Entity`1.Id`` | Property | Public | — | `src/Advanced.Core/Entities/Entity.cs:11:16` |
| `T:Advanced.Core.Entities.Customer` | Class | Public | Partial | `src/Advanced.Core/Entities/Customer.cs:5:22`, `src/Advanced.Core/Entities/Customer.Orders.cs:3:22` |
| `M:Advanced.Core.Entities.Customer.#ctor(System.Guid,System.String)` | Constructor | Public | — | `src/Advanced.Core/Entities/Customer.cs:7:12` |
| `P:Advanced.Core.Entities.Customer.Name` | Property | Public | — | `src/Advanced.Core/Entities/Customer.cs:13:19` |
| `P:Advanced.Core.Entities.Customer.ModifiedAt` | Property | Public | — | `src/Advanced.Core/Entities/Customer.cs:15:27` |
| `F:Advanced.Core.Entities.Customer._orders` | Field | Private | — | `src/Advanced.Core/Entities/Customer.Orders.cs:5:41` |
| `P:Advanced.Core.Entities.Customer.Orders` | Property | Public | — | `src/Advanced.Core/Entities/Customer.Orders.cs:7:40` |
| `M:Advanced.Core.Entities.Customer.AddOrder(System.Decimal)` | Method | Public | — | `src/Advanced.Core/Entities/Customer.Orders.cs:9:17` |
| `T:Advanced.Core.Entities.Customer.OrderSummary` | Class | Public | Sealed | `src/Advanced.Core/Entities/Customer.Orders.cs:14:25` (contained in `Customer`) |
| `M:Advanced.Core.Entities.Customer.OrderSummary.#ctor(System.Decimal)` | Constructor | Public | — | `src/Advanced.Core/Entities/Customer.Orders.cs:16:16` |
| `P:Advanced.Core.Entities.Customer.OrderSummary.Total` | Property | Public | — | `src/Advanced.Core/Entities/Customer.Orders.cs:21:24` |
| `T:Advanced.Core.Abstractions.IAuditable` | Interface | Public | — | `src/Advanced.Core/Abstractions/IAuditable.cs:3:18` |
| `P:Advanced.Core.Abstractions.IAuditable.ModifiedAt` | Property | Public | — | `src/Advanced.Core/Abstractions/IAuditable.cs:5:20` |
| ``T:Advanced.Core.Abstractions.IRepository`1`` | Interface | Public | Generic | `src/Advanced.Core/Abstractions/IRepository.cs:5:18` |
| ``M:Advanced.Core.Abstractions.IRepository`1.Find(System.Guid)`` | Method | Public | — | `src/Advanced.Core/Abstractions/IRepository.cs:8:8` |
| ``M:Advanced.Core.Abstractions.IRepository`1.Add(`0)`` | Method | Public | — | `src/Advanced.Core/Abstractions/IRepository.cs:10:10` |
| `T:Advanced.Core.Abstractions.ResultKind` | Enum | Public | — | `src/Advanced.Core/Abstractions/Result.cs:3:13` |
| `F:Advanced.Core.Abstractions.ResultKind.Success` | Constant | Public | — | `src/Advanced.Core/Abstractions/Result.cs:5:5` |
| `F:Advanced.Core.Abstractions.ResultKind.Failure` | Constant | Public | — | `src/Advanced.Core/Abstractions/Result.cs:6:5` |
| ``T:Advanced.Core.Abstractions.Result`1`` | Struct | Public | Generic | `src/Advanced.Core/Abstractions/Result.cs:9:24` |
| ``M:Advanced.Core.Abstractions.Result`1.#ctor(Advanced.Core.Abstractions.ResultKind,`0)`` | Constructor | Public | — | `src/Advanced.Core/Abstractions/Result.cs:11:12` |
| ``P:Advanced.Core.Abstractions.Result`1.Kind`` | Property | Public | — | `src/Advanced.Core/Abstractions/Result.cs:17:23` |
| ``P:Advanced.Core.Abstractions.Result`1.Value`` | Property | Public | — | `src/Advanced.Core/Abstractions/Result.cs:19:15` |
| ``M:Advanced.Core.Abstractions.Result`1.Ok(`0)`` | Method | Public | Static | `src/Advanced.Core/Abstractions/Result.cs:21:29` |
| ``T:Advanced.Core.Abstractions.ResultHandler`1`` | Delegate | Public | Generic | `src/Advanced.Core/Abstractions/Result.cs:24:22` |

### 8.4 Entities of `Advanced.Services` (14)

| Symbol key | Kind | Visibility | Modifiers | Declaration |
|---|---|---|---|---|
| `T:Advanced.Services.Customers.ICustomerService` | Interface | Public | — | `src/Advanced.Services/Customers/ICustomerService.cs:6:18` |
| `M:Advanced.Services.Customers.ICustomerService.Create(System.String)` | Method | Public | — | `src/Advanced.Services/Customers/ICustomerService.cs:8:22` |
| `T:Advanced.Services.Customers.CustomerService` | Class | Internal | Sealed | `src/Advanced.Services/Customers/CustomerService.cs:6:23` |
| `F:Advanced.Services.Customers.CustomerService._repository` | Field | Private | — | `src/Advanced.Services/Customers/CustomerService.cs:8:44` |
| ``M:Advanced.Services.Customers.CustomerService.#ctor(Advanced.Core.Abstractions.IRepository{Advanced.Core.Entities.Customer})`` | Constructor | Public | — | `src/Advanced.Services/Customers/CustomerService.cs:10:12` |
| `M:Advanced.Services.Customers.CustomerService.Create(System.String)` | Method | Public | — | `src/Advanced.Services/Customers/CustomerService.cs:15:29` |
| `T:Advanced.Services.Customers.InMemoryCustomerRepository` | Class | Internal | Sealed | `src/Advanced.Services/Customers/InMemoryCustomerRepository.cs:6:23` |
| `F:Advanced.Services.Customers.InMemoryCustomerRepository._items` | Field | Private | — | `src/Advanced.Services/Customers/InMemoryCustomerRepository.cs:8:49` |
| `M:Advanced.Services.Customers.InMemoryCustomerRepository.Find(System.Guid)` | Method | Public | — | `src/Advanced.Services/Customers/InMemoryCustomerRepository.cs:10:22` |
| `M:Advanced.Services.Customers.InMemoryCustomerRepository.Add(Advanced.Core.Entities.Customer)` | Method | Public | — | `src/Advanced.Services/Customers/InMemoryCustomerRepository.cs:12:17` |
| `T:Advanced.Services.ServiceCollectionExtensions` | Class | Public | Static | `src/Advanced.Services/ServiceCollectionExtensions.cs:8:21` |
| `M:Advanced.Services.ServiceCollectionExtensions.AddAdvancedServices(IServiceCollection)` (V-1) | Method | Public | Static | `src/Advanced.Services/ServiceCollectionExtensions.cs:10:38` |
| `T:Advanced.Services.ServiceCollectionExtensions.Catalog` | Class | Private | Sealed | `src/Advanced.Services/ServiceCollectionExtensions.cs:23:26` (contained in `ServiceCollectionExtensions`) |
| `M:Advanced.Services.ServiceCollectionExtensions.Catalog.AddSingleton(System.String)` | Method | Public | — | `src/Advanced.Services/ServiceCollectionExtensions.cs:25:21` |

**Verification item V-1 — decided in ANALYSIS-005 (ADR-011):** symbol keys must not depend on the
machine. Every C# compilation references the same embedded .NET 10 reference assemblies
(`Basic.Reference.Assemblies.Net100`) and never NuGet assemblies. Framework types are therefore always
resolved (`System.Guid`), and package types are never resolved: Roslyn writes an unresolved type as it is
named in source, so the parameter of `AddAdvancedServices` appears as `IServiceCollection` (not
`Microsoft.Extensions.DependencyInjection.IServiceCollection`) — on every machine, restored or not.

## 9. Semantic relationships (ANALYSIS-006)

Endpoints are entities of the repository; relationships to types outside the repository (`System.*`,
NuGet types) are not created. The source is the innermost declaring entity of the syntax (member or
type). With the in-repository compilation reference from `Advanced.Services` to `Advanced.Core`, all rows
are `Confirmed` regardless of SDK reference packs. The list is the **minimum** set; additional `UsesType`
relationships for other type occurrences are allowed.

| Source | Kind | Target | Evidence | Cross-project |
|---|---|---|---|---|
| `T:Advanced.Core.Entities.Customer` | Inherits | ``T:Advanced.Core.Entities.Entity`1`` | `src/Advanced.Core/Entities/Customer.cs:5:33` | — |
| `T:Advanced.Core.Entities.Customer` | Implements | `T:Advanced.Core.Abstractions.IAuditable` | `src/Advanced.Core/Entities/Customer.cs:5:47` | — |
| `M:Advanced.Core.Entities.Customer.#ctor(System.Guid,System.String)` | Calls | ``M:Advanced.Core.Entities.Entity`1.#ctor(`0)`` | `src/Advanced.Core/Entities/Customer.cs:8:11` (`base`) | — |
| `M:Advanced.Core.Entities.Customer.AddOrder(System.Decimal)` | Instantiates | `T:Advanced.Core.Entities.Customer.OrderSummary` | `src/Advanced.Core/Entities/Customer.Orders.cs:11:25` | — |
| `F:Advanced.Core.Entities.Customer._orders` | UsesType | `T:Advanced.Core.Entities.Customer.OrderSummary` | `src/Advanced.Core/Entities/Customer.Orders.cs:5:27` | — |
| ``T:Advanced.Core.Abstractions.IRepository`1`` | UsesType | ``T:Advanced.Core.Entities.Entity`1`` | `src/Advanced.Core/Abstractions/IRepository.cs:6:15` (constraint) | — |
| ``P:Advanced.Core.Abstractions.Result`1.Kind`` | UsesType | `T:Advanced.Core.Abstractions.ResultKind` | `src/Advanced.Core/Abstractions/Result.cs:17:12` | — |
| ``T:Advanced.Core.Abstractions.ResultHandler`1`` | UsesType | ``T:Advanced.Core.Abstractions.Result`1`` | `src/Advanced.Core/Abstractions/Result.cs:24:39` | — |
| `T:Advanced.Services.Customers.CustomerService` | Implements | `T:Advanced.Services.Customers.ICustomerService` | `src/Advanced.Services/Customers/CustomerService.cs:6:41` | — |
| `F:Advanced.Services.Customers.CustomerService._repository` | UsesType | ``T:Advanced.Core.Abstractions.IRepository`1`` | `src/Advanced.Services/Customers/CustomerService.cs:8:22` | yes |
| `F:Advanced.Services.Customers.CustomerService._repository` | UsesType | `T:Advanced.Core.Entities.Customer` | `src/Advanced.Services/Customers/CustomerService.cs:8:34` (generic argument) | yes |
| `M:Advanced.Services.Customers.CustomerService.Create(System.String)` | Instantiates | `T:Advanced.Core.Entities.Customer` | `src/Advanced.Services/Customers/CustomerService.cs:17:28` | yes |
| `M:Advanced.Services.Customers.CustomerService.Create(System.String)` | Calls | ``M:Advanced.Core.Abstractions.IRepository`1.Add(`0)`` | `src/Advanced.Services/Customers/CustomerService.cs:18:21` | yes |
| `M:Advanced.Services.Customers.CustomerService.Create(System.String)` | Calls | ``M:Advanced.Core.Abstractions.Result`1.Ok(`0)`` | `src/Advanced.Services/Customers/CustomerService.cs:19:33` | yes |
| `M:Advanced.Services.Customers.ICustomerService.Create(System.String)` | UsesType | ``T:Advanced.Core.Abstractions.Result`1`` | `src/Advanced.Services/Customers/ICustomerService.cs:8:5` | yes |
| `T:Advanced.Services.Customers.InMemoryCustomerRepository` | Implements | ``T:Advanced.Core.Abstractions.IRepository`1`` | `src/Advanced.Services/Customers/InMemoryCustomerRepository.cs:6:52` | yes |

Notes: generic instantiations target the **definition** (`` IRepository`1 ``, `` Result`1 ``), not the
constructed type. Member access to a property (`item.Id`, `InMemoryCustomerRepository.cs:12:51`) is not a
`Calls` relationship in M3 (properties are not invoked), so no relationship to
``P:Advanced.Core.Entities.Entity`1.Id`` is expected. **Must not exist:** any relationship whose source or target is a placeholder project;
any relationship produced by the comment or string literal in `ServiceCollectionExtensions.cs` lines 16–17.

## 10. DI registrations (ANALYSIS-007, `Heuristic`)

Source: the registering method; target: the implementation type; attributes `serviceType` (symbol key of
the service type definition) and `lifetime`. Evidence location: start of the invocation (`services`).

| Source | Target | `serviceType` | `lifetime` | Evidence |
|---|---|---|---|---|
| `M:Advanced.Services.ServiceCollectionExtensions.AddAdvancedServices(IServiceCollection)` | `T:Advanced.Services.Customers.CustomerService` | `T:Advanced.Services.Customers.ICustomerService` | `scoped` | `src/Advanced.Services/ServiceCollectionExtensions.cs:12:9` |
| `M:Advanced.Services.ServiceCollectionExtensions.AddAdvancedServices(IServiceCollection)` | `T:Advanced.Services.Customers.InMemoryCustomerRepository` | ``T:Advanced.Core.Abstractions.IRepository`1`` | `singleton` | `src/Advanced.Services/ServiceCollectionExtensions.cs:13:9` |

**Negative cases — no `RegistersService`:** the commented-out call (line 16), the string literal (line
17) and `new Catalog().AddSingleton(example)` (line 18: an instance method of an in-repository class with
one non-generic argument, not an `IServiceCollection` extension).
