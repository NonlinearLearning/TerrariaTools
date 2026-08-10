using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Analysis;
using NLISSN.Core.Analysis.ExpressionPropagation;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Rules;

namespace NLISSN.Rules;

/// 从已验证的表达式标记向上寻找最小可改写宿主，并在宿主语义不明确时停止提升。
public static class ExpressionHostLiftingHelpers
{
    // 表达式宿主只能来自传播阶段已解析的 topology fact；声明和语句节点保留各自的直接提升规则。
    public static IEnumerable<LiftedMarkRecord> BuildHostLiftedMarks(ILiftRuleContext context, string ruleId, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        var liftedMarks = new List<LiftedMarkRecord>();
        var knownKeys = seedMarks
          .Select(mark => LiftingCommon.BuildNodeKey(mark.SyntaxNode))
          .Concat(propagatedMarks.Select(mark =>
            LiftingCommon.BuildNodeKey(mark.Mark.SyntaxNode)))
          .ToHashSet();
        var emittedExpressionKeys = new HashSet<(int Start, int Length, int RawKind)>();

        foreach (var propagatedMark in propagatedMarks)
        {
            if (!TryGetTopologyExpressionHost(propagatedMark, out var expressionHost))
            {
                continue;
            }

            if (!LiftingCommon.AllowedLiftNodeKinds.Contains(expressionHost.Kind()))
            {
                continue;
            }

            var key = LiftingCommon.BuildNodeKey(expressionHost);
            if (!emittedExpressionKeys.Add(key))
            {
                continue;
            }

            liftedMarks.Add(new LiftedMarkRecord(
              ruleId,
              MarkRecordFactory.Create(
                ruleId,
                expressionHost,
                $"Lifted topology host {expressionHost.Kind()} from its propagation fact."),
              propagatedMark.SourceMark,
              propagatedMark.Depth + 1));
        }

        var worklist = seedMarks
          .Select(mark => (Current: mark, Source: mark, Depth: 0))
          .Concat(propagatedMarks.Select(mark =>
            (Current: mark.Mark, Source: mark.SourceMark, Depth: mark.Depth)))
          .ToList();

        for (var index = 0; index < worklist.Count; index++)
        {
            var item = worklist[index];
            if (LiftingCommon.IsSymbolReferencePropagation(item.Current))
            {
                continue;
            }

            if (item.Current.SyntaxNode is ExpressionSyntax expression)
            {
                foreach (var owner in GetStructuralOwners(expression, context))
                {
                    AddLiftedOwner(owner, item, ruleId, knownKeys, liftedMarks);
                }

                continue;
            }

            var liftedNode = TryLiftNonExpressionNode(item.Current.SyntaxNode);
            if (liftedNode is null)
            {
                continue;
            }

            var key = LiftingCommon.BuildNodeKey(liftedNode);
            if (!knownKeys.Add(key))
            {
                continue;
            }

            var liftedMark = new LiftedMarkRecord(
              ruleId,
              MarkRecordFactory.Create(
                ruleId,
                liftedNode,
                $"Lifted mark from {item.Current.SyntaxNode.Kind()} to {liftedNode.Kind()}."),
              item.Source,
              item.Depth + 1);
            liftedMarks.Add(liftedMark);
            worklist.Add((liftedMark.Mark, liftedMark.SourceMark, liftedMark.Depth));
        }

        return liftedMarks;
    }

    private static void AddLiftedOwner(
      SyntaxNode liftedNode,
      (MarkRecord Current, MarkRecord Source, int Depth) item,
      string ruleId,
      ISet<(int Start, int Length, int RawKind)> knownKeys,
      ICollection<LiftedMarkRecord> liftedMarks)
    {
        var key = LiftingCommon.BuildNodeKey(liftedNode);
        if (!knownKeys.Add(key))
        {
            return;
        }

        liftedMarks.Add(new LiftedMarkRecord(
          ruleId,
          MarkRecordFactory.Create(
            ruleId,
            liftedNode,
            $"Lifted topology owner {liftedNode.Kind()} from {item.Current.SyntaxNode.Kind()}."),
          item.Source,
          item.Depth + 1));
    }

    private static SyntaxNode? TryLiftNonExpressionNode(SyntaxNode markedNode)
    {
        switch (markedNode)
        {
            case VariableDeclaratorSyntax variableDeclarator:
                return TryLiftVariableDeclarator(variableDeclarator);
            case StatementSyntax statement:
                return TryLiftStatement(statement);
            case ArgumentSyntax argument:
                return argument.Parent;
            case ArgumentListSyntax argumentList:
                return argumentList.Parent;
            case BracketedArgumentListSyntax bracketedArgumentList:
                return bracketedArgumentList.Parent;
            case InterpolationSyntax interpolation:
                return interpolation.Parent;
            case SwitchExpressionArmSyntax switchArm:
                return switchArm.Parent;
        }

        return null;
    }

    private static IEnumerable<SyntaxNode> GetStructuralOwners(
      ExpressionSyntax expression,
      ILiftRuleContext context)
    {
        var path = context.ResolveExpressionTopology(expression);
        return path.StructuralOwners.Where(candidate =>
          candidate is not BlockSyntax &&
          candidate is not SwitchSectionSyntax &&
          (candidate is not SwitchStatementSyntax switchStatement ||
            IsWithinSwitchGoverningCondition(expression, switchStatement)) &&
          LiftingCommon.AllowedLiftNodeKinds.Contains((SyntaxKind)candidate.RawKind));
    }

    private static bool IsWithinSwitchGoverningCondition(
      ExpressionSyntax expression,
      SwitchStatementSyntax switchStatement)
    {
        return switchStatement.Expression.DescendantNodesAndSelf()
          .Any(node => ReferenceEquals(node, expression));
    }

    private static bool TryGetTopologyExpressionHost(
      PropagatedMarkRecord propagatedMark,
      out ExpressionSyntax expressionHost)
    {
        expressionHost = null!;
        if (propagatedMark.Mark.SyntaxNode is not ExpressionSyntax expression ||
            propagatedMark.Payload is not ExpressionTopologyPayload payload ||
            !payload.CanContinueOutward ||
            !ReferenceEquals(payload.Step.Host, expression))
        {
            return false;
        }

        expressionHost = expression;
        return true;
    }

    private static SyntaxNode? TryLiftVariableDeclarator(VariableDeclaratorSyntax variableDeclarator)
    {
        if (variableDeclarator.Parent?.Parent is LocalDeclarationStatementSyntax localDeclarationStatement)
        {
            return localDeclarationStatement;
        }

        if (variableDeclarator.Parent is VariableDeclarationSyntax variableDeclaration &&
            variableDeclaration.Parent is ForStatementSyntax forStatement)
        {
            return forStatement;
        }

        return null;
    }

    private static SyntaxNode? TryLiftStatement(StatementSyntax statement)
    {
        if (statement.Parent is BlockSyntax block &&
            block.Statements.Count == 1)
        {
            if (block.Parent is WhileStatementSyntax whileStatement &&
                ReferenceEquals(whileStatement.Statement, block))
            {
                return whileStatement;
            }

            if (block.Parent is DoStatementSyntax doStatement &&
                ReferenceEquals(doStatement.Statement, block))
            {
                return doStatement;
            }
        }

        return null;
    }

}
