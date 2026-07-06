using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;
using RoslynPrototype.Propagation;
using Rules;

namespace RoslynPrototype.Marking;

public sealed class MarkingEngine
{
    /// <summary>
    /// 执行所有启用规则的标记逻辑，并为每条命中补齐注解与主图节点绑定。
    /// </summary>
    /// <param name="context">当前规则执行所需的分析上下文。</param>
    /// <param name="root">待分析源码的语法树根节点。</param>
    /// <param name="rules">参与当前分析的删除规则集合。</param>
    /// <returns>去重后的种子标记集合。</returns>
    public IReadOnlyList<MarkRecord> Run(
      RuleContext context,
      SyntaxNode root,
      IReadOnlyList<RuleDefinitionMark> rules)
    {
        var seedMarks = new List<MarkRecord>();

        foreach (var rule in rules)
        {
            foreach (var mark in rule.Mark(context, root))
            {
                ValidateMarkNode(rule, mark.SyntaxNode);
                seedMarks.Add(BindMarkRecord(context, mark, rule.GroupKey));
            }
        }

        // 同一规则可能通过多条路径命中同一个语法节点，这里按规则和语法位置去重。
        return seedMarks
        .DistinctBy(mark => (GetGroupKey(mark), mark.SyntaxNode.SpanStart, mark.SyntaxNode.Span.Length))
        .ToList();
    }

    /// <summary>
    /// 校验标记阶段产出的命中节点是否落在规则声明允许的语法种类范围内。
    /// </summary>
    internal static void ValidateMarkNode(RuleDefinitionMark rule, SyntaxNode syntaxNode)
    {
        var nodeKind = (SyntaxKind)syntaxNode.RawKind;
        if (rule.AllowedMarkNodeKinds.Contains(nodeKind))
        {
            return;
        }

        var allowedKinds = string.Join(", ", rule.AllowedMarkNodeKinds);
        throw new InvalidOperationException(
          $"Rule '{rule.RuleId}' emitted unsupported mark node kind '{nodeKind}'. Allowed mark node kinds: {allowedKinds}.");
    }

    /// <summary>
    /// 校验传播阶段产出的命中节点是否落在规则声明允许的语法种类范围内。
    /// </summary>
    internal static void ValidatePropagateNode(RuleDefinitionPropagate rule, SyntaxNode syntaxNode)
    {
        var nodeKind = (SyntaxKind)syntaxNode.RawKind;
        if (rule.AllowedPropagateNodeKinds.Contains(nodeKind))
        {
            return;
        }

        var allowedKinds = string.Join(", ", rule.AllowedPropagateNodeKinds);
        throw new InvalidOperationException(
          $"Rule '{rule.RuleId}' emitted unsupported propagate node kind '{nodeKind}'. Allowed propagate node kinds: {allowedKinds}.");
    }

    /// <summary>
    /// 为命中记录补齐缺失的语法注解与主图节点绑定。
    /// </summary>
    internal static MarkRecord BindMarkRecord(
      RuleContext context,
      MarkRecord candidate,
      string? groupKey = null)
    {
        var annotation = candidate.Annotation ?? new SyntaxAnnotation("RuleHitNode", Guid.NewGuid().ToString("N"));
        var primaryGraphNode = candidate.PrimaryGraphNode ?? ResolvePrimaryGraphNode(context.Graph, candidate.SyntaxNode);
        if (primaryGraphNode is null)
        {
            throw new InvalidOperationException(
              $"Could not bind syntax node '{candidate.SyntaxNode.Kind()}' to a graph node.");
        }

        return candidate with
        {
            Annotation = annotation,
            PrimaryGraphNode = primaryGraphNode,
            GroupKey = candidate.GroupKey ?? groupKey
        };
    }

    /// <summary>
    /// 为传播记录中的命中标记和源标记分别补齐绑定信息。
    /// </summary>
    internal static PropagatedMarkRecord BindPropagatedMarkRecord(
      RuleContext context,
      PropagatedMarkRecord candidate,
      string? groupKey = null)
    {
        return candidate with
        {
            Mark = BindMarkRecord(context, candidate.Mark, groupKey),
            SourceMark = BindMarkRecord(context, candidate.SourceMark, groupKey),
            GroupKey = candidate.GroupKey ?? groupKey
        };
    }

    internal static string GetGroupKey(MarkRecord mark)
    {
        return mark.GroupKey ?? mark.RuleId;
    }

    internal static string GetGroupKey(PropagatedMarkRecord propagatedMark)
    {
        return propagatedMark.GroupKey ??
          propagatedMark.Mark.GroupKey ??
          propagatedMark.SourceMark.GroupKey ??
          propagatedMark.RuleId;
    }

    /// <summary>
    /// 根据文件路径与跨度，从图中找出最适合作为主绑定的节点。
    /// </summary>
    private static RoslynCpgNode? ResolvePrimaryGraphNode(RoslynCpgGraph graph, SyntaxNode syntaxNode)
    {
        var filePath = syntaxNode.SyntaxTree.FilePath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        return graph.Nodes
          .Where(node =>
            !node.IsImplicit &&
            string.Equals(node.FilePath, filePath, StringComparison.Ordinal) &&
            node.SpanStart == syntaxNode.SpanStart &&
            node.SpanEnd == syntaxNode.Span.End)
          .OrderBy(GetBindingPriority)
          .FirstOrDefault();
    }

    /// <summary>
    /// 当同一语法跨度对应多个图节点时，定义主绑定节点的优先级。
    /// </summary>
    private static int GetBindingPriority(RoslynCpgNode node)
    {
        return node.Kind switch
        {
            RoslynCpgNodeKind.Method => 0,
            RoslynCpgNodeKind.MethodParameter => 1,
            RoslynCpgNodeKind.CallSite => 2,
            RoslynCpgNodeKind.MemberAccess => 3,
            RoslynCpgNodeKind.Reference => 4,
            RoslynCpgNodeKind.Operation => 5,
            RoslynCpgNodeKind.OpInvocation => 6,
            RoslynCpgNodeKind.OpBinary => 7,
            RoslynCpgNodeKind.OpAssignment => 8,
            RoslynCpgNodeKind.OpLocalReference => 9,
            RoslynCpgNodeKind.OpParameterReference => 10,
            RoslynCpgNodeKind.OpFieldReference => 11,
            RoslynCpgNodeKind.OpPropertyReference => 12,
            RoslynCpgNodeKind.SyntaxNode => 13,
            _ => 14
        };
    }
}
