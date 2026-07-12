using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Analysis;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;
using Rules;

namespace RoslynPrototype.Lifting;

public static class DeleteSObjectIfStructureLiftingHelpers
{
    public static IEnumerable<LiftedMarkRecord> BuildIfStructureLiftedMarks(RuleContext context, string ruleId, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        var ifStructureAnalyzer = new IfStructureAnalyzer();
        var knownKeys = seedMarks
          .Select(mark => DeleteSObjectLiftingCommon.BuildNodeKey(mark.SyntaxNode))
          .Concat(propagatedMarks.Select(mark =>
            DeleteSObjectLiftingCommon.BuildNodeKey(mark.Mark.SyntaxNode)))
          .ToHashSet();

        foreach (var mark in seedMarks.Concat(propagatedMarks.Select(item => item.Mark)))
        {
            foreach (var lifted in BuildStructuralMarksForLiftedNode(
                       context,
                       ruleId,
                       mark.SyntaxNode,
                       "If structure lifting from existing mark.",
                       ifStructureAnalyzer))
            {
                var key = DeleteSObjectLiftingCommon.BuildNodeKey(lifted.SyntaxNode);
                if (!knownKeys.Add(key))
                {
                    continue;
                }

                yield return new LiftedMarkRecord(
                  ruleId,
                  lifted,
                  mark,
                  1);
            }
        }
    }

    private static IReadOnlyList<MarkRecord> BuildStructuralMarksForLiftedNode(RuleContext context, string ruleId, SyntaxNode markedNode, string reason, IfStructureAnalyzer ifStructureAnalyzer)
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

    private static bool TryResolveMarkedIfStructure(RuleContext context, SyntaxNode markedNode, IfStructureAnalyzer ifStructureAnalyzer, out IfStructureAnalysis? ifAnalysis)
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
