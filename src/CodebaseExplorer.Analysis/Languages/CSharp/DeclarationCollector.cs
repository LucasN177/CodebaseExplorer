using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Entities;
using CodebaseExplorer.Domain.Modules;
using CodebaseExplorer.Domain.Projects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodebaseExplorer.Analysis.Languages.CSharp;

/// <summary>
/// Collects namespaces (<see cref="CodeModule"/>s) and the types and members declared in a project's
/// source (<see cref="CodeEntity"/>s) from its compilation (ANALYSIS-005). Rules: fixture
/// <c>dotnet-advanced</c> EXPECTED.md §8.
/// </summary>
internal sealed class DeclarationCollector
{
    /// <summary>Qualified name of the module for types in the global namespace.</summary>
    public const string GlobalNamespaceModule = "(global)";

    private static readonly SymbolDisplayFormat QualifiedTypeFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

    private static readonly SymbolDisplayFormat SignatureFormat = SymbolDisplayFormat.CSharpErrorMessageFormat
        .WithMemberOptions(SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeType)
        .WithParameterOptions(SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeName);

    private readonly CodeProject _project;
    private readonly IReadOnlyDictionary<SyntaxTree, SourceFile> _files;
    private readonly Dictionary<string, CodeModule> _modules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CodeEntity> _entities = new(StringComparer.Ordinal);

    public DeclarationCollector(CodeProject project, IReadOnlyDictionary<SyntaxTree, SourceFile> files)
    {
        _project = project;
        _files = files;
    }

    public IReadOnlyCollection<CodeModule> Modules => _modules.Values;

    public IReadOnlyCollection<CodeEntity> Entities => _entities.Values;

    /// <summary>The entity of a symbol declared in this project, if any (by documentation comment ID).</summary>
    public CodeEntity? FindEntity(ISymbol symbol) =>
        symbol.OriginalDefinition.GetDocumentationCommentId() is { } key ? _entities.GetValueOrDefault(key) : null;

    public void Collect(Compilation compilation, CancellationToken cancellationToken)
    {
        // All named types with a declaration in this project, outer before inner (containing entities first).
        var types = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        foreach (var tree in _files.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot(cancellationToken).DescendantNodes()
                .Where(n => n is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax))
            {
                if (model.GetDeclaredSymbol(declaration, cancellationToken) is INamedTypeSymbol type
                    && type.GetDocumentationCommentId() is { } key)
                {
                    types.TryAdd(key, type);
                }
            }
        }

        foreach (var (key, type) in types.OrderBy(t => Depth(t.Value)).ThenBy(t => t.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var container = type.ContainingType is { } outer ? FindEntity(outer) : null;
            var entity = AddEntity(type, key, container);
            if (entity is null || type.TypeKind == TypeKind.Delegate)
            {
                continue;
            }

            foreach (var member in type.GetMembers().Where(IsSourceMember).OrderBy(m => m.GetDocumentationCommentId(), StringComparer.Ordinal))
            {
                if (member.GetDocumentationCommentId() is { } memberKey)
                {
                    AddEntity(member, memberKey, entity);
                }
            }
        }
    }

    private CodeEntity? AddEntity(ISymbol symbol, string key, CodeEntity? container)
    {
        if (_entities.ContainsKey(key))
        {
            return _entities[key];
        }

        var declarations = symbol.Locations
            .Where(l => l.IsInSource && l.SourceTree is not null && _files.ContainsKey(l.SourceTree))
            .OrderBy(l => _files[l.SourceTree!].Path.Value, StringComparer.Ordinal)
            .ThenBy(l => l.SourceSpan.Start)
            .Select(l => ToLocation(l))
            .ToArray();
        if (declarations.Length == 0 || KindOf(symbol) is not { } kind)
        {
            return null;
        }

        var outermost = symbol is INamedTypeSymbol t ? t : symbol.ContainingType;
        while (outermost!.ContainingType is { } next)
        {
            outermost = next;
        }

        var syntax = symbol.DeclaringSyntaxReferences.Where(r => _files.ContainsKey(r.SyntaxTree)).Select(r => r.GetSyntax()).ToArray();
        var entity = CodeEntity.Create(
            _project,
            ModuleFor(outermost.ContainingNamespace),
            kind,
            NameOf(symbol),
            QualifiedNameOf(symbol),
            LanguageId.CSharp,
            key,
            declarations,
            VisibilityOf(symbol),
            ModifiersOf(symbol, syntax),
            container,
            symbol is INamedTypeSymbol ? null : symbol.ToDisplayString(SignatureFormat),
            new EntityMetrics(
                syntax.Sum(LineCount),
                symbol is INamedTypeSymbol type ? type.GetMembers().Count(IsSourceMember) + type.GetTypeMembers().Length : null));
        _entities.Add(key, entity);
        return entity;
    }

    private CodeModule ModuleFor(INamespaceSymbol? @namespace)
    {
        if (@namespace is null || @namespace.IsGlobalNamespace)
        {
            return GetOrAddModule(GlobalNamespaceModule, GlobalNamespaceModule, parent: null);
        }

        // Enclosing namespaces first, so that every module has its parent (EXPECTED.md §8.1).
        var chain = new Stack<INamespaceSymbol>();
        for (var current = @namespace; current is { IsGlobalNamespace: false }; current = current.ContainingNamespace)
        {
            chain.Push(current);
        }

        CodeModule? module = null;
        while (chain.TryPop(out var current))
        {
            module = GetOrAddModule(current.Name, current.ToDisplayString(), module);
        }

        return module!;
    }

    private CodeModule GetOrAddModule(string name, string qualifiedName, CodeModule? parent)
    {
        if (!_modules.TryGetValue(qualifiedName, out var module))
        {
            module = CodeModule.Create(_project, ModuleKind.Namespace, name, qualifiedName, parent);
            _modules.Add(qualifiedName, module);
        }

        return module;
    }

    private SourceLocation ToLocation(Location location)
    {
        var span = location.GetLineSpan();
        return new SourceLocation(
            _files[location.SourceTree!].FileId,
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1,
            span.EndLinePosition.Line + 1,
            Math.Max(span.EndLinePosition.Character + 1, 1));
    }

    /// <summary>Members declared in source (EXPECTED.md §8.2): no implicit members, accessors or nested types.</summary>
    private static bool IsSourceMember(ISymbol member) =>
        !member.IsImplicitlyDeclared
        && member is not INamedTypeSymbol
        && member.Locations.Any(l => l.IsInSource)
        && member switch
        {
            IMethodSymbol method => method.MethodKind is MethodKind.Ordinary or MethodKind.Constructor or MethodKind.StaticConstructor
                or MethodKind.Destructor or MethodKind.UserDefinedOperator or MethodKind.Conversion or MethodKind.ExplicitInterfaceImplementation,
            IPropertySymbol or IFieldSymbol or IEventSymbol => true,
            _ => false,
        };

    private static int Depth(INamedTypeSymbol type)
    {
        var depth = 0;
        for (var current = type.ContainingType; current is not null; current = current.ContainingType)
        {
            depth++;
        }

        return depth;
    }

    private static EntityKind? KindOf(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol { IsRecord: true } => EntityKind.Record,
        INamedTypeSymbol type => type.TypeKind switch
        {
            TypeKind.Class => EntityKind.Class,
            TypeKind.Interface => EntityKind.Interface,
            TypeKind.Struct => EntityKind.Struct,
            TypeKind.Enum => EntityKind.Enum,
            TypeKind.Delegate => EntityKind.Delegate,
            _ => null,
        },
        IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } => EntityKind.Constructor,
        IMethodSymbol => EntityKind.Method,
        IPropertySymbol => EntityKind.Property,
        IFieldSymbol { IsConst: true } or IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } => EntityKind.Constant,
        IFieldSymbol => EntityKind.Field,
        IEventSymbol => EntityKind.Event,
        _ => null,
    };

    private static string NameOf(ISymbol symbol) => symbol switch
    {
        IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor or MethodKind.Destructor } method => method.ContainingType.Name,
        IPropertySymbol { IsIndexer: true } => "this[]",
        _ => symbol.Name,
    };

    private static string QualifiedNameOf(ISymbol symbol) => symbol is INamedTypeSymbol type
        ? type.ToDisplayString(QualifiedTypeFormat)
        : $"{symbol.ContainingType.ToDisplayString(QualifiedTypeFormat)}.{NameOf(symbol)}";

    private static Visibility VisibilityOf(ISymbol symbol) => symbol.DeclaredAccessibility switch
    {
        Accessibility.Public => Visibility.Public,
        Accessibility.Internal => Visibility.Internal,
        Accessibility.Protected => Visibility.Protected,
        Accessibility.Private => Visibility.Private,
        Accessibility.ProtectedOrInternal => Visibility.ProtectedInternal,
        Accessibility.ProtectedAndInternal => Visibility.PrivateProtected,
        _ => Visibility.Unknown,
    };

    /// <summary>EXPECTED.md §8.2: flags reflect what is written, not what Roslyn implies (e.g. structs are not "sealed").</summary>
    private EntityModifiers ModifiersOf(ISymbol symbol, IReadOnlyList<SyntaxNode> syntax)
    {
        var modifiers = EntityModifiers.None;
        var inInterface = symbol.ContainingType?.TypeKind == TypeKind.Interface;
        var isType = symbol as INamedTypeSymbol;
        var isStaticClass = isType is { TypeKind: TypeKind.Class, IsStatic: true };

        if (symbol.IsAbstract && !inInterface && isType?.TypeKind != TypeKind.Interface && !isStaticClass)
        {
            modifiers |= EntityModifiers.Abstract;
        }

        if (symbol.IsStatic && symbol is not IFieldSymbol { IsConst: true } && symbol.ContainingType?.TypeKind != TypeKind.Enum)
        {
            modifiers |= EntityModifiers.Static;
        }

        if (symbol.IsSealed && !isStaticClass && (isType?.TypeKind == TypeKind.Class || (isType is null && symbol.IsOverride)))
        {
            modifiers |= EntityModifiers.Sealed;
        }

        if (symbol is IMethodSymbol { IsAsync: true })
        {
            modifiers |= EntityModifiers.Async;
        }

        if (syntax.OfType<MemberDeclarationSyntax>().Any(s => s.Modifiers.Any(SyntaxKind.PartialKeyword)))
        {
            modifiers |= EntityModifiers.Partial;
        }

        if (symbol is INamedTypeSymbol { Arity: > 0 } or IMethodSymbol { IsGenericMethod: true })
        {
            modifiers |= EntityModifiers.Generic;
        }

        if (syntax.Count > 0 && syntax.All(s => _files[s.SyntaxTree].IsGenerated))
        {
            modifiers |= EntityModifiers.Generated;
        }

        return modifiers;
    }

    private static int LineCount(SyntaxNode node)
    {
        var span = node.SyntaxTree.GetLineSpan(node.Span);
        return span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
    }
}

/// <summary>A parsed source file of a project.</summary>
/// <param name="Path">Repository-relative path.</param>
/// <param name="FileId">Id of the repository file.</param>
/// <param name="IsGenerated">True if the file carries an auto-generated header.</param>
internal sealed record SourceFile(RelativePath Path, ElementId FileId, bool IsGenerated);
