using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Entities;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Relationships;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodebaseExplorer.Analysis.Languages.CSharp;

/// <summary>
/// Extracts entity-to-entity relationships from a project's semantic model (ANALYSIS-006): <c>Inherits</c>,
/// <c>Implements</c>, <c>UsesType</c>, <c>Instantiates</c>, <c>Calls</c>. Rules: fixture
/// <c>dotnet-advanced</c> EXPECTED.md §9.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Only entities of the repository are endpoints; framework and package types are ignored.</item>
/// <item>The source is the innermost declaring entity of the syntax (member, else type).</item>
/// <item>Generic instantiations target the definition (<c>IRepository`1</c>, not <c>IRepository&lt;Customer&gt;</c>).</item>
/// <item>Resolved by the semantic model → <c>Confirmed</c>. Not resolved, but the name (and arity) matches exactly
/// one type of the repository → <c>Syntactic</c> (e.g. a missing project reference or <c>using</c>).</item>
/// <item>Each occurrence is one evidence record; occurrences of the same (source, kind, target) form one relationship.</item>
/// </list>
/// </remarks>
internal sealed class RelationshipCollector
{
    public const string RuleInherits = "csharp.Inherits";
    public const string RuleImplements = "csharp.Implements";
    public const string RuleUsesType = "csharp.UsesType";
    public const string RuleInstantiates = "csharp.Instantiates";
    public const string RuleCalls = "csharp.Calls";

    private readonly Func<ISymbol, CodeEntity?> _entityOf;
    private readonly ILookup<(string Name, int Arity), CodeEntity> _typesByName;
    private readonly IReadOnlyDictionary<SyntaxTree, SourceFile> _files;
    private readonly AnalysisRunId _runId;
    private readonly Dictionary<ElementId, (ElementReference Source, RelationshipKind Kind, ElementReference Target, List<AnalysisEvidence> Evidence)> _relationships = [];

    /// <param name="entityOf">The repository entity of a symbol (declared in this project or a referenced one), if any.</param>
    /// <param name="knownTypes">Type entities known so far, for the name-based (syntactic) fallback.</param>
    /// <param name="files">The project's source files.</param>
    /// <param name="runId">Run the evidence belongs to.</param>
    public RelationshipCollector(Func<ISymbol, CodeEntity?> entityOf, IEnumerable<CodeEntity> knownTypes, IReadOnlyDictionary<SyntaxTree, SourceFile> files, AnalysisRunId runId)
    {
        _entityOf = entityOf;
        _files = files;
        _runId = runId;
        _typesByName = knownTypes
            .Where(e => e.SymbolKey.StartsWith("T:", StringComparison.Ordinal))
            .ToLookup(e => (e.Name, Arity(e.SymbolKey)));
    }

    public IEnumerable<CodeRelationship> Relationships => _relationships.Values
        .Select(r => CodeRelationship.Create(r.Source, r.Kind, r.Target, r.Evidence));

    public void Collect(Compilation compilation, CancellationToken cancellationToken)
    {
        foreach (var tree in _files.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot(cancellationToken).DescendantNodes())
            {
                switch (node)
                {
                    case BaseTypeSyntax baseType:
                        AddBaseType(model, baseType, cancellationToken);
                        break;
                    case BaseObjectCreationExpressionSyntax creation:
                        AddCreation(model, creation, cancellationToken);
                        break;
                    case InvocationExpressionSyntax invocation:
                        AddCall(model, invocation, NameOf(invocation.Expression) ?? (SyntaxNode)invocation, cancellationToken);
                        break;
                    case ConstructorInitializerSyntax initializer:
                        AddCall(model, initializer, initializer.ThisOrBaseKeyword, cancellationToken);
                        break;
                    case SimpleNameSyntax name:
                        AddTypeUsage(model, name, cancellationToken);
                        break;
                }
            }
        }
    }

    private void AddBaseType(SemanticModel model, BaseTypeSyntax baseType, CancellationToken cancellationToken)
    {
        if (baseType.Parent?.Parent is not BaseTypeDeclarationSyntax declaration
            || model.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol declaring)
        {
            return;
        }

        var (target, level) = ResolveType(model, baseType.Type, cancellationToken);
        if (target is null)
        {
            return;
        }

        var targetIsInterface = target.SymbolKey.StartsWith("T:", StringComparison.Ordinal) && target.Kind == EntityKind.Interface;
        var kind = targetIsInterface && declaring.TypeKind != TypeKind.Interface ? RelationshipKind.Implements : RelationshipKind.Inherits;
        Add(EntityOf(declaring), kind, target, level, kind == RelationshipKind.Implements ? RuleImplements : RuleInherits, BaseName(baseType.Type));
    }

    private void AddCreation(SemanticModel model, BaseObjectCreationExpressionSyntax creation, CancellationToken cancellationToken)
    {
        CodeEntity? target;
        EvidenceLevel level;
        SyntaxNode location;
        if (creation is ObjectCreationExpressionSyntax explicitType)
        {
            (target, level) = ResolveType(model, explicitType.Type, cancellationToken);
            location = BaseName(explicitType.Type);
        }
        else
        {
            target = EntityOf(model.GetTypeInfo(creation, cancellationToken).Type);
            level = EvidenceLevel.Confirmed;
            location = creation;
        }

        if (target is not null)
        {
            Add(SourceOf(model, creation, cancellationToken), RelationshipKind.Instantiates, target, level, RuleInstantiates, location);
        }
    }

    private void AddCall(SemanticModel model, SyntaxNode call, SyntaxNodeOrToken location, CancellationToken cancellationToken)
    {
        var info = model.GetSymbolInfo(call, cancellationToken);
        var method = info.Symbol as IMethodSymbol;
        var level = EvidenceLevel.Confirmed;
        if (method is null && info.CandidateSymbols.OfType<IMethodSymbol>().ToArray() is [var single])
        {
            method = single;
            level = EvidenceLevel.Syntactic; // overload or accessibility not decided by the compiler
        }

        if (EntityOf(method?.ReducedFrom ?? method) is { } target)
        {
            Add(SourceOf(model, call, cancellationToken), RelationshipKind.Calls, target, level, RuleCalls, location);
        }
    }

    private void AddTypeUsage(SemanticModel model, SimpleNameSyntax name, CancellationToken cancellationToken)
    {
        // Handled elsewhere: the type of a base list entry, the type of `new T()`, names in member access/invocation
        // positions that are members, and the right part of qualified names (the whole name is visited).
        var outermost = OutermostTypeSyntax(name);
        if (outermost.Parent is BaseTypeSyntax || outermost.Parent is ObjectCreationExpressionSyntax creation && creation.Type == outermost)
        {
            if (!IsGenericArgumentOf(name, outermost))
            {
                return;
            }
        }

        if (name.Parent is QualifiedNameSyntax qualified && qualified.Right == name)
        {
            return; // resolved via the qualified name itself
        }

        var symbol = model.GetSymbolInfo(name, cancellationToken).Symbol;
        CodeEntity? target;
        EvidenceLevel level;
        if (symbol is INamedTypeSymbol type)
        {
            (target, level) = (EntityOf(type), EvidenceLevel.Confirmed);
        }
        else if (symbol is null && model.GetTypeInfo(name, cancellationToken).Type is IErrorTypeSymbol && IsInTypePosition(name))
        {
            (target, level) = (MatchByName(name), EvidenceLevel.Syntactic);
        }
        else
        {
            return;
        }

        if (target is not null)
        {
            Add(SourceOf(model, name, cancellationToken), RelationshipKind.UsesType, target, level, RuleUsesType, name);
        }
    }

    private (CodeEntity? Target, EvidenceLevel Level) ResolveType(SemanticModel model, TypeSyntax syntax, CancellationToken cancellationToken)
    {
        var type = model.GetTypeInfo(syntax, cancellationToken).Type;
        if (type is not null and not IErrorTypeSymbol)
        {
            return (EntityOf(type), EvidenceLevel.Confirmed);
        }

        return BaseName(syntax) is SimpleNameSyntax name ? (MatchByName(name), EvidenceLevel.Syntactic) : (null, EvidenceLevel.Syntactic);
    }

    private CodeEntity? MatchByName(SimpleNameSyntax name)
    {
        var arity = name is GenericNameSyntax generic ? generic.Arity : 0;
        return _typesByName[(name.Identifier.ValueText, arity)].ToArray() is [var single] ? single : null;
    }

    private void Add(CodeEntity? source, RelationshipKind kind, CodeEntity target, EvidenceLevel level, string rule, SyntaxNodeOrToken location)
    {
        if (source is null || (source.Id == target.Id && kind != RelationshipKind.Calls))
        {
            return;
        }

        var sourceReference = new ElementReference(ElementKind.Entity, source.Id);
        var targetReference = new ElementReference(ElementKind.Entity, target.Id);
        var id = CodeRelationship.IdFor(sourceReference, kind, targetReference);
        var span = location.GetLocation()!.GetLineSpan();
        var evidence = AnalysisEvidence.Create(
            id,
            CSharpCodes.Analyzer,
            CSharpCodes.Version,
            level,
            rule,
            new SourceLocation(
                _files[location.SyntaxTree!].FileId,
                span.StartLinePosition.Line + 1,
                span.StartLinePosition.Character + 1,
                span.EndLinePosition.Line + 1,
                span.EndLinePosition.Character + 1),
            _runId);

        if (!_relationships.TryGetValue(id, out var relationship))
        {
            relationship = (sourceReference, kind, targetReference, []);
            _relationships.Add(id, relationship);
        }

        relationship.Evidence.Add(evidence);
    }

    private CodeEntity? SourceOf(SemanticModel model, SyntaxNode node, CancellationToken cancellationToken)
    {
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            ISymbol? declared = ancestor switch
            {
                AccessorDeclarationSyntax accessor => accessor.Parent?.Parent is { } property ? model.GetDeclaredSymbol(property, cancellationToken) : null,
                BaseFieldDeclarationSyntax field => field.Declaration.Variables.FirstOrDefault() is { } variable ? model.GetDeclaredSymbol(variable, cancellationToken) : null,
                MemberDeclarationSyntax member and not BaseNamespaceDeclarationSyntax and not GlobalStatementSyntax => model.GetDeclaredSymbol(member, cancellationToken),
                _ => null,
            };

            if (EntityOf(declared) is { } entity)
            {
                return entity;
            }
        }

        return null;
    }

    private CodeEntity? EntityOf(ISymbol? symbol) => symbol is null ? null : _entityOf(symbol.OriginalDefinition);

    private static SimpleNameSyntax? NameOf(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax access => access.Name,
        MemberBindingExpressionSyntax binding => binding.Name,
        SimpleNameSyntax name => name,
        _ => null,
    };

    /// <summary>The rightmost simple name of a type syntax (<c>Ns.Type&lt;T&gt;</c> → <c>Type&lt;T&gt;</c>).</summary>
    private static SyntaxNode BaseName(TypeSyntax syntax) => syntax switch
    {
        QualifiedNameSyntax qualified => qualified.Right,
        AliasQualifiedNameSyntax alias => alias.Name,
        NullableTypeSyntax nullable => BaseName(nullable.ElementType),
        _ => syntax,
    };

    private static TypeSyntax OutermostTypeSyntax(SimpleNameSyntax name)
    {
        SyntaxNode current = name;
        while (current.Parent is QualifiedNameSyntax or NullableTypeSyntax)
        {
            current = current.Parent;
        }

        return current as TypeSyntax ?? name;
    }

    private static bool IsGenericArgumentOf(SimpleNameSyntax name, TypeSyntax outermost) =>
        name.Ancestors().TakeWhile(a => a != outermost).Any(a => a is TypeArgumentListSyntax);

    private static bool IsInTypePosition(SimpleNameSyntax name) =>
        SyntaxFacts.IsInTypeOnlyContext(name) || name.Parent is TypeArgumentListSyntax or VariableDeclarationSyntax or ParameterSyntax;

    private static int Arity(string typeKey)
    {
        var tick = typeKey.LastIndexOf('`');
        return tick >= 0 && int.TryParse(typeKey.AsSpan(tick + 1), out var arity) ? arity : 0;
    }
}
