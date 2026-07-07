using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Marking;
using RoslynPrototype.Decision;
using RoslynPrototype.Propagation;

namespace Rules;

public sealed class DeleteClassObjectCreationDeclarationPropagationRule : RuleDefinitionPropagate
{
    public override string RuleId { get; } = DeleteClassRuleIds.ObjectCreationDeclarationPropagationRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Propagate delete-class object creations to local declarators";

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
      new[]
      {
        SyntaxKind.VariableDeclarator
      };

    public override IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
        _ = context;
        foreach (var seedMark in seedMarks)
        {
            if (seedMark.SyntaxNode is not ObjectCreationExpressionSyntax and
                not ImplicitObjectCreationExpressionSyntax)
            {
                continue;
            }

            var declarator = FindInitializerDeclarator(seedMark.SyntaxNode);
            if (declarator is null)
            {
                continue;
            }

            yield return new PropagatedMarkRecord(
              RuleId,
              RuleAnalysisHelpers.CreateMark(
                RuleId,
                declarator,
                "Object creation initializer is marked; propagate mark to local declarator."),
              seedMark,
              1);
        }
    }

    private static VariableDeclaratorSyntax? FindInitializerDeclarator(SyntaxNode syntaxNode)
    {
        foreach (var ancestor in syntaxNode.Ancestors())
        {
            if (ancestor is EqualsValueClauseSyntax equalsValueClause &&
                equalsValueClause.Value.Span.Contains(syntaxNode.Span) &&
                equalsValueClause.Parent is VariableDeclaratorSyntax variableDeclarator)
            {
                return variableDeclarator;
            }
        }

        return null;
    }
}

public sealed class DeleteClassSymbolReferencePropagationRule : RuleDefinitionPropagate
{
    public override string RuleId { get; } = DeleteClassRuleIds.LocalReferencePropagationRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Propagate delete-class local declarators to same-scope references";

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
      new[]
      {
        SyntaxKind.IdentifierName
      };

    public override IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
        var markedSymbols = BuildMarkedLocalDefinitions(context, seedMarks);
        if (markedSymbols.Count == 0)
        {
            yield break;
        }

        var knownKeys = seedMarks
          .Select(mark => BuildNodeKey(mark.SyntaxNode))
          .ToHashSet();
        foreach (var reference in context.AnalysisContext.CompilationRoot.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var referencedSymbol = ResolveReferencedSymbol(context, reference);
            if (referencedSymbol is null ||
                !markedSymbols.TryGetValue(referencedSymbol, out var sourceMark) ||
                !IsSameScope(sourceMark.SyntaxNode, reference) ||
                reference.SpanStart <= sourceMark.SyntaxNode.SpanStart ||
                !knownKeys.Add(BuildNodeKey(reference)))
            {
                continue;
            }

            yield return new PropagatedMarkRecord(
              RuleId,
              RuleAnalysisHelpers.CreateMark(
                RuleId,
                reference,
                $"Symbol reference '{reference.Identifier.ValueText}' resolves to a marked delete-class local definition."),
              sourceMark,
              1);
        }
    }

    private static Dictionary<ISymbol, MarkRecord> BuildMarkedLocalDefinitions(RuleContext context, IReadOnlyList<MarkRecord> marks)
    {
        var symbols = new Dictionary<ISymbol, MarkRecord>(SymbolEqualityComparer.Default);
        foreach (var mark in marks)
        {
            if (!IsObjectCreationDefinitionMark(mark))
            {
                continue;
            }

            var symbol = ResolveDeclaredLocalSymbol(context, mark.SyntaxNode);
            if (symbol is null || symbols.ContainsKey(symbol))
            {
                continue;
            }

            symbols.Add(symbol, mark);
        }

        return symbols;
    }

    private static bool IsObjectCreationDefinitionMark(MarkRecord mark)
    {
        return mark.SyntaxNode is VariableDeclaratorSyntax &&
          mark.Reason.Contains(
            "Object creation initializer is marked",
            StringComparison.Ordinal);
    }

    private static ISymbol? ResolveDeclaredLocalSymbol(RuleContext context, SyntaxNode node)
    {
        var symbol = node is VariableDeclaratorSyntax variableDeclarator
          ? context.SemanticModel.GetDeclaredSymbol(variableDeclarator)
          : null;

        return symbol is ILocalSymbol ? symbol : null;
    }

    private static ISymbol? ResolveReferencedSymbol(RuleContext context, IdentifierNameSyntax identifierName)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(identifierName).Symbol;
        return symbol is ILocalSymbol ? symbol : null;
    }

    private static bool IsSameScope(SyntaxNode sourceNode, SyntaxNode referenceNode)
    {
        var sourceScope = FindContainingExecutableScope(sourceNode);
        var referenceScope = FindContainingExecutableScope(referenceNode);
        return sourceScope is not null &&
          referenceScope is not null &&
          ReferenceEquals(sourceScope, referenceScope);
    }

    private static SyntaxNode? FindContainingExecutableScope(SyntaxNode node)
    {
        return node.AncestorsAndSelf().FirstOrDefault(ancestor =>
          ancestor is MethodDeclarationSyntax or
            ConstructorDeclarationSyntax or
            DestructorDeclarationSyntax or
            OperatorDeclarationSyntax or
            ConversionOperatorDeclarationSyntax or
            AccessorDeclarationSyntax or
            AnonymousFunctionExpressionSyntax or
            LocalFunctionStatementSyntax);
    }

    private static (int Start, int Length, int RawKind) BuildNodeKey(SyntaxNode syntaxNode)
    {
        return (syntaxNode.SpanStart, syntaxNode.Span.Length, syntaxNode.RawKind);
    }
}

public sealed class DeleteClassMethodParameterUsagePropagationRule : RuleDefinitionPropagate
{
    private readonly DeleteClassParameterShrinkAnalyzer _analyzer = new();

    public override string RuleId { get; } = DeleteClassRuleIds.MethodParameterUsagePropagationRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Propagate delete-class method parameter usage to owning methods and mapped callsites";

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration,
        SyntaxKind.InvocationExpression
      };

    public override IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
        var knownKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seedMark in seedMarks)
        {
            if (!TryBuildPayload(context, seedMark, out var payload))
            {
                continue;
            }

            if (knownKeys.Add(DecisionCpgFactory.BuildNodeKey(payload.Method)))
            {
                yield return new PropagatedMarkRecord(
                  RuleId,
                  RuleAnalysisHelpers.CreateMark(
                    RuleId,
                    payload.Method,
                    "Method parameter type references the delete-class target; propagate to the owning method declaration."),
                  seedMark,
                  1,
                  Payload: payload);
            }

            foreach (var invocation in payload.InvocationCallsites)
            {
                if (!knownKeys.Add(DecisionCpgFactory.BuildNodeKey(invocation)))
                {
                    continue;
                }

                yield return new PropagatedMarkRecord(
                  RuleId,
                  RuleAnalysisHelpers.CreateMark(
                    RuleId,
                    invocation,
                    "Invocation passes the delete-class typed parameter; propagate to a shrinkable callsite."),
                  seedMark,
                  1,
                  Payload: payload);
            }
        }
    }

    private bool TryBuildPayload(RuleContext context, MarkRecord seedMark, out MethodParameterUsagePayload payload)
    {
        payload = null!;
        if (!string.Equals(seedMark.RuleId, DeleteClassRuleIds.TypeSyntaxMarkRuleId, StringComparison.Ordinal) ||
            seedMark.SyntaxNode is not TypeSyntax typeSyntax)
        {
            return false;
        }

        if (_analyzer.TryBuildNamedArgumentMethodPlan(context, typeSyntax, out var namedPlan))
        {
            payload = CreatePayload(
              typeSyntax,
              namedPlan.Method,
              MethodParameterUsageMode.NamedArgument,
              namedPlan.InvocationRewrites);
            return true;
        }

        if (_analyzer.TryBuildOptionalParameterMethodPlan(context, typeSyntax, out var optionalPlan))
        {
            payload = CreatePayload(
              typeSyntax,
              optionalPlan.Method,
              MethodParameterUsageMode.Optional,
              optionalPlan.InvocationRewrites);
            return true;
        }

        if (_analyzer.TryBuildParamsMethodPlan(context, typeSyntax, out var paramsPlan))
        {
            payload = CreatePayload(
              typeSyntax,
              paramsPlan.Method,
              MethodParameterUsageMode.ParamsOmitted,
              paramsPlan.InvocationRewrites);
            return true;
        }

        if (_analyzer.TryBuildPrivateMethodPlan(context, typeSyntax, out var privatePlan))
        {
            payload = CreatePayload(
              typeSyntax,
              privatePlan.Method,
              MethodParameterUsageMode.PrivatePositional,
              privatePlan.InvocationRewrites);
            return true;
        }

        if (_analyzer.TryBuildPublicMethodPlan(context, typeSyntax, out var publicPlan))
        {
            payload = CreatePayload(
              typeSyntax,
              publicPlan.Method,
              MethodParameterUsageMode.PublicPositional,
              publicPlan.InvocationRewrites);
            return true;
        }

        return false;
    }

    private static MethodParameterUsagePayload CreatePayload(TypeSyntax typeSyntax, MethodDeclarationSyntax method, MethodParameterUsageMode mode, IReadOnlyList<InvocationRewrite> invocationRewrites)
    {
        var parameterIndex = method.ParameterList.Parameters
          .Select((parameter, index) => new { parameter, index })
          .First(item => item.parameter.Type?.Span.Contains(typeSyntax.Span) == true);
        return new MethodParameterUsagePayload(
          method,
          parameterIndex.parameter,
          parameterIndex.index,
          mode,
          invocationRewrites.Select(rewrite => rewrite.Invocation).ToList());
    }
}

public sealed class DeleteClassLocalFunctionParameterUsagePropagationRule : RuleDefinitionPropagate
{
    private readonly DeleteClassParameterShrinkAnalyzer _analyzer = new();

    public override string RuleId { get; } = DeleteClassRuleIds.LocalFunctionParameterUsagePropagationRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Propagate delete-class local-function parameter usage to local functions and mapped callsites";

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
      new[]
      {
        SyntaxKind.LocalFunctionStatement,
        SyntaxKind.InvocationExpression
      };

    public override IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
        var knownKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seedMark in seedMarks)
        {
            if (!TryBuildPayload(context, seedMark, out var payload))
            {
                continue;
            }

            if (knownKeys.Add(DecisionCpgFactory.BuildNodeKey(payload.LocalFunction)))
            {
                yield return new PropagatedMarkRecord(
                  RuleId,
                  RuleAnalysisHelpers.CreateMark(
                    RuleId,
                    payload.LocalFunction,
                    "Local function parameter type references the delete-class target; propagate to the owning local function."),
                  seedMark,
                  1,
                  Payload: payload);
            }

            foreach (var invocation in payload.InvocationCallsites)
            {
                if (!knownKeys.Add(DecisionCpgFactory.BuildNodeKey(invocation)))
                {
                    continue;
                }

                yield return new PropagatedMarkRecord(
                  RuleId,
                  RuleAnalysisHelpers.CreateMark(
                    RuleId,
                    invocation,
                    "Local function invocation passes the delete-class typed parameter; propagate to a shrinkable callsite."),
                  seedMark,
                  1,
                  Payload: payload);
            }
        }
    }

    private bool TryBuildPayload(RuleContext context, MarkRecord seedMark, out LocalFunctionParameterUsagePayload payload)
    {
        payload = null!;
        if (!string.Equals(seedMark.RuleId, DeleteClassRuleIds.TypeSyntaxMarkRuleId, StringComparison.Ordinal) ||
            seedMark.SyntaxNode is not TypeSyntax typeSyntax)
        {
            return false;
        }

        if (_analyzer.TryBuildNamedArgumentLocalFunctionPlan(context, typeSyntax, out var namedPlan))
        {
            payload = CreatePayload(
              typeSyntax,
              namedPlan.LocalFunction,
              LocalFunctionParameterUsageMode.NamedArgument,
              namedPlan.InvocationRewrites);
            return true;
        }

        if (_analyzer.TryBuildOptionalParameterLocalFunctionPlan(context, typeSyntax, out var optionalPlan))
        {
            payload = CreatePayload(
              typeSyntax,
              optionalPlan.LocalFunction,
              LocalFunctionParameterUsageMode.Optional,
              optionalPlan.InvocationRewrites);
            return true;
        }

        if (_analyzer.TryBuildLocalFunctionPlan(context, typeSyntax, out var positionalPlan))
        {
            payload = CreatePayload(
              typeSyntax,
              positionalPlan.LocalFunction,
              LocalFunctionParameterUsageMode.Positional,
              positionalPlan.InvocationRewrites);
            return true;
        }

        return false;
    }

    private static LocalFunctionParameterUsagePayload CreatePayload(TypeSyntax typeSyntax, LocalFunctionStatementSyntax localFunction, LocalFunctionParameterUsageMode mode, IReadOnlyList<InvocationRewrite> invocationRewrites)
    {
        var parameterIndex = localFunction.ParameterList.Parameters
          .Select((parameter, index) => new { parameter, index })
          .First(item => item.parameter.Type?.Span.Contains(typeSyntax.Span) == true);
        return new LocalFunctionParameterUsagePayload(
          localFunction,
          parameterIndex.parameter,
          parameterIndex.index,
          mode,
          invocationRewrites.Select(rewrite => rewrite.Invocation).ToList());
    }
}

public sealed class DeleteClassIndexerParameterUsagePropagationRule : RuleDefinitionPropagate
{
    private readonly DeleteClassParameterShrinkAnalyzer _analyzer = new();

    public override string RuleId { get; } = DeleteClassRuleIds.IndexerParameterUsagePropagationRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Propagate delete-class indexer parameter usage to indexers and mapped access sites";

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
      new[]
      {
        SyntaxKind.IndexerDeclaration,
        SyntaxKind.ElementAccessExpression
      };

    public override IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
        var knownKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seedMark in seedMarks)
        {
            if (!TryBuildPayload(context, seedMark, out var payload))
            {
                continue;
            }

            if (knownKeys.Add(DecisionCpgFactory.BuildNodeKey(payload.Indexer)))
            {
                yield return new PropagatedMarkRecord(
                  RuleId,
                  RuleAnalysisHelpers.CreateMark(
                    RuleId,
                    payload.Indexer,
                    "Indexer parameter type references the delete-class target; propagate to the owning indexer declaration."),
                  seedMark,
                  1,
                  Payload: payload);
            }

            foreach (var access in payload.AccessCallsites)
            {
                if (!knownKeys.Add(DecisionCpgFactory.BuildNodeKey(access)))
                {
                    continue;
                }

                yield return new PropagatedMarkRecord(
                  RuleId,
                  RuleAnalysisHelpers.CreateMark(
                    RuleId,
                    access,
                    "Indexer access passes the delete-class typed parameter; propagate to a shrinkable access site."),
                  seedMark,
                  1,
                  Payload: payload);
            }
        }
    }

    private bool TryBuildPayload(RuleContext context, MarkRecord seedMark, out IndexerParameterUsagePayload payload)
    {
        payload = null!;
        if (!string.Equals(seedMark.RuleId, DeleteClassRuleIds.TypeSyntaxMarkRuleId, StringComparison.Ordinal) ||
            seedMark.SyntaxNode is not TypeSyntax typeSyntax)
        {
            return false;
        }

        if (_analyzer.TryBuildNamedArgumentIndexerPlan(context, typeSyntax, out var namedPlan))
        {
            payload = CreatePayload(
              typeSyntax,
              namedPlan.Indexer,
              IndexerParameterUsageMode.NamedArgument,
              namedPlan.AccessRewrites);
            return true;
        }

        if (_analyzer.TryBuildIndexerPlan(context, typeSyntax, out var positionalPlan))
        {
            payload = CreatePayload(
              typeSyntax,
              positionalPlan.Indexer,
              IndexerParameterUsageMode.Positional,
              positionalPlan.AccessRewrites);
            return true;
        }

        return false;
    }

    private static IndexerParameterUsagePayload CreatePayload(TypeSyntax typeSyntax, IndexerDeclarationSyntax indexer, IndexerParameterUsageMode mode, IReadOnlyList<ElementAccessRewrite> accessRewrites)
    {
        var parameterIndex = indexer.ParameterList.Parameters
          .Select((parameter, index) => new { parameter, index })
          .First(item => item.parameter.Type?.Span.Contains(typeSyntax.Span) == true);
        return new IndexerParameterUsagePayload(
          indexer,
          parameterIndex.parameter,
          parameterIndex.index,
          mode,
          accessRewrites.Select(rewrite => rewrite.ElementAccess).ToList());
    }
}

public sealed class DeleteClassDelegateUsageClassificationPropagationRule : RuleDefinitionPropagate
{
    private readonly DeleteClassParameterShrinkAnalyzer _analyzer = new();

    public override string RuleId { get; } = DeleteClassRuleIds.DelegateUsageClassificationPropagationRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Propagate delete-class delegate parameter usage to delegate declarations and mapped bindings";

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
      new[]
      {
        SyntaxKind.DelegateDeclaration,
        SyntaxKind.MethodDeclaration,
        SyntaxKind.LocalFunctionStatement,
        SyntaxKind.ParenthesizedLambdaExpression,
        SyntaxKind.SimpleLambdaExpression,
        SyntaxKind.AnonymousMethodExpression,
        SyntaxKind.InvocationExpression
      };

    public override IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
        var knownKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seedMark in seedMarks)
        {
            if (!TryBuildPayload(context, seedMark, out var payload))
            {
                continue;
            }

            if (knownKeys.Add(DecisionCpgFactory.BuildNodeKey(payload.DelegateDeclaration)))
            {
                yield return new PropagatedMarkRecord(
                  RuleId,
                  RuleAnalysisHelpers.CreateMark(
                    RuleId,
                    payload.DelegateDeclaration,
                    "Delegate parameter type references the delete-class target; propagate to the owning delegate declaration."),
                  seedMark,
                  1,
                  Payload: payload);
            }

            foreach (var method in payload.MethodTargets)
            {
                if (!knownKeys.Add(DecisionCpgFactory.BuildNodeKey(method)))
                {
                    continue;
                }

                yield return new PropagatedMarkRecord(
                  RuleId,
                  RuleAnalysisHelpers.CreateMark(
                    RuleId,
                    method,
                    "Delegate method-group target must shrink to stay compatible with the delete-class delegate signature."),
                  seedMark,
                  1,
                  Payload: payload);
            }

            foreach (var localFunction in payload.LocalFunctionTargets)
            {
                if (!knownKeys.Add(DecisionCpgFactory.BuildNodeKey(localFunction)))
                {
                    continue;
                }

                yield return new PropagatedMarkRecord(
                  RuleId,
                  RuleAnalysisHelpers.CreateMark(
                    RuleId,
                    localFunction,
                    "Delegate local-function target must shrink to stay compatible with the delete-class delegate signature."),
                  seedMark,
                  1,
                  Payload: payload);
            }

            foreach (var lambda in payload.LambdaTargets)
            {
                if (!knownKeys.Add(DecisionCpgFactory.BuildNodeKey(lambda)))
                {
                    continue;
                }

                yield return new PropagatedMarkRecord(
                  RuleId,
                  RuleAnalysisHelpers.CreateMark(
                    RuleId,
                    lambda,
                    "Delegate lambda binding must shrink to stay compatible with the delete-class delegate signature."),
                  seedMark,
                  1,
                  Payload: payload);
            }

            foreach (var invocation in payload.InvocationCallsites)
            {
                if (!knownKeys.Add(DecisionCpgFactory.BuildNodeKey(invocation)))
                {
                    continue;
                }

                yield return new PropagatedMarkRecord(
                  RuleId,
                  RuleAnalysisHelpers.CreateMark(
                    RuleId,
                    invocation,
                    "Delegate invocation passes the delete-class typed parameter; propagate to a shrinkable invocation chain."),
                  seedMark,
                  1,
                  Payload: payload);
            }
        }
    }

    private bool TryBuildPayload(RuleContext context, MarkRecord seedMark, out DelegateUsagePayload payload)
    {
        payload = null!;
        if (!string.Equals(seedMark.RuleId, DeleteClassRuleIds.TypeSyntaxMarkRuleId, StringComparison.Ordinal) ||
            seedMark.SyntaxNode is not TypeSyntax typeSyntax ||
            !DeleteClassParameterShrinkAnalyzer.TryResolveDelegateParameter(
              typeSyntax,
              out var delegateDeclaration,
              out var parameter,
              out var parameterIndex) ||
            context.SemanticModel.GetDeclaredSymbol(delegateDeclaration, CancellationToken.None) is not INamedTypeSymbol delegateSymbol ||
            delegateSymbol.DelegateInvokeMethod is not IMethodSymbol invokeMethod ||
            parameterIndex >= invokeMethod.Parameters.Length ||
            !DeleteClassParameterShrinkAnalyzer.TryBuildReplacementDelegate(
              delegateDeclaration,
              parameter,
              out _))
        {
            return false;
        }

        if (DeleteClassParameterShrinkAnalyzer.TryCollectDelegateUsageSummary(
              context,
              delegateSymbol,
              invokeMethod.Parameters[parameterIndex],
              parameterIndex,
              out var usageSummary))
        {
            if (usageSummary.MethodGroupTargets.Count > 0 && usageSummary.LambdaRewrites.Count == 0)
            {
                payload = CreatePayload(
                  delegateDeclaration,
                  parameter,
                  parameterIndex,
                  DelegateUsageMode.MethodGroup,
                  usageSummary);
                return true;
            }

            if (usageSummary.LambdaRewrites.Count > 0 && usageSummary.MethodGroupTargets.Count == 0)
            {
                payload = CreatePayload(
                  delegateDeclaration,
                  parameter,
                  parameterIndex,
                  DelegateUsageMode.Lambda,
                  usageSummary);
                return true;
            }

            if (usageSummary.InvocationRewrites.Count > 0 &&
                usageSummary.MethodGroupTargets.Count == 0 &&
                usageSummary.LambdaRewrites.Count == 0)
            {
                payload = CreatePayload(
                  delegateDeclaration,
                  parameter,
                  parameterIndex,
                  DelegateUsageMode.InvocationChain,
                  usageSummary);
                return true;
            }
        }

        if (DeleteClassParameterShrinkAnalyzer.HasDelegateReferences(
              context.SemanticModel.Compilation,
              delegateSymbol))
        {
            return false;
        }

        if (_analyzer.TryBuildDelegatePlan(context, typeSyntax, out var plainPlan))
        {
            payload = new DelegateUsagePayload(
              plainPlan.DelegateDeclaration,
              parameter,
              parameterIndex,
              DelegateUsageMode.PlainSignature,
              Array.Empty<MethodDeclarationSyntax>(),
              Array.Empty<LocalFunctionStatementSyntax>(),
              Array.Empty<ExpressionSyntax>(),
              Array.Empty<InvocationExpressionSyntax>());
            return true;
        }

        return false;
    }

    private static DelegateUsagePayload CreatePayload(DelegateDeclarationSyntax delegateDeclaration, ParameterSyntax parameter, int parameterIndex, DelegateUsageMode mode, DelegateUsageSummary usageSummary)
    {
        return new DelegateUsagePayload(
          delegateDeclaration,
          parameter,
          parameterIndex,
          mode,
          usageSummary.MethodRewrites.Select(rewrite => rewrite.Method).ToList(),
          usageSummary.LocalFunctionRewrites.Select(rewrite => rewrite.LocalFunction).ToList(),
          usageSummary.LambdaRewrites.Select(rewrite => rewrite.Expression).ToList(),
          usageSummary.InvocationRewrites.Select(rewrite => rewrite.Invocation).ToList());
    }
}

public sealed class DeleteClassExtensionMethodMappedCallsitePropagationRule : RuleDefinitionPropagate
{
    private readonly DeleteClassParameterShrinkAnalyzer _analyzer = new();

    public override string RuleId { get; } = DeleteClassRuleIds.ExtensionMethodMappedCallsitePropagationRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Propagate delete-class extension-method parameter usage to mapped extension callsites";

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration,
        SyntaxKind.InvocationExpression
      };

    public override IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
        var knownKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seedMark in seedMarks)
        {
            if (!TryBuildPayload(context, seedMark, out var payload))
            {
                continue;
            }

            if (knownKeys.Add(DecisionCpgFactory.BuildNodeKey(payload.Method)))
            {
                yield return new PropagatedMarkRecord(
                  RuleId,
                  RuleAnalysisHelpers.CreateMark(
                    RuleId,
                    payload.Method,
                    "Extension method non-receiver parameter type references the delete-class target; propagate to the owning method declaration."),
                  seedMark,
                  1,
                  Payload: payload);
            }

            foreach (var invocation in payload.InvocationCallsites)
            {
                if (!knownKeys.Add(DecisionCpgFactory.BuildNodeKey(invocation)))
                {
                    continue;
                }

                yield return new PropagatedMarkRecord(
                  RuleId,
                  RuleAnalysisHelpers.CreateMark(
                    RuleId,
                    invocation,
                    "Extension method invocation passes the delete-class typed parameter; propagate to a shrinkable mapped callsite."),
                  seedMark,
                  1,
                  Payload: payload);
            }
        }
    }

    private bool TryBuildPayload(RuleContext context, MarkRecord seedMark, out ExtensionMethodMappedCallsitePayload payload)
    {
        payload = null!;
        if (!string.Equals(seedMark.RuleId, DeleteClassRuleIds.TypeSyntaxMarkRuleId, StringComparison.Ordinal) ||
            seedMark.SyntaxNode is not TypeSyntax typeSyntax ||
            !_analyzer.TryBuildExtensionReceiverNonFirstParameterPlan(context, typeSyntax, out var plan))
        {
            return false;
        }

        var parameterIndex = plan.Method.ParameterList.Parameters
          .Select((parameter, index) => new { parameter, index })
          .First(item => item.parameter.Type?.Span.Contains(typeSyntax.Span) == true)
          .index;
        payload = new ExtensionMethodMappedCallsitePayload(
          plan.Method,
          plan.Method.ParameterList.Parameters[parameterIndex],
          parameterIndex,
          plan.InvocationRewrites.Select(rewrite => rewrite.Invocation).ToList());
        return true;
    }
}

public sealed class DeleteClassDeclarationHostPropagationRule : RuleDefinitionPropagate
{
    public override string RuleId { get; } = DeleteClassRuleIds.DeclarationHostPropagationRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Propagate delete-class type syntax marks to stable declaration hosts";

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
      new[]
      {
        SyntaxKind.BaseList,
        SyntaxKind.DelegateDeclaration,
        SyntaxKind.EventDeclaration,
        SyntaxKind.EventFieldDeclaration,
        SyntaxKind.FieldDeclaration,
        SyntaxKind.IndexerDeclaration,
        SyntaxKind.LocalDeclarationStatement,
        SyntaxKind.MethodDeclaration,
        SyntaxKind.PropertyDeclaration,
        SyntaxKind.SimpleBaseType
      };

    public override IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
        _ = context;
        var knownKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seedMark in seedMarks)
        {
            if (!TryBuildPayload(seedMark, out var payload, out var reason))
            {
                continue;
            }

            var hostKey = DecisionCpgFactory.BuildNodeKey(payload.HostDeclaration);
            if (!knownKeys.Add(hostKey))
            {
                continue;
            }

            yield return new PropagatedMarkRecord(
              RuleId,
              RuleAnalysisHelpers.CreateMark(
                RuleId,
                payload.HostDeclaration,
                reason),
              seedMark,
              1,
              Payload: payload);
        }
    }

    private static bool TryBuildPayload(MarkRecord seedMark, out DeclarationHostPayload payload, out string reason)
    {
        payload = null!;
        reason = string.Empty;
        if (!string.Equals(seedMark.RuleId, DeleteClassRuleIds.TypeSyntaxMarkRuleId, StringComparison.Ordinal) ||
            seedMark.SyntaxNode is not TypeSyntax typeSyntax)
        {
            return false;
        }

        if (TryResolveFieldDeclaration(typeSyntax, out var fieldDeclaration))
        {
            payload = new DeclarationHostPayload(fieldDeclaration, DeclarationHostKind.FieldDeclaration);
            reason = "Declaration type references the delete-class target; propagate to the owning field declaration.";
            return true;
        }

        if (TryResolvePropertyDeclaration(typeSyntax, out var propertyDeclaration))
        {
            payload = new DeclarationHostPayload(propertyDeclaration, DeclarationHostKind.PropertyDeclaration);
            reason = "Declaration type references the delete-class target; propagate to the owning property declaration.";
            return true;
        }

        if (TryResolvePrivateMethodReturn(typeSyntax, out var privateMethod))
        {
            payload = new DeclarationHostPayload(privateMethod, DeclarationHostKind.MethodReturnType);
            reason = "Method return type references the delete-class target; propagate to the owning private method.";
            return true;
        }

        if (TryResolveNonPrivateMethodReturn(typeSyntax, out var publicMethod))
        {
            payload = new DeclarationHostPayload(publicMethod, DeclarationHostKind.MethodReturnType);
            reason = "Method return type references the delete-class target; propagate to the owning non-private method.";
            return true;
        }

        if (TryResolveInterfaceMethod(typeSyntax, out var interfaceMethod))
        {
            payload = new DeclarationHostPayload(interfaceMethod, DeclarationHostKind.InterfaceMethod);
            reason = "Interface method signature references the delete-class target; propagate to the owning interface method.";
            return true;
        }

        if (TryResolveInterfaceProperty(typeSyntax, out var interfaceProperty))
        {
            payload = new DeclarationHostPayload(interfaceProperty, DeclarationHostKind.InterfaceProperty);
            reason = "Interface property signature references the delete-class target; propagate to the owning interface property.";
            return true;
        }

        if (TryResolveInterfaceEvent(typeSyntax, out var interfaceEvent))
        {
            payload = new DeclarationHostPayload(interfaceEvent, DeclarationHostKind.InterfaceEvent);
            reason = "Interface event signature references the delete-class target; propagate to the owning interface event.";
            return true;
        }

        if (TryResolveInterfaceIndexer(typeSyntax, out var interfaceIndexer))
        {
            payload = new DeclarationHostPayload(interfaceIndexer, DeclarationHostKind.InterfaceIndexer);
            reason = "Interface indexer signature references the delete-class target; propagate to the owning interface indexer.";
            return true;
        }

        if (TryResolveDelegateReturn(typeSyntax, out var delegateDeclaration))
        {
            payload = new DeclarationHostPayload(delegateDeclaration, DeclarationHostKind.DelegateReturnType);
            reason = "Delegate return type references the delete-class target; propagate to the owning delegate declaration.";
            return true;
        }

        if (TryResolveExtensionMethodFromReceiver(typeSyntax, out var extensionMethod))
        {
            payload = new DeclarationHostPayload(extensionMethod, DeclarationHostKind.ExtensionReceiverMethod);
            reason = "Extension method receiver type references the delete-class target; propagate to the owning extension method.";
            return true;
        }

        if (TryResolveBaseDeletionTarget(typeSyntax, out var baseDeletionTarget))
        {
            payload = new DeclarationHostPayload(baseDeletionTarget, DeclarationHostKind.BaseType);
            reason = "Base type references the delete-class target; propagate to the owning base-list deletion host.";
            return true;
        }

        if (TryResolveGenericLocalDeclaration(typeSyntax, out var localDeclaration))
        {
            payload = new DeclarationHostPayload(localDeclaration, DeclarationHostKind.LocalGenericTypeArgument);
            reason = "Local declaration type argument references the delete-class target; propagate to the owning local declaration.";
            return true;
        }

        return false;
    }

    private static bool TryResolveFieldDeclaration(TypeSyntax typeSyntax, out FieldDeclarationSyntax fieldDeclaration)
    {
        fieldDeclaration = typeSyntax.Ancestors()
          .OfType<FieldDeclarationSyntax>()
          .FirstOrDefault(candidate => candidate.Declaration.Type.Span.Contains(typeSyntax.Span))!;
        return fieldDeclaration is not null;
    }

    private static bool TryResolvePropertyDeclaration(TypeSyntax typeSyntax, out PropertyDeclarationSyntax propertyDeclaration)
    {
        propertyDeclaration = typeSyntax.Ancestors()
          .OfType<PropertyDeclarationSyntax>()
          .FirstOrDefault(candidate => candidate.Type.Span.Contains(typeSyntax.Span))!;
        return propertyDeclaration is not null &&
          propertyDeclaration.Parent is not InterfaceDeclarationSyntax;
    }

    private static bool TryResolvePrivateMethodReturn(TypeSyntax typeSyntax, out MethodDeclarationSyntax methodDeclaration)
    {
        methodDeclaration = typeSyntax.Ancestors()
          .OfType<MethodDeclarationSyntax>()
          .FirstOrDefault(candidate => candidate.ReturnType.Span.Contains(typeSyntax.Span))!;
        return methodDeclaration is not null &&
          DeleteClassMethodProposalSafety.IsSafePrivateMethod(methodDeclaration);
    }

    private static bool TryResolveNonPrivateMethodReturn(TypeSyntax typeSyntax, out MethodDeclarationSyntax methodDeclaration)
    {
        methodDeclaration = typeSyntax.Ancestors()
          .OfType<MethodDeclarationSyntax>()
          .FirstOrDefault(candidate => candidate.ReturnType.Span.Contains(typeSyntax.Span))!;
        return methodDeclaration is not null &&
          DeleteClassMethodProposalSafety.IsSafeNonPrivateMethod(methodDeclaration);
    }

    private static bool TryResolveInterfaceMethod(TypeSyntax typeSyntax, out MethodDeclarationSyntax methodDeclaration)
    {
        methodDeclaration = typeSyntax.Ancestors()
          .OfType<MethodDeclarationSyntax>()
          .FirstOrDefault(candidate =>
            candidate.ReturnType.Span.Contains(typeSyntax.Span) ||
            candidate.ParameterList.Parameters.Any(parameter => parameter.Type?.Span.Contains(typeSyntax.Span) == true))!;
        return methodDeclaration?.Parent is InterfaceDeclarationSyntax;
    }

    private static bool TryResolveInterfaceProperty(TypeSyntax typeSyntax, out PropertyDeclarationSyntax propertyDeclaration)
    {
        propertyDeclaration = typeSyntax.Ancestors()
          .OfType<PropertyDeclarationSyntax>()
          .FirstOrDefault(candidate => candidate.Type.Span.Contains(typeSyntax.Span))!;
        return propertyDeclaration?.Parent is InterfaceDeclarationSyntax;
    }

    private static bool TryResolveInterfaceEvent(TypeSyntax typeSyntax, out SyntaxNode eventDeclaration)
    {
        var explicitEvent = typeSyntax.Ancestors()
          .OfType<EventDeclarationSyntax>()
          .FirstOrDefault(candidate => candidate.Type.Span.Contains(typeSyntax.Span));
        if (explicitEvent?.Parent is InterfaceDeclarationSyntax)
        {
            eventDeclaration = explicitEvent;
            return true;
        }

        var eventField = typeSyntax.Ancestors()
          .OfType<EventFieldDeclarationSyntax>()
          .FirstOrDefault(candidate => candidate.Declaration.Type.Span.Contains(typeSyntax.Span));
        if (eventField?.Parent is InterfaceDeclarationSyntax)
        {
            eventDeclaration = eventField;
            return true;
        }

        eventDeclaration = typeSyntax;
        return false;
    }

    private static bool TryResolveInterfaceIndexer(TypeSyntax typeSyntax, out IndexerDeclarationSyntax indexerDeclaration)
    {
        indexerDeclaration = typeSyntax.Ancestors()
          .OfType<IndexerDeclarationSyntax>()
          .FirstOrDefault(candidate =>
            candidate.Type.Span.Contains(typeSyntax.Span) ||
            candidate.ParameterList.Parameters.Any(parameter => parameter.Type?.Span.Contains(typeSyntax.Span) == true))!;
        return indexerDeclaration?.Parent is InterfaceDeclarationSyntax;
    }

    private static bool TryResolveDelegateReturn(TypeSyntax typeSyntax, out DelegateDeclarationSyntax delegateDeclaration)
    {
        delegateDeclaration = typeSyntax.Ancestors()
          .OfType<DelegateDeclarationSyntax>()
          .FirstOrDefault(candidate => candidate.ReturnType.Span.Contains(typeSyntax.Span))!;
        return delegateDeclaration is not null;
    }

    private static bool TryResolveExtensionMethodFromReceiver(TypeSyntax typeSyntax, out MethodDeclarationSyntax methodDeclaration)
    {
        methodDeclaration = null!;
        var parameter = typeSyntax.Ancestors()
          .OfType<ParameterSyntax>()
          .FirstOrDefault(candidate =>
            candidate.Type?.Span.Contains(typeSyntax.Span) == true &&
            candidate.Modifiers.Any(SyntaxKind.ThisKeyword));
        if (parameter?.Parent is not ParameterListSyntax parameterList ||
            parameterList.Parameters.FirstOrDefault() != parameter ||
            parameterList.Parent is not MethodDeclarationSyntax method)
        {
            return false;
        }

        methodDeclaration = method;
        return DeleteClassMethodProposalSafety.IsSafeExtensionReceiverMethod(methodDeclaration);
    }

    private static bool TryResolveBaseDeletionTarget(TypeSyntax typeSyntax, out SyntaxNode deletionTarget)
    {
        var simpleBaseType = typeSyntax.Ancestors()
          .OfType<SimpleBaseTypeSyntax>()
          .FirstOrDefault(candidate => candidate.Type.Span.Contains(typeSyntax.Span));
        if (simpleBaseType?.Parent is not BaseListSyntax baseList)
        {
            deletionTarget = typeSyntax;
            return false;
        }

        deletionTarget = baseList.Types.Count == 1
          ? baseList
          : simpleBaseType;
        return true;
    }

    private static bool TryResolveGenericLocalDeclaration(TypeSyntax typeSyntax, out LocalDeclarationStatementSyntax localDeclaration)
    {
        localDeclaration = null!;
        if (!typeSyntax.Ancestors().OfType<TypeArgumentListSyntax>().Any())
        {
            return false;
        }

        var variableDeclaration = typeSyntax.Ancestors()
          .OfType<VariableDeclarationSyntax>()
          .FirstOrDefault(candidate => candidate.Type.Span.Contains(typeSyntax.Span));
        if (variableDeclaration?.Parent is not LocalDeclarationStatementSyntax statement)
        {
            return false;
        }

        localDeclaration = statement;
        return true;
    }
}

public sealed class DeleteClassIfStructureCompletionPropagationRule : RuleDefinitionPropagate
{
    public override string RuleId { get; } = DeleteClassRuleIds.IfStructureCompletionPropagationRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Propagate delete-class if/elseif/else completion state as structured payloads";

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
      new[]
      {
        SyntaxKind.IdentifierName,
        SyntaxKind.ThisExpression,
        SyntaxKind.BaseExpression,
        SyntaxKind.VariableDeclarator,
        SyntaxKind.SimpleMemberAccessExpression,
        SyntaxKind.MemberBindingExpression,
        SyntaxKind.InvocationExpression,
        SyntaxKind.ElementAccessExpression,
        SyntaxKind.ConditionalAccessExpression,
        SyntaxKind.ObjectCreationExpression,
        SyntaxKind.ImplicitObjectCreationExpression,
        SyntaxKind.LogicalNotExpression,
        SyntaxKind.SimpleAssignmentExpression,
        SyntaxKind.AddAssignmentExpression,
        SyntaxKind.SubtractAssignmentExpression,
        SyntaxKind.MultiplyAssignmentExpression,
        SyntaxKind.DivideAssignmentExpression,
        SyntaxKind.LogicalAndExpression,
        SyntaxKind.LogicalOrExpression,
        SyntaxKind.TupleExpression,
        SyntaxKind.Block,
        SyntaxKind.LocalDeclarationStatement,
        SyntaxKind.ExpressionStatement,
        SyntaxKind.ElseClause,
        SyntaxKind.IfStatement,
        SyntaxKind.SwitchStatement,
        SyntaxKind.SwitchSection,
        SyntaxKind.ReturnStatement
      };

    public override IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
        return DeleteSObjectPropagationHelpers.EnumerateIfStructureCompletionPropagations(
          context,
          seedMarks,
          RuleId);
    }
}
