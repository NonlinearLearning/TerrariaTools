using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Analysis;
using RoslynPrototype.Lifting;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;
using Rules;

namespace RoslynPrototype.Decision;

public static class DeleteSObjectProposalHelpers
{
    public static readonly IReadOnlyList<SyntaxKind> LogicalConflictNodeKinds =
      new[]
      {
        SyntaxKind.LogicalAndExpression,
        SyntaxKind.LogicalOrExpression
      };

    public static readonly IReadOnlyList<SyntaxKind> IfConflictNodeKinds =
      new[]
      {
        SyntaxKind.IfStatement
      };

    public static readonly IReadOnlyList<SyntaxKind> ControlConflictNodeKinds =
      new[]
      {
        SyntaxKind.ForStatement,
        SyntaxKind.WhileStatement,
        SyntaxKind.DoStatement,
        SyntaxKind.SwitchStatement,
        SyntaxKind.ReturnStatement
      };

    public static readonly IReadOnlyList<SyntaxKind> DefaultConflictNodeKinds =
      new[]
      {
        SyntaxKind.IfStatement,
        SyntaxKind.ForStatement,
        SyntaxKind.WhileStatement,
        SyntaxKind.DoStatement,
        SyntaxKind.SwitchStatement,
        SyntaxKind.TryStatement,
        SyntaxKind.ReturnStatement,
        SyntaxKind.LogicalAndExpression,
        SyntaxKind.LogicalOrExpression,
        SyntaxKind.ConditionalExpression
      };

    public static readonly IReadOnlyList<SyntaxKind> MergeableNodeKinds =
      new[]
      {
        SyntaxKind.IdentifierName,
        SyntaxKind.SimpleMemberAccessExpression,
        SyntaxKind.InvocationExpression,
        SyntaxKind.ElementAccessExpression,
        SyntaxKind.NumericLiteralExpression,
        SyntaxKind.StringLiteralExpression,
        SyntaxKind.TrueLiteralExpression,
        SyntaxKind.FalseLiteralExpression,
        SyntaxKind.NullLiteralExpression,
        SyntaxKind.ParenthesizedExpression,
        SyntaxKind.CastExpression,
        SyntaxKind.AddExpression,
        SyntaxKind.SubtractExpression,
        SyntaxKind.MultiplyExpression,
        SyntaxKind.DivideExpression
      };

    public static IReadOnlyList<(MarkRecord Mark, MarkRecord SourceMark)> EnumerateDerivedMarks(IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        return propagatedMarks
          .Select(mark => (
            Mark: mark.Mark,
            SourceMark: mark.SourceMark,
            IsSymbolReference: IsSymbolReferencePropagation(mark),
            IsStructuredPayload: IsStructuredPropagationPayload(mark.Payload)))
          .Concat(liftedMarks.Select(mark => (
            Mark: mark.Mark,
            SourceMark: mark.SourceMark,
            IsSymbolReference: false,
            IsStructuredPayload: false)))
          .Where(mark => !mark.IsSymbolReference && !mark.IsStructuredPayload)
          .Select(mark => (mark.Mark, mark.SourceMark))
          .DistinctBy(mark => BuildNodeKey(mark.Mark.SyntaxNode))
          .OrderBy(mark => mark.Mark.SyntaxNode.SpanStart)
          .ThenByDescending(mark => mark.Mark.SyntaxNode.Span.Length)
          .ToList();
    }

    public static IReadOnlyList<(MarkRecord Mark, MarkRecord SourceMark)> EnumerateActiveDerivedMarks(IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        var derivedMarks = EnumerateDerivedMarks(propagatedMarks, liftedMarks);
        var coveredDerivedKeys = BuildCoveredSeedKeys(
          derivedMarks.Select(item => item.Mark).ToList(),
          derivedMarks.Select(item => item.Mark).ToList());
        return derivedMarks
          .Where(item => !coveredDerivedKeys.Contains(BuildNodeKey(item.Mark.SyntaxNode)))
          .ToList();
    }

    public static IEnumerable<MarkRecord> EnumerateUncoveredSeedMarks(IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        var derivedMarks = propagatedMarks
          .Select(mark => mark.Mark)
          .Concat(liftedMarks.Select(mark => mark.Mark))
          .DistinctBy(mark => BuildNodeKey(mark.SyntaxNode))
          .ToList();
        var coveredSeedKeys = BuildCoveredSeedKeys(seedMarks, derivedMarks);

        foreach (var seedMark in seedMarks)
        {
            if (coveredSeedKeys.Contains(BuildNodeKey(seedMark.SyntaxNode)))
            {
                continue;
            }

            yield return seedMark;
        }
    }

    public static bool IsSymbolReferencePropagation(PropagatedMarkRecord propagatedMark)
    {
        return propagatedMark.Mark.Reason.StartsWith(
          "Symbol reference ",
          StringComparison.Ordinal);
    }

    public static IEnumerable<LogicalHostPayload> EnumerateLogicalHostPayloads(IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        return propagatedMarks
          .Where(mark => mark.Payload is LogicalHostPayload)
          .Select(mark => (LogicalHostPayload)mark.Payload!)
          .DistinctBy(payload => BuildNodeKey(payload.Host))
          .OrderBy(payload => payload.Host.SpanStart)
          .ThenByDescending(payload => payload.Host.Span.Length);
    }

    public static IEnumerable<IfStructureCompletionPayload> EnumerateIfStructureCompletionPayloads(IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        return propagatedMarks
          .Where(mark => mark.Payload is IfStructureCompletionPayload)
          .Select(mark => (IfStructureCompletionPayload)mark.Payload!)
          .DistinctBy(payload => BuildNodeKey(GetIfStructureDecisionNode(payload)))
          .OrderBy(payload => GetIfStructureDecisionNode(payload).SpanStart)
          .ThenByDescending(payload => GetIfStructureDecisionNode(payload).Span.Length);
    }

    public static HashSet<(int Start, int Length, int RawKind)> BuildCoveredSeedKeys(IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<MarkRecord> propagatedMarks)
    {
        var coveredSeedKeys = new HashSet<(int Start, int Length, int RawKind)>();
        foreach (var seedMark in seedMarks)
        {
            if (propagatedMarks.Any(propagatedMark =>
                  !ReferenceEquals(propagatedMark.SyntaxNode, seedMark.SyntaxNode) &&
                  propagatedMark.SyntaxNode.Span.Contains(seedMark.SyntaxNode.Span)))
            {
                coveredSeedKeys.Add(BuildNodeKey(seedMark.SyntaxNode));
            }
        }

        return coveredSeedKeys;
    }

    public static ExpressionSyntax? BuildLogicalReplacement(LogicalHostPayload payload)
    {
        if (payload.RemovableOperands.Count == 0 || payload.SurvivorOperands.Count == 0)
        {
            return null;
        }

        var survivors = payload.SurvivorOperands
          .Select(operand => operand.WithoutTrivia())
          .ToList();
        if (survivors.Count == 0)
        {
            return null;
        }

        var replacement = survivors[0];
        for (var index = 1; index < survivors.Count; index++)
        {
            replacement = SyntaxFactory.BinaryExpression(
              payload.Host.Kind(),
              replacement,
              survivors[index]);
        }

        return replacement;
    }

    public static DecisionUnit CreateLogicalReplaceDecision(string ruleId, BinaryExpressionSyntax anchorNode, ExpressionSyntax replacementNode)
    {
        var anchorFragment = DecisionCpgFactory.CreateFragment(
          BuildFragmentId(anchorNode),
          anchorNode,
          "anchor",
          DecisionActionKind.Replace);
        var replacementFragment = DecisionCpgFactory.CreateFragment(
          BuildFragmentId(replacementNode),
          replacementNode.WithoutTrivia(),
          "replacement",
          DecisionActionKind.Replace);
        var unitNode = DecisionCpgFactory.CreateUnit(
          ruleId,
          DecisionActionKind.Replace,
          anchorFragment,
          reason: $"Reduced {anchorNode.Kind()} to the surviving operand.",
          conflictKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          mergeKey: DecisionCpgFactory.BuildNodeKey(anchorNode));

        return new DecisionUnit(
          ruleId,
          DecisionActionKind.Replace,
          unitNode,
          new[] { anchorFragment, replacementFragment },
          new[]
          {
            DecisionCpgFactory.CreateContainment(unitNode, anchorFragment),
            DecisionCpgFactory.CreateContainment(unitNode, replacementFragment),
            DecisionCpgFactory.CreateRelation("reduced-to", anchorFragment, replacementFragment)
          },
          DecisionCpgFactory.CreateSyntaxBindings(
            (anchorFragment, anchorNode),
            (replacementFragment, replacementNode.WithoutTrivia())),
          mergeKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          conflictKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          reason: $"Reduced {anchorNode.Kind()} to the surviving operand.");
    }

    public static bool TryBuildIfStructureDecisionFromMark(string ruleId, IfStructureCompletionPayload payload, out DecisionUnit? decision, out IReadOnlyList<SyntaxNode> consumedNodes)
    {
        decision = null;
        consumedNodes = BuildIfStructureConsumedNodes(payload);

        switch (payload.Kind)
        {
            case IfStructureCompletionKind.ReplaceIfWithElseIfTail:
                if (payload.TailNode is not IfStatementSyntax elseIfTail)
                {
                    return false;
                }

                decision = CreateStatementReplaceDecision(
                  ruleId,
                  payload.AnchorIf,
                  elseIfTail,
                  "If section is fully marked; replace it with the remaining elseif branch.");
                return true;
            case IfStructureCompletionKind.DeleteWholeIf:
                decision = DeleteDecisionFactory.CreateDeleteDecision(
                  ruleId,
                  payload.AnchorIf,
                  "If/else structure is fully marked; delete the whole if statement.");
                return true;
            case IfStructureCompletionKind.ReplaceOwningElseWithElseTail:
                if (payload.ParentElseClause is null ||
                    payload.TailNode is not ElseClauseSyntax elseTail)
                {
                    return false;
                }

                decision = CreateElseClauseReplaceDecision(
                  ruleId,
                  payload.ParentElseClause,
                  SyntaxFactory.ElseClause(elseTail.Statement.WithoutTrivia()),
                  "Else-if section is fully marked; collapse its owning else to the remaining else branch.");
                return true;
            case IfStructureCompletionKind.ReplaceIfWithElseTail:
                if (payload.TailNode is not ElseClauseSyntax tailElse)
                {
                    return false;
                }

                decision = CreateStatementReplaceDecision(
                  ruleId,
                  payload.AnchorIf,
                  tailElse.Statement,
                  "If section is fully marked; replace it with the remaining else branch.");
                return true;
            case IfStructureCompletionKind.DeleteOwningElseClause:
                if (payload.ParentElseClause is null)
                {
                    return false;
                }

                decision = DeleteDecisionFactory.CreateDeleteDecision(
                  ruleId,
                  payload.ParentElseClause,
                  "Else-if section is fully marked and has no remaining tail; remove owning else clause.",
                  payload.AnchorIf,
                  conflictKey: DecisionCpgFactory.BuildNodeKey(payload.AnchorIf));
                return true;
            default:
                return false;
        }
    }

    public static DecisionUnit CreateStatementReplaceDecision(string ruleId, StatementSyntax anchorNode, StatementSyntax replacementNode, string reason)
    {
        var anchorFragment = DecisionCpgFactory.CreateFragment(
          BuildFragmentId(anchorNode),
          anchorNode,
          "anchor",
          DecisionActionKind.Replace);
        var replacementFragment = DecisionCpgFactory.CreateFragment(
          BuildFragmentId(replacementNode),
          replacementNode.WithoutTrivia(),
          "replacement",
          DecisionActionKind.Replace);
        var unitNode = DecisionCpgFactory.CreateUnit(
          ruleId,
          DecisionActionKind.Replace,
          anchorFragment,
          reason: reason,
          conflictKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          mergeKey: DecisionCpgFactory.BuildNodeKey(anchorNode));

        return new DecisionUnit(
          ruleId,
          DecisionActionKind.Replace,
          unitNode,
          new[] { anchorFragment, replacementFragment },
          new[]
          {
            DecisionCpgFactory.CreateContainment(unitNode, anchorFragment),
            DecisionCpgFactory.CreateContainment(unitNode, replacementFragment),
            DecisionCpgFactory.CreateRelation("reduced-to", anchorFragment, replacementFragment)
          },
          DecisionCpgFactory.CreateSyntaxBindings(
            (anchorFragment, anchorNode),
            (replacementFragment, replacementNode.WithoutTrivia())),
          mergeKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          conflictKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          reason: reason);
    }

    public static DecisionUnit CreateElseClauseReplaceDecision(string ruleId, ElseClauseSyntax anchorNode, ElseClauseSyntax replacementNode, string reason)
    {
        var anchorFragment = DecisionCpgFactory.CreateFragment(
          BuildFragmentId(anchorNode),
          anchorNode,
          "anchor",
          DecisionActionKind.Replace);
        var replacementFragment = DecisionCpgFactory.CreateFragment(
          BuildFragmentId(replacementNode),
          replacementNode.WithoutTrivia(),
          "replacement",
          DecisionActionKind.Replace);
        var unitNode = DecisionCpgFactory.CreateUnit(
          ruleId,
          DecisionActionKind.Replace,
          anchorFragment,
          reason: reason,
          conflictKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          mergeKey: DecisionCpgFactory.BuildNodeKey(anchorNode));

        return new DecisionUnit(
          ruleId,
          DecisionActionKind.Replace,
          unitNode,
          new[] { anchorFragment, replacementFragment },
          new[]
          {
            DecisionCpgFactory.CreateContainment(unitNode, anchorFragment),
            DecisionCpgFactory.CreateContainment(unitNode, replacementFragment),
            DecisionCpgFactory.CreateRelation("reduced-to", anchorFragment, replacementFragment)
          },
          DecisionCpgFactory.CreateSyntaxBindings(
            (anchorFragment, anchorNode),
            (replacementFragment, replacementNode.WithoutTrivia())),
          mergeKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          conflictKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          reason: reason);
    }

    public static (int Start, int Length, int RawKind) BuildNodeKey(SyntaxNode syntaxNode)
    {
        return (syntaxNode.SpanStart, syntaxNode.Span.Length, syntaxNode.RawKind);
    }

    private static string BuildFragmentId(SyntaxNode node)
    {
        return $"frag:{DecisionCpgFactory.BuildNodeKey(node)}";
    }

    public static bool TryResolveMarkedIfStructure(RuleContext context, SyntaxNode markedNode, IfStructureAnalyzer ifStructureAnalyzer, out IfStructureAnalysis? ifAnalysis)
    {
        ifAnalysis = null;
        if (markedNode is IfStatementSyntax ifStatement)
        {
            ifAnalysis = context.AnalyzeIfStructure(ifStatement);
            return true;
        }

        if (markedNode is ElseClauseSyntax elseClause &&
            elseClause.Statement is IfStatementSyntax elseIfStatement)
        {
            ifAnalysis = context.AnalyzeIfStructure(elseIfStatement);
            return true;
        }

        if (markedNode is not ExpressionSyntax expression ||
            !context.TryFindContainingIf(expression, out ifAnalysis) ||
            ifAnalysis is null)
        {
            return false;
        }

        return true;
    }

    public static IfStructureCompletionPayload? TryBuildIfStructureCompletionPayload(RuleContext context, SyntaxNode markedNode)
    {
        if (!TryResolveMarkedIfStructure(
              context,
              markedNode,
              new IfStructureAnalyzer(),
              out var ifAnalysis))
        {
            return null;
        }

        if (ifAnalysis!.TailSection is not null)
        {
            if (ifAnalysis.TailSection.Kind == IfSectionKind.ElseIf)
            {
                return new IfStructureCompletionPayload(
                  ifAnalysis.AnchorIf,
                  ifAnalysis.ParentElseClause,
                  ifAnalysis.TailSection.Node,
                  IfStructureCompletionKind.ReplaceIfWithElseIfTail);
            }

            if (ifAnalysis.AnchorVariant == IfStructureVariant.HeadIf)
            {
                return new IfStructureCompletionPayload(
                  ifAnalysis.AnchorIf,
                  ifAnalysis.ParentElseClause,
                  ifAnalysis.TailSection.Node,
                  IfStructureCompletionKind.DeleteWholeIf);
            }

            if (ifAnalysis.ParentElseClause is not null)
            {
                return new IfStructureCompletionPayload(
                  ifAnalysis.AnchorIf,
                  ifAnalysis.ParentElseClause,
                  ifAnalysis.TailSection.Node,
                  IfStructureCompletionKind.ReplaceOwningElseWithElseTail);
            }

            return new IfStructureCompletionPayload(
              ifAnalysis.AnchorIf,
              ifAnalysis.ParentElseClause,
              ifAnalysis.TailSection.Node,
              IfStructureCompletionKind.ReplaceIfWithElseTail);
        }

        if (ifAnalysis.AnchorVariant == IfStructureVariant.ElseIf &&
            ifAnalysis.ParentElseClause is not null)
        {
            return new IfStructureCompletionPayload(
              ifAnalysis.AnchorIf,
              ifAnalysis.ParentElseClause,
              null,
              IfStructureCompletionKind.DeleteOwningElseClause);
        }

        if (ifAnalysis.AnchorVariant == IfStructureVariant.HeadIf &&
            markedNode is ExpressionSyntax expression &&
            IsConditionEquivalent(expression, ifAnalysis.AnchorIf.Condition))
        {
            return new IfStructureCompletionPayload(
              ifAnalysis.AnchorIf,
              ifAnalysis.ParentElseClause,
              null,
              IfStructureCompletionKind.DeleteWholeIf);
        }

        return null;
    }

    public static SyntaxNode GetIfStructureDecisionNode(IfStructureCompletionPayload payload)
    {
        return payload.Kind switch
        {
            IfStructureCompletionKind.ReplaceOwningElseWithElseTail or
            IfStructureCompletionKind.DeleteOwningElseClause => payload.ParentElseClause is not null
              ? payload.ParentElseClause
              : payload.AnchorIf,
            _ => payload.AnchorIf
        };
    }

    private static IReadOnlyList<SyntaxNode> BuildIfStructureConsumedNodes(IfStructureCompletionPayload payload)
    {
        var nodes = new List<SyntaxNode> { payload.AnchorIf };
        if (payload.ParentElseClause is not null)
        {
            nodes.Add(payload.ParentElseClause);
        }

        if (payload.TailNode is not null)
        {
            nodes.Add(payload.TailNode);
            if (payload.TailNode is ElseClauseSyntax elseClause)
            {
                nodes.Add(elseClause.Statement);
            }
        }

        return nodes
          .DistinctBy(BuildNodeKey)
          .ToList();
    }

    private static bool IsConditionEquivalent(ExpressionSyntax expression, ExpressionSyntax condition)
    {
        var currentExpression = UnwrapParenthesizedExpression(expression);
        var currentCondition = UnwrapParenthesizedExpression(condition);
        return currentExpression.Span.Equals(currentCondition.Span);
    }

    private static ExpressionSyntax UnwrapParenthesizedExpression(ExpressionSyntax expression)
    {
        var current = expression;
        while (current is ParenthesizedExpressionSyntax parenthesizedExpression)
        {
            current = parenthesizedExpression.Expression;
        }

        return current;
    }

    private static bool IsStructuredPropagationPayload(object? payload)
    {
        return payload is MethodParameterUsagePayload or
          LogicalHostPayload or
          IfStructureCompletionPayload;
    }
}
