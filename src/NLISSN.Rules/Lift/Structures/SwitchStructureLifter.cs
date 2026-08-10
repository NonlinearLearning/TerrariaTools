using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Analysis;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Rules;

namespace NLISSN.Rules;

/// 判断 switch 分支是否可整体规约，避免在 case 标签和控制流边界不完整时生成改写。
public static class SwitchStructureLiftingHelpers
{
    // 基于已有 provisional mark 判断哪些 switch section / statement 已可整体规约。
    public static IEnumerable<LiftedMarkRecord> BuildSwitchLiftedMarks(ILiftRuleContext context, string ruleId, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> existingLiftedMarks)
    {
        var provisionalMarks = seedMarks
          .Concat(propagatedMarks.Select(mark => mark.Mark))
          .Concat(existingLiftedMarks.Select(mark => mark.Mark))
          .ToList();
        var producedKeys = existingLiftedMarks
          .Where(mark => mark.StructureKind == StructuralKind.Switch)
          .Select(mark => LiftingCommon.BuildNodeKey(mark.Mark.SyntaxNode))
          .ToHashSet();

        foreach (var switchMark in BuildSwitchMarks(ruleId, provisionalMarks, FindTopologySwitchOwners(context, seedMarks, propagatedMarks, existingLiftedMarks)))
        {
            var key = LiftingCommon.BuildNodeKey(switchMark.SyntaxNode);
            if (!producedKeys.Add(key))
            {
                continue;
            }

            yield return new LiftedMarkRecord(
              ruleId,
              switchMark,
              FindSourceMarkForAncestor(seedMarks, existingLiftedMarks, switchMark.SyntaxNode),
              1,
              StructureKind: StructuralKind.Switch);
        }
    }

    private static IReadOnlyList<MarkRecord> BuildSwitchMarks(string ruleId, IReadOnlyList<MarkRecord> provisionalMarks, IReadOnlyList<SyntaxNode> topologyOwners)
    {
        var marks = new List<MarkRecord>();
        var markKeys = provisionalMarks
          .Where(mark => mark.SyntaxNode is not SwitchSectionSyntax and not SwitchStatementSyntax)
          .Select(mark => LiftingCommon.BuildNodeKey(mark.SyntaxNode))
          .ToHashSet();
        var candidateSections = topologyOwners
          .OfType<SwitchSectionSyntax>()
          .DistinctBy(LiftingCommon.BuildNodeKey)
          .ToList();

        foreach (var section in candidateSections)
        {
            if (!IsSwitchSectionFullyMarked(section, markKeys))
            {
                continue;
            }

            marks.Add(MarkRecordFactory.Create(
              ruleId,
              section,
              "All executable statements in switch case are marked; mark whole switch section."));
        }

        var candidateSwitches = topologyOwners
          .OfType<SwitchStatementSyntax>()
          .DistinctBy(LiftingCommon.BuildNodeKey)
          .ToList();
        foreach (var switchStatement in candidateSwitches)
        {
            if (AllNonDefaultSectionsMarked(switchStatement, markKeys, marks))
            {
                marks.Add(MarkRecordFactory.Create(
                  ruleId,
                  switchStatement,
                  "All non-default switch sections are marked; mark whole switch statement."));
            }
        }

        return marks;
    }

    private static IReadOnlyList<SyntaxNode> FindTopologySwitchOwners(
      ILiftRuleContext context,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
      IReadOnlyList<LiftedMarkRecord> existingLiftedMarks)
    {
        return seedMarks
          .Concat(propagatedMarks.Select(mark => mark.Mark))
          .Concat(existingLiftedMarks.Select(mark => mark.SourceMark))
          .Select(mark => mark.SyntaxNode)
          .OfType<ExpressionSyntax>()
          .SelectMany(expression => context.ResolveExpressionTopology(expression).StructuralOwners)
          .Where(node => node is SwitchSectionSyntax or SwitchStatementSyntax)
          .DistinctBy(LiftingCommon.BuildNodeKey)
          .ToList();
    }

    private static MarkRecord FindSourceMarkForAncestor(
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<LiftedMarkRecord> existingLiftedMarks,
      SyntaxNode ancestor)
    {
        var seed = seedMarks.FirstOrDefault(mark => ancestor.Span.Contains(mark.SyntaxNode.Span));
        if (seed is not null)
        {
            return seed;
        }

        var inherited = existingLiftedMarks
          .Select(mark => mark.SourceMark)
          .FirstOrDefault(mark => ancestor.Span.Contains(mark.SyntaxNode.Span));
        if (inherited is not null)
        {
            return inherited;
        }

        throw new InvalidOperationException("Switch lift requires a source mark from a declared host or if lift producer.");
    }

    private static bool AllNonDefaultSectionsMarked(SwitchStatementSyntax switchStatement, IReadOnlySet<(int Start, int Length, int RawKind)> markKeys, IReadOnlyList<MarkRecord> synthesizedMarks)
    {
        var markedSectionKeys = synthesizedMarks
          .Where(mark => mark.SyntaxNode is SwitchSectionSyntax)
          .Select(mark => LiftingCommon.BuildNodeKey(mark.SyntaxNode))
          .ToHashSet();
        foreach (var section in switchStatement.Sections)
        {
            if (IsDefaultOnlySection(section))
            {
                continue;
            }

            var sectionKey = LiftingCommon.BuildNodeKey(section);
            if (!markedSectionKeys.Contains(sectionKey) &&
                !markKeys.Contains(sectionKey))
            {
                return false;
            }
        }

        return switchStatement.Sections.Any(section => !IsDefaultOnlySection(section));
    }

    private static bool IsSwitchSectionFullyMarked(SwitchSectionSyntax section, IReadOnlySet<(int Start, int Length, int RawKind)> markKeys)
    {
        var executableStatements = EnumerateExecutableCaseStatements(section).ToList();
        if (executableStatements.Count == 0)
        {
            return false;
        }

        return executableStatements.All(statement =>
          markKeys.Contains(LiftingCommon.BuildNodeKey(statement)));
    }

    private static IEnumerable<StatementSyntax> EnumerateExecutableCaseStatements(SwitchSectionSyntax section)
    {
        foreach (var statement in section.Statements)
        {
            foreach (var nested in EnumerateExecutableStatements(statement))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<StatementSyntax> EnumerateExecutableStatements(StatementSyntax statement)
    {
        if (statement is BreakStatementSyntax)
        {
            yield break;
        }

        if (statement is BlockSyntax block)
        {
            foreach (var nested in block.Statements)
            {
                foreach (var item in EnumerateExecutableStatements(nested))
                {
                    yield return item;
                }
            }

            yield break;
        }

        yield return statement;
    }

    private static bool IsDefaultOnlySection(SwitchSectionSyntax section)
    {
        return section.Labels.All(label => label is DefaultSwitchLabelSyntax);
    }
}
