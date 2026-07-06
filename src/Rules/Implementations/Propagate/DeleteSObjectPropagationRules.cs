using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MinimalRoslynCpg.Analysis;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;

namespace Rules;

public abstract class DeleteSObjectPropagationRuleBase : RuleDefinitionPropagate
{
    protected static readonly IReadOnlyList<SyntaxKind> SharedAllowedPropagateNodeKinds =
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

    public override string GroupKey { get; } = DeleteSObjectRuleIds.GroupKey;

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds =>
      SharedAllowedPropagateNodeKinds;

    protected static (int Start, int Length, int RawKind) BuildNodeKey(SyntaxNode syntaxNode)
    {
        return (syntaxNode.SpanStart, syntaxNode.Span.Length, syntaxNode.RawKind);
    }
}

public sealed class DeleteSObjectAssignmentLeftValuePropagationRule : DeleteSObjectPropagationRuleBase
{
    public override string RuleId { get; } = DeleteSObjectRuleIds.AssignmentLeftValuePropagationRuleId;

    public override string Name { get; } = "Propagate s-object marks from assignment right values to left values";

    public override IEnumerable<PropagatedMarkRecord> Propagate(
      RuleContext context,
      IReadOnlyList<MarkRecord> seedMarks)
    {
        _ = context;
        foreach (var seedMark in seedMarks)
        {
            if (seedMark.SyntaxNode is not ExpressionSyntax expression)
            {
                continue;
            }

            foreach (var ancestor in expression.Ancestors())
            {
                if (ancestor is AssignmentExpressionSyntax assignmentExpression &&
                    assignmentExpression.Right.Span.Contains(expression.Span))
                {
                    yield return new PropagatedMarkRecord(
                      RuleId,
                      RuleAnalysisHelpers.CreateMark(
                        RuleId,
                        assignmentExpression.Left,
                        "Assignment right value is marked; propagate mark to assignment left value."),
                      seedMark,
                      1);
                    break;
                }
            }
        }
    }
}

public sealed class DeleteSObjectDefinitionInitializerPropagationRule : DeleteSObjectPropagationRuleBase
{
    public override string RuleId { get; } = DeleteSObjectRuleIds.DefinitionInitializerPropagationRuleId;

    public override string Name { get; } = "Propagate s-object marks from definition initializers to declarators";

    public override IEnumerable<PropagatedMarkRecord> Propagate(
      RuleContext context,
      IReadOnlyList<MarkRecord> seedMarks)
    {
        _ = context;
        foreach (var seedMark in seedMarks)
        {
            if (seedMark.SyntaxNode is not ExpressionSyntax expression)
            {
                continue;
            }

            foreach (var ancestor in expression.Ancestors())
            {
                if (ancestor is EqualsValueClauseSyntax equalsValueClause &&
                    equalsValueClause.Value.Span.Contains(expression.Span) &&
                    equalsValueClause.Parent is VariableDeclaratorSyntax variableDeclarator)
                {
                    yield return new PropagatedMarkRecord(
                      RuleId,
                      RuleAnalysisHelpers.CreateMark(
                        RuleId,
                        variableDeclarator,
                        "Definition initializer is marked; propagate mark to defined left value."),
                      seedMark,
                      1);
                    break;
                }
            }
        }
    }
}

public sealed class DeleteSObjectLogicalConditionPropagationRule : DeleteSObjectPropagationRuleBase
{
    public override string RuleId { get; } = DeleteSObjectRuleIds.LogicalPropagationRuleId;

    public override string Name { get; } = "Propagate s-object marks into logical condition hosts";

    public override IEnumerable<PropagatedMarkRecord> Propagate(
      RuleContext context,
      IReadOnlyList<MarkRecord> seedMarks)
    {
        var targetNames = ParseTargetNames(context);
        if (targetNames.Count == 0)
        {
            yield break;
        }

        var knownKeys = seedMarks
          .Select(mark => BuildNodeKey(mark.SyntaxNode))
          .ToHashSet();
        var analyzer = new LogicalConditionMarkAnalyzer();
        foreach (var seedMark in seedMarks)
        {
            if (seedMark.SyntaxNode is not ExpressionSyntax expression ||
                !analyzer.CanAnalyze(expression, context.AnalysisContext))
            {
                continue;
            }

            LogicalConditionMarkAnalysis analysis;
            try
            {
                analysis = analyzer.Analyze(
                  expression,
                  string.Join(",", targetNames),
                  context.AnalysisContext);
            }
            catch (InvalidOperationException exception) when (
                exception.Message.StartsWith(
                  "Could not resolve target symbol",
                  StringComparison.Ordinal))
            {
                continue;
            }

            if (analysis.PreferredMarkedNode is BinaryExpressionSyntax logicalHost &&
                (logicalHost.IsKind(SyntaxKind.LogicalAndExpression) ||
                 logicalHost.IsKind(SyntaxKind.LogicalOrExpression)) &&
                DeleteSObjectPropagationHelpers.TryBuildLogicalHostPayload(
                  context,
                  logicalHost,
                  analysis.Hits.Select(hit => hit.Node)) is not null)
            {
                continue;
            }

            if (ReferenceEquals(analysis.PreferredMarkedNode, expression) ||
                analysis.Hits.Count <= 1 && analysis.OperandGroups.Count == 0 ||
                !knownKeys.Add(BuildNodeKey(analysis.PreferredMarkedNode)))
            {
                continue;
            }

            yield return new PropagatedMarkRecord(
              RuleId,
              RuleAnalysisHelpers.CreateMark(
                RuleId,
                analysis.PreferredMarkedNode,
                "Logical condition group is marked; lift atomic hits to the logical host."),
              seedMark,
              1);
        }
    }

    private static IReadOnlyList<string> ParseTargetNames(RuleContext context)
    {
        if (!context.TryGetOption("target-name", out var targetName) ||
            string.IsNullOrWhiteSpace(targetName))
        {
            return Array.Empty<string>();
        }

        return targetName
          .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
          .Where(name => !string.IsNullOrWhiteSpace(name))
          .Distinct(StringComparer.Ordinal)
          .ToList();
    }
}

public sealed class DeleteSObjectLogicalOperandGroupPropagationRule : DeleteSObjectPropagationRuleBase
{
    public override string RuleId { get; } = DeleteSObjectRuleIds.LogicalOperandGroupPropagationRuleId;

    public override string Name { get; } = "Propagate s-object logical operand groups as structured payloads";

    public override IEnumerable<PropagatedMarkRecord> Propagate(
      RuleContext context,
      IReadOnlyList<MarkRecord> seedMarks)
    {
        var targetNames = DeleteSObjectPropagationHelpers.ParseTargetNames(context);
        if (targetNames.Count == 0)
        {
            yield break;
        }

        var analyzer = new LogicalConditionMarkAnalyzer();
        var knownKeys = new HashSet<(int Start, int Length, int RawKind)>();
        foreach (var seedMark in seedMarks)
        {
            if (seedMark.SyntaxNode is not ExpressionSyntax expression ||
                !analyzer.CanAnalyze(expression, context.AnalysisContext))
            {
                continue;
            }

            LogicalConditionMarkAnalysis analysis;
            try
            {
                analysis = analyzer.Analyze(
                  expression,
                  string.Join(",", targetNames),
                  context.AnalysisContext);
            }
            catch (InvalidOperationException exception) when (
                exception.Message.StartsWith(
                  "Could not resolve target symbol",
                  StringComparison.Ordinal))
            {
                continue;
            }

            if (analysis.PreferredMarkedNode is not BinaryExpressionSyntax host ||
                (!host.IsKind(SyntaxKind.LogicalAndExpression) &&
                 !host.IsKind(SyntaxKind.LogicalOrExpression)) ||
                !knownKeys.Add(BuildNodeKey(host)))
            {
                continue;
            }

            var payload = DeleteSObjectPropagationHelpers.TryBuildLogicalHostPayload(
              context,
              host,
              analysis.Hits.Select(hit => hit.Node));
            if (payload is null)
            {
                continue;
            }

            yield return new PropagatedMarkRecord(
              RuleId,
              RuleAnalysisHelpers.CreateMark(
                RuleId,
                host,
                "Logical condition group is marked; propagate removable and surviving operands to the logical host."),
              seedMark,
              1,
              Payload: payload);
        }
    }
}

public sealed class DeleteSObjectIfStructureCompletionPropagationRule : DeleteSObjectPropagationRuleBase
{
    public override string RuleId { get; } = DeleteSObjectRuleIds.IfStructureCompletionPropagationRuleId;

    public override string Name { get; } = "Propagate s-object if/elseif/else completion state as structured payloads";

    public override IEnumerable<PropagatedMarkRecord> Propagate(
      RuleContext context,
      IReadOnlyList<MarkRecord> seedMarks)
    {
        return DeleteSObjectPropagationHelpers.EnumerateIfStructureCompletionPropagations(
          context,
          seedMarks,
          RuleId);
    }
}

public sealed class DeleteSObjectSymbolReferencePropagationRule : DeleteSObjectPropagationRuleBase
{
    public override string RuleId { get; } = DeleteSObjectRuleIds.SymbolReferencePropagationRuleId;

    public override string Name { get; } = "Propagate s-object marks from marked definitions to symbol references";

    public override IEnumerable<PropagatedMarkRecord> Propagate(
      RuleContext context,
      IReadOnlyList<MarkRecord> seedMarks)
    {
        var markedSymbols = BuildMarkedLocalDefinitions(context, seedMarks);
        if (markedSymbols.Count == 0)
        {
            yield break;
        }

        var knownKeys = seedMarks
          .Select(mark => BuildNodeKey(mark.SyntaxNode))
          .ToHashSet();
        var root = context.AnalysisContext.CompilationRoot;
        foreach (var reference in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var referencedSymbol = ResolveReferencedSymbol(context, reference);
            if (referencedSymbol is null ||
                !markedSymbols.TryGetValue(referencedSymbol, out var sourceMark) ||
                !IsSameScope(sourceMark.SyntaxNode, reference) ||
                !knownKeys.Add(BuildNodeKey(reference)))
            {
                continue;
            }

            yield return new PropagatedMarkRecord(
              RuleId,
              RuleAnalysisHelpers.CreateMark(
                RuleId,
                reference,
                $"Symbol reference '{reference.Identifier.ValueText}' resolves to a marked definition."),
              sourceMark,
              1);
        }
    }

    private static Dictionary<ISymbol, MarkRecord> BuildMarkedLocalDefinitions(
      RuleContext context,
      IReadOnlyList<MarkRecord> marks)
    {
        var symbols = new Dictionary<ISymbol, MarkRecord>(SymbolEqualityComparer.Default);
        foreach (var mark in marks)
        {
            if (!IsInitializerDefinitionMark(mark))
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

    private static bool IsInitializerDefinitionMark(MarkRecord mark)
    {
        return mark.SyntaxNode is VariableDeclaratorSyntax &&
          mark.Reason.Contains(
            "Definition initializer is marked",
            StringComparison.Ordinal);
    }

    private static ISymbol? ResolveDeclaredLocalSymbol(
      RuleContext context,
      SyntaxNode node)
    {
        var symbol = node is VariableDeclaratorSyntax variableDeclarator
          ? context.SemanticModel.GetDeclaredSymbol(variableDeclarator)
          : null;

        if (symbol is ILocalSymbol)
        {
            return symbol;
        }

        return null;
    }

    private static ISymbol? ResolveReferencedSymbol(
      RuleContext context,
      IdentifierNameSyntax identifierName)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(identifierName).Symbol;
        return symbol is ILocalSymbol or IParameterSymbol ? symbol : null;
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
}

internal static class DeleteSObjectPropagationHelpers
{
    internal static IReadOnlyList<string> ParseTargetNames(RuleContext context)
    {
        if (!context.TryGetOption("target-name", out var targetName) ||
            string.IsNullOrWhiteSpace(targetName))
        {
            return Array.Empty<string>();
        }

        return targetName
          .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
          .Where(name => !string.IsNullOrWhiteSpace(name))
          .Distinct(StringComparer.Ordinal)
          .ToList();
    }

    internal static LogicalHostPayload? TryBuildLogicalHostPayload(
      RuleContext context,
      BinaryExpressionSyntax host,
      IEnumerable<SyntaxNode> sourceNodes)
    {
        var removableNodes = sourceNodes
          .Where(node => host.Span.Contains(node.Span))
          .OfType<ExpressionSyntax>()
          .DistinctBy(node => (node.SpanStart, node.Span.Length, node.RawKind))
          .OrderBy(node => node.SpanStart)
          .ThenByDescending(node => node.Span.Length)
          .ToList();
        if (removableNodes.Count == 0)
        {
            return null;
        }

        var operands = new BinaryExpressionAnalyzer()
          .Analyze(host, removableNodes[0], context.AnalysisContext)
          .AffectedSyntaxTree
          .OfType<ExpressionSyntax>()
          .Where(node => node is not BinaryExpressionSyntax nested || !nested.IsKind(host.Kind()))
          .ToList();
        var removableOperands = new List<ExpressionSyntax>();
        var survivorOperands = new List<ExpressionSyntax>();

        foreach (var operand in operands)
        {
            if (ShouldRemoveOperand(operand, removableNodes))
            {
                removableOperands.Add(operand);
                continue;
            }

            survivorOperands.Add(operand);
        }

        if (removableOperands.Count == 0 ||
            survivorOperands.Count == 0 ||
            survivorOperands.Count == operands.Count)
        {
            return null;
        }

        return new LogicalHostPayload(host, removableOperands, survivorOperands);
    }

    internal static IEnumerable<PropagatedMarkRecord> EnumerateIfStructureCompletionPropagations(
      RuleContext context,
      IReadOnlyList<MarkRecord> seedMarks,
      string ruleId)
    {
        var knownKeys = new HashSet<(int Start, int Length, int RawKind)>();
        foreach (var seedMark in seedMarks)
        {
            var payload = DeleteSObjectProposalHelpers.TryBuildIfStructureCompletionPayload(
              context,
              seedMark.SyntaxNode);
            if (payload is null)
            {
                continue;
            }

            var decisionNode = DeleteSObjectProposalHelpers.GetIfStructureDecisionNode(payload);
            if (!knownKeys.Add(DeleteSObjectProposalHelpers.BuildNodeKey(decisionNode)))
            {
                continue;
            }

            yield return new PropagatedMarkRecord(
              ruleId,
              RuleAnalysisHelpers.CreateMark(
                ruleId,
                decisionNode,
                BuildIfStructureCompletionReason(payload.Kind)),
              seedMark,
              1,
              Payload: payload);
        }
    }

    private static bool ShouldRemoveOperand(
      ExpressionSyntax operand,
      IReadOnlyList<ExpressionSyntax> sourceNodes)
    {
        return sourceNodes.Any(sourceNode =>
          operand.Span.Contains(sourceNode.Span) ||
          sourceNode.Span.Contains(operand.Span));
    }

    private static string BuildIfStructureCompletionReason(IfStructureCompletionKind kind)
    {
        return kind switch
        {
            IfStructureCompletionKind.DeleteWholeIf =>
              "If/else structure is fully marked; delete the whole if statement.",
            IfStructureCompletionKind.DeleteOwningElseClause =>
              "Else-if section is fully marked and has no remaining tail; remove owning else clause.",
            IfStructureCompletionKind.ReplaceIfWithElseIfTail =>
              "If section is fully marked; replace it with the remaining elseif branch.",
            IfStructureCompletionKind.ReplaceIfWithElseTail =>
              "If section is fully marked; replace it with the remaining else branch.",
            IfStructureCompletionKind.ReplaceOwningElseWithElseTail =>
              "Else-if section is fully marked; collapse its owning else to the remaining else branch.",
            _ => "If structure completion is propagated."
        };
    }
}
