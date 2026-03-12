using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Dome.Analysis.Roslyn;

using TerrariaTools.Dome.Core;

public sealed record RoslynAnalysisDocument(
    SourceDocument Document,
    CompilationUnitSyntax Root,
    SemanticModel SemanticModel,
    IReadOnlyList<AnalysisTarget> Targets);

public sealed record RoslynAnalysisResult(
    AnalysisView View,
    IReadOnlyList<RoslynAnalysisDocument> Documents);

public sealed class RoslynAnalysisEngine
{
    public Task<RoslynAnalysisResult> AnalyzeAsync(
        IReadOnlyList<SourceDocument> documents,
        CancellationToken cancellationToken)
    {
        var trees = documents
            .Select(document => CSharpSyntaxTree.ParseText(document.SourceText, path: document.SourcePath))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "DomeAnalysis",
            trees,
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });

        var analyzedDocuments = new List<RoslynAnalysisDocument>(documents.Count);
        var allTargets = new List<AnalysisTarget>();
        var allEdges = new List<AnalysisEdge>();

        for (var index = 0; index < trees.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tree = trees[index];
            var root = tree.GetCompilationUnitRoot(cancellationToken);
            var semanticModel = compilation.GetSemanticModel(tree);
            var targets = AnalyzeDocument(documents[index], root, semanticModel, allEdges);

            analyzedDocuments.Add(new RoslynAnalysisDocument(documents[index], root, semanticModel, targets));
            allTargets.AddRange(targets);
        }

        return Task.FromResult(new RoslynAnalysisResult(new AnalysisView(allTargets, allEdges), analyzedDocuments));
    }

    private static IReadOnlyList<AnalysisTarget> AnalyzeDocument(
        SourceDocument document,
        CompilationUnitSyntax root,
        SemanticModel model,
        ICollection<AnalysisEdge> edges)
    {
        var targets = new List<AnalysisTarget>();
        AnalysisTarget? previousTarget = null;

        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            foreach (var variable in field.Declaration.Variables.Where(variable => variable.Initializer != null))
            {
                var memberSymbol = model.GetDeclaredSymbol(variable);
                if (memberSymbol == null)
                {
                    continue;
                }

                var currentTarget = CreateInitializerTarget(
                    document,
                    field,
                    variable.Initializer!,
                    memberSymbol,
                    model,
                    MemberKind.Field);
                targets.Add(currentTarget);
                AddTargetEdges(currentTarget, previousTarget, edges);
                previousTarget = currentTarget;
            }
        }

        foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Where(property => property.Initializer != null))
        {
            var memberSymbol = model.GetDeclaredSymbol(property);
            if (memberSymbol == null)
            {
                continue;
            }

            var currentTarget = CreateInitializerTarget(
                document,
                property,
                property.Initializer!,
                memberSymbol,
                model,
                MemberKind.Property);
            targets.Add(currentTarget);
            AddTargetEdges(currentTarget, previousTarget, edges);
            previousTarget = currentTarget;
        }

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var memberSymbol = model.GetDeclaredSymbol(method);
            if (memberSymbol == null)
            {
                continue;
            }

            foreach (var statement in method.Body?.Statements ?? [])
            {
                var currentTarget = CreateStatementTarget(document, statement, memberSymbol, model, MemberKind.Method);
                targets.Add(currentTarget);
                AddTargetEdges(currentTarget, previousTarget, edges);
                previousTarget = currentTarget;
            }
        }

        foreach (var ctor in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
        {
            var memberSymbol = model.GetDeclaredSymbol(ctor);
            if (memberSymbol == null)
            {
                continue;
            }

            foreach (var statement in ctor.Body?.Statements ?? [])
            {
                var currentTarget = CreateStatementTarget(document, statement, memberSymbol, model, MemberKind.Constructor);
                targets.Add(currentTarget);
                AddTargetEdges(currentTarget, previousTarget, edges);
                previousTarget = currentTarget;
            }
        }

        foreach (var accessor in root.DescendantNodes().OfType<AccessorDeclarationSyntax>())
        {
            var memberSymbol = model.GetDeclaredSymbol(accessor);
            if (memberSymbol == null)
            {
                continue;
            }

            foreach (var statement in accessor.Body?.Statements ?? [])
            {
                var currentTarget = CreateStatementTarget(document, statement, memberSymbol, model, MemberKind.Accessor);
                targets.Add(currentTarget);
                AddTargetEdges(currentTarget, previousTarget, edges);
                previousTarget = currentTarget;
            }
        }

        return targets;
    }

    private static AnalysisTarget CreateInitializerTarget(
        SourceDocument document,
        CSharpSyntaxNode declarationNode,
        EqualsValueClauseSyntax initializer,
        ISymbol memberSymbol,
        SemanticModel model,
        MemberKind memberKind)
    {
        var memberId = MetadataMemberIdBuilder.Build(memberSymbol);
        var definesSymbols = GetDefinedSymbols(initializer, model, memberId, memberSymbol);
        var usesSymbols = GetUsedSymbols(initializer, model, memberId, memberSymbol);
        return new AnalysisTarget(
            new PlanTarget(
                document.RelativePath,
                memberId,
                memberKind,
                TargetKind.Statement,
                declarationNode.SpanStart,
                declarationNode.Span.Length,
                declarationNode.ToString().Trim()),
            IsHighRiskMember(memberSymbol),
            Array.Empty<DirectiveAction>(),
            definesSymbols,
            usesSymbols);
    }

    private static AnalysisTarget CreateStatementTarget(
        SourceDocument document,
        StatementSyntax statement,
        ISymbol memberSymbol,
        SemanticModel model,
        MemberKind memberKind)
    {
        var memberId = MetadataMemberIdBuilder.Build(memberSymbol);
        var definesSymbols = GetDefinedSymbols(statement, model, memberId);
        var usesSymbols = GetUsedSymbols(statement, model, memberId);
        return new AnalysisTarget(
            new PlanTarget(
                document.RelativePath,
                memberId,
                memberKind,
                TargetKind.Statement,
                statement.SpanStart,
                statement.Span.Length,
                statement.ToString().Trim()),
            IsHighRiskMember(memberSymbol),
            DirectiveReader.Read(statement),
            definesSymbols,
            usesSymbols);
    }

    private static IReadOnlyList<SymbolRef> GetDefinedSymbols(
        SyntaxNode node,
        SemanticModel model,
        MemberId declaringMemberId,
        ISymbol? memberSymbol = null)
    {
        var symbols = new List<SymbolRef>();

        if (node is LocalDeclarationStatementSyntax localDeclaration)
        {
            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                var projected = SymbolRefProjector.ProjectDeclared(localDeclaration, variable, model, declaringMemberId);
                if (projected != null)
                {
                    symbols.Add(projected);
                }
            }
        }
        else if (node is ExpressionStatementSyntax expressionStatement &&
                 expressionStatement.Expression is AssignmentExpressionSyntax assignment &&
                 assignment.Left is IdentifierNameSyntax identifier)
        {
            var projected = SymbolRefProjector.ProjectUsed(identifier, model, declaringMemberId);
            if (projected != null)
            {
                symbols.Add(projected);
            }
        }
        else if (memberSymbol is IFieldSymbol or IPropertySymbol)
        {
            var projected = SymbolRefProjector.Project(memberSymbol, declaringMemberId);
            if (projected != null)
            {
                symbols.Add(projected);
            }
        }

        return symbols;
    }

    private static IReadOnlyList<SymbolRef> GetUsedSymbols(
        SyntaxNode node,
        SemanticModel model,
        MemberId declaringMemberId,
        ISymbol? memberSymbol = null)
    {
        var definedKeys = GetDefinedSymbols(node, model, declaringMemberId, memberSymbol)
            .Select(symbol => symbol.SymbolKey)
            .ToHashSet(StringComparer.Ordinal);

        return node.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Select(identifier => SymbolRefProjector.ProjectUsed(identifier, model, declaringMemberId))
            .Where(symbol => symbol != null)
            .Cast<SymbolRef>()
            .Where(symbol => !definedKeys.Contains(symbol.SymbolKey))
            .DistinctBy(symbol => symbol.SymbolKey)
            .ToArray();
    }

    private static void AddTargetEdges(
        AnalysisTarget currentTarget,
        AnalysisTarget? previousTarget,
        ICollection<AnalysisEdge> edges)
    {
        foreach (var symbol in currentTarget.DefinesSymbols)
        {
            edges.Add(new AnalysisEdge(currentTarget.Target.TargetKey, currentTarget.Target.TargetKey, AnalysisEdgeKind.Defines, symbol.SymbolKey));
        }

        foreach (var symbol in currentTarget.UsesSymbols)
        {
            edges.Add(new AnalysisEdge(currentTarget.Target.TargetKey, currentTarget.Target.TargetKey, AnalysisEdgeKind.Uses, symbol.SymbolKey));
        }

        if (previousTarget != null)
        {
            edges.Add(new AnalysisEdge(previousTarget.Target.TargetKey, currentTarget.Target.TargetKey, AnalysisEdgeKind.Precedes));
        }
    }

    private static bool IsHighRiskMember(ISymbol memberSymbol)
    {
        if (memberSymbol is not IMethodSymbol method)
        {
            return false;
        }

        if (method.IsVirtual || method.IsOverride || method.IsAbstract)
        {
            return true;
        }

        foreach (var iface in method.ContainingType.AllInterfaces)
        {
            foreach (var ifaceMember in iface.GetMembers().OfType<IMethodSymbol>())
            {
                var implementation = method.ContainingType.FindImplementationForInterfaceMember(ifaceMember);
                if (SymbolEqualityComparer.Default.Equals(implementation, method))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
