using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Analysis;
using RoslynPrototype.Decision;
using RoslynPrototype.Marking;
using Rules;

namespace RoslynPrototype.Propagation;

public static class DeleteSObjectPropagationHelpers
{
    public static IReadOnlyList<string> ParseTargetNames(RuleContext context)
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

    public static LogicalHostPayload? TryBuildLogicalHostPayload(RuleContext context, BinaryExpressionSyntax host, IEnumerable<SyntaxNode> sourceNodes)
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

        var operands = context.AnalyzeBinaryExpression(host, removableNodes[0])
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

    public static IEnumerable<PropagatedMarkRecord> EnumerateIfStructureCompletionPropagations(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, string ruleId)
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
              MarkRecordFactory.Create(
                ruleId,
                decisionNode,
                BuildIfStructureCompletionReason(payload.Kind)),
              seedMark,
              1,
              Payload: payload);
        }
    }

    private static bool ShouldRemoveOperand(ExpressionSyntax operand, IReadOnlyList<ExpressionSyntax> sourceNodes)
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
