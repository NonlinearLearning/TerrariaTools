using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;
using Rules;

namespace RoslynPrototype.Lifting;

public static class DeleteSObjectSwitchLiftingHelpers
{
    public static IEnumerable<LiftedMarkRecord> BuildSwitchLiftedMarks(string ruleId, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> existingLiftedMarks)
    {
        var provisionalMarks = seedMarks
          .Concat(propagatedMarks.Select(mark => mark.Mark))
          .Concat(existingLiftedMarks.Select(mark => mark.Mark))
          .ToList();
        var producedKeys = provisionalMarks
          .Select(mark => DeleteSObjectLiftingCommon.BuildNodeKey(mark.SyntaxNode))
          .ToHashSet();

        foreach (var switchMark in BuildSwitchMarks(ruleId, provisionalMarks))
        {
            var key = DeleteSObjectLiftingCommon.BuildNodeKey(switchMark.SyntaxNode);
            if (!producedKeys.Add(key))
            {
                continue;
            }

            yield return new LiftedMarkRecord(
              ruleId,
              switchMark,
              FindSourceMarkForAncestor(seedMarks, switchMark.SyntaxNode),
              1);
        }
    }

    private static IReadOnlyList<MarkRecord> BuildSwitchMarks(string ruleId, IReadOnlyList<MarkRecord> provisionalMarks)
    {
        var marks = new List<MarkRecord>();
        var markKeys = provisionalMarks
          .Select(mark => DeleteSObjectLiftingCommon.BuildNodeKey(mark.SyntaxNode))
          .ToHashSet();
        var candidateSections = provisionalMarks
          .SelectMany(mark => mark.SyntaxNode.AncestorsAndSelf().OfType<SwitchSectionSyntax>())
          .DistinctBy(DeleteSObjectLiftingCommon.BuildNodeKey)
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

        var candidateSwitches = provisionalMarks
          .SelectMany(mark => mark.SyntaxNode.AncestorsAndSelf().OfType<SwitchStatementSyntax>())
          .DistinctBy(DeleteSObjectLiftingCommon.BuildNodeKey)
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

    private static MarkRecord FindSourceMarkForAncestor(IReadOnlyList<MarkRecord> seedMarks, SyntaxNode ancestor)
    {
        return seedMarks.FirstOrDefault(mark => ancestor.Span.Contains(mark.SyntaxNode.Span)) ??
          seedMarks[0];
    }

    private static bool AllNonDefaultSectionsMarked(SwitchStatementSyntax switchStatement, IReadOnlySet<(int Start, int Length, int RawKind)> markKeys, IReadOnlyList<MarkRecord> synthesizedMarks)
    {
        var markedSectionKeys = synthesizedMarks
          .Where(mark => mark.SyntaxNode is SwitchSectionSyntax)
          .Select(mark => DeleteSObjectLiftingCommon.BuildNodeKey(mark.SyntaxNode))
          .ToHashSet();
        foreach (var section in switchStatement.Sections)
        {
            if (IsDefaultOnlySection(section))
            {
                continue;
            }

            var sectionKey = DeleteSObjectLiftingCommon.BuildNodeKey(section);
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
          markKeys.Contains(DeleteSObjectLiftingCommon.BuildNodeKey(statement)));
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
