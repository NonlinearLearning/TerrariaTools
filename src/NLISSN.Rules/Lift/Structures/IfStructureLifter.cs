using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Analysis;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Rules;

namespace NLISSN.Rules;

/// 判断 if / else if / else 树是否已具备完整删除条件，并构造保留分支所需的结构化事实。
public static class IfStructureLiftingHelpers
{
    public static IfStructureLiftPayload? TryBuildPayload(
      ILiftRuleContext context,
      SyntaxNode markedNode,
      IReadOnlyList<MarkRecord> allMarks)
    {
        if (!TryResolveMarkedIfStructure(
              context,
              markedNode,
              new IfStructureAnalyzer(),
              out var ifAnalysis) ||
            ifAnalysis is null ||
            !MarkCoverage.IsCovered(ifAnalysis.AnchorIf.Condition, allMarks))
        {
            return null;
        }

        if (ifAnalysis.TailSection is not null)
        {
            if (ifAnalysis.TailSection.Kind == IfSectionKind.ElseIf)
            {
                return new IfStructureLiftPayload(
                  ifAnalysis.AnchorIf, ifAnalysis.ParentElseClause, ifAnalysis.TailSection.Node,
                  IfStructureLiftKind.ReplaceIfWithElseIfTail);
            }

            if (ifAnalysis.AnchorVariant == IfStructureVariant.HeadIf)
            {
                return new IfStructureLiftPayload(
                  ifAnalysis.AnchorIf, ifAnalysis.ParentElseClause, ifAnalysis.TailSection.Node,
                  IfStructureLiftKind.DeleteWholeIf);
            }

            if (ifAnalysis.ParentElseClause is not null)
            {
                return new IfStructureLiftPayload(
                  ifAnalysis.AnchorIf, ifAnalysis.ParentElseClause, ifAnalysis.TailSection.Node,
                  IfStructureLiftKind.ReplaceOwningElseWithElseTail);
            }

            return new IfStructureLiftPayload(
              ifAnalysis.AnchorIf, ifAnalysis.ParentElseClause, ifAnalysis.TailSection.Node,
              IfStructureLiftKind.ReplaceIfWithElseTail);
        }

        return ifAnalysis.AnchorVariant == IfStructureVariant.ElseIf && ifAnalysis.ParentElseClause is not null
          ? new IfStructureLiftPayload(
            ifAnalysis.AnchorIf, ifAnalysis.ParentElseClause, null,
            IfStructureLiftKind.DeleteOwningElseClause)
          : new IfStructureLiftPayload(
            ifAnalysis.AnchorIf, ifAnalysis.ParentElseClause, null,
            IfStructureLiftKind.DeleteWholeIf);
    }

    // 从已有 seed / propagated mark 推导完整的 if 结构标记，并避免重复提升同一宿主。
    public static IEnumerable<LiftedMarkRecord> BuildIfStructureLiftedMarks(ILiftRuleContext context, string ruleId, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        var ifStructureAnalyzer = new IfStructureAnalyzer();
        // 输入事实可以与结构结论共享同一语法节点；只抑制本规则重复产出的 Lift.IfStructure。
        var knownKeys = new HashSet<(int Start, int Length, int RawKind)>();

        var allMarks = seedMarks.Concat(propagatedMarks.Select(item => item.Mark)).ToList();
        foreach (var (candidateIf, sourceMark) in GetCandidateIfs(context, allMarks, propagatedMarks))
        {
            if (!MarkCoverage.IsCovered(candidateIf.Condition, allMarks))
            {
                continue;
            }

            foreach (var lifted in BuildStructuralMarksForLiftedNode(
                       context,
                       ruleId,
                       candidateIf,
                       "If structure lifting from existing mark.",
                       ifStructureAnalyzer))
            {
                var key = LiftingCommon.BuildNodeKey(lifted.SyntaxNode);
                if (!knownKeys.Add(key))
                {
                    continue;
                }

                yield return new LiftedMarkRecord(
                  ruleId,
                  lifted,
                  sourceMark,
                  1);
            }
        }
    }

    private static IEnumerable<(IfStatementSyntax Candidate, MarkRecord Source)> GetCandidateIfs(
      ILiftRuleContext context,
      IReadOnlyList<MarkRecord> allMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        var knownKeys = new HashSet<(int Start, int Length, int RawKind)>();
        foreach (var candidate in allMarks
                   .Select(mark => mark.SyntaxNode)
                   .OfType<IfStatementSyntax>())
        {
            if (knownKeys.Add(LiftingCommon.BuildNodeKey(candidate)))
            {
                yield return (candidate, allMarks.First(mark => candidate.Span.Contains(mark.SyntaxNode.Span)));
            }
        }

        // A local definition can be removed independently of the terminal expression that initialized it.
        // Its proven symbol references may therefore complete an if condition without reopening that terminal path.
        foreach (var referenceFact in propagatedMarks.Where(IsDefinitionBackedSymbolReference))
        {
            if (referenceFact.Mark.SyntaxNode is not IdentifierNameSyntax reference ||
                !context.TryFindContainingIf(reference, out var analysis) ||
                analysis is null ||
                !knownKeys.Add(LiftingCommon.BuildNodeKey(analysis.AnchorIf)))
            {
                continue;
            }

            yield return (analysis.AnchorIf, referenceFact.Mark);
        }
    }

    private static bool IsDefinitionBackedSymbolReference(PropagatedMarkRecord fact)
    {
        return fact.Mark.FactKind == RuleFactKind.FlowSymbolReference &&
          fact.Mark.SyntaxNode is IdentifierNameSyntax &&
          fact.SourceMark.FactKind == RuleFactKind.FlowLocalDefinition &&
          fact.SourceMark.SyntaxNode is VariableDeclaratorSyntax &&
          fact.SourceMark.OutputKind == RuleOutputKind.LocalDefinitionFromInitializer;
    }

    private static IReadOnlyList<MarkRecord> BuildStructuralMarksForLiftedNode(ILiftRuleContext context, string ruleId, SyntaxNode markedNode, string reason, IfStructureAnalyzer ifStructureAnalyzer)
    {
        if (!TryResolveMarkedIfStructure(
              context,
              markedNode,
              ifStructureAnalyzer,
              out var ifAnalysis))
        {
            return new[]
            {
              MarkRecordFactory.Create(ruleId, markedNode, reason)
            };
        }

        var analysis = ifAnalysis!;
        if (markedNode is not IfStatementSyntax &&
            analysis.TailSection is null &&
            analysis.AnchorVariant != IfStructureVariant.ElseIf)
        {
            return new[]
            {
              MarkRecordFactory.Create(ruleId, markedNode, reason)
            };
        }

        var marks = new List<MarkRecord>
        {
          MarkRecordFactory.Create(ruleId, analysis.AnchorIf, reason)
        };
        if (analysis.TailSection is not null &&
            ShouldMarkIfTail(analysis))
        {
            marks.Add(MarkRecordFactory.Create(
              ruleId,
              analysis.TailSection.Node,
              BuildIfTailReason(analysis.TailSection.Kind)));

            if (analysis.TailSection.Kind == IfSectionKind.Else)
            {
                marks.Add(MarkRecordFactory.Create(
                  ruleId,
                  analysis.TailSection.Statement,
                  "Else branch body is part of the remaining else section and must be marked together."));
            }
        }
        else if (analysis.TailSection is null &&
                 analysis.AnchorVariant == IfStructureVariant.ElseIf &&
                 analysis.ParentElseClause is not null)
        {
            marks.Add(MarkRecordFactory.Create(
              ruleId,
              analysis.ParentElseClause,
              "Else-if section is fully marked and has no remaining tail; remove owning else clause."));
        }

        return marks;
    }

    private static bool TryResolveMarkedIfStructure(ILiftRuleContext context, SyntaxNode markedNode, IfStructureAnalyzer ifStructureAnalyzer, out IfStructureAnalysis? ifAnalysis)
    {
        ifAnalysis = null;
        if (markedNode is IfStatementSyntax ifStatement)
        {
            ifAnalysis = context.AnalyzeIfStructure(ifStatement);
            return true;
        }

        if (markedNode is not ExpressionSyntax expression ||
            !context.TryFindContainingIf(expression, out ifAnalysis) ||
            ifAnalysis is null)
        {
            return false;
        }

        return IsConditionEquivalent(expression, ifAnalysis.AnchorIf.Condition);
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

    private static string BuildIfTailReason(IfSectionKind sectionKind)
    {
        return sectionKind switch
        {
            IfSectionKind.ElseIf => "If section is fully marked; remaining elseif branch becomes the replacement target.",
            IfSectionKind.Else => "If section is fully marked; remaining else branch becomes the replacement target.",
            _ => "If structure tail is marked."
        };
    }

    private static bool ShouldMarkIfTail(IfStructureAnalysis ifAnalysis)
    {
        if (ifAnalysis.TailSection is null)
        {
            return false;
        }

        if (ifAnalysis.TailSection.Kind != IfSectionKind.Else)
        {
            return true;
        }

        return ifAnalysis.AnchorVariant == IfStructureVariant.HeadIf;
    }
}
