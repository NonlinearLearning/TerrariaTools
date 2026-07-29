using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Analysis;
using NLISSN.Core.Decision;
using NLISSN.Core.Marking;
using NLISSN.Rules;

namespace NLISSN.Core.Propagation;

/// 集中封装传播阶段复用的结构化事实构造，避免各条规则各自重扫逻辑宿主和 if 结构。
public static class DeleteSObjectPropagationHelpers
{
    // 解析 target-name 选项中的目标名称列表，供多条传播规则复用同一匹配基准。
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

    // 为逻辑与/或宿主拆分可删与保留操作数，只有两边都明确时才产出结构化 payload。
    public static LogicalHostPayload? TryBuildLogicalHostPayload(RuleContext context, BinaryExpressionSyntax host, IEnumerable<SyntaxNode> sourceNodes)
    {
        // 先把所有命中压回当前逻辑宿主的直接操作数，再判断删掉哪些操作数后仍有剩余表达式。
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

    // 只在 if 结构已经形成完整删除或替换形态时，传播单一结构 payload 给提案阶段。
    public static IEnumerable<PropagatedMarkRecord> EnumerateIfStructureCompletionPropagations(
      RuleContext context,
      IReadOnlyList<MarkRecord> seedMarks,
      string ruleId,
      RuleSemanticTag? semanticTag = null)
    {
        // 传播阶段只负责产出“结构已完整命中”的 payload；
        // 真正删除整段 if 还是折叠到 tail，由 Propose 阶段统一裁决。
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
                BuildIfStructureCompletionReason(payload.Kind),
                semanticTag: semanticTag),
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
