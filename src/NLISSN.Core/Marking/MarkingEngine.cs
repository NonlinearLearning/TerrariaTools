using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Pipeline;

namespace NLISSN.Core.Marking;

public sealed class MarkingEngine
{
    // 执行所有标记规则，补齐图绑定后按规则节点和语法位置去重返回种子标记。
    public IReadOnlyList<MarkRecord> Run(RuleContext context, SyntaxNode root, IReadOnlyList<RuleDefinitionMark> rules)
    {
        var nodes = rules.Select(rule => new RuleGraphNode(
          RuleNodeId.For(RuleKind.Mark, rule.RuleId),
          RuleKind.Mark,
          Array.Empty<RuleDependency>())
        {
          ProducedSyntax = rule.Produces.Outputs,
          ConsumedSyntax = rule.Consumes.Inputs
        }).ToList();
        var graph = new RuleGraphCompiler().Compile(nodes);
        var executionNodes = rules.Select(rule =>
        {
            var node = graph.Nodes.Single(candidate => candidate.NodeId == RuleNodeId.For(RuleKind.Mark, rule.RuleId));
            return new RuleGraphExecutionNode(
              node,
              (_, _) => Task.FromResult(CreateResult(
                rule.Produces,
                ExecuteRule(context, root, rule))));
        }).ToList();
        var graphDegree = context.Runtime.ExecutionOptions.EnableGroupParallelism
          ? context.Runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism
          : 1;
        var execution = new RuleGraphExecutor(context.Runtime.ConcurrencyPool).ExecuteAsync(
            graph,
            executionNodes,
            graphDegree,
            context.Runtime.ExecutionOptions.CancellationToken)
          .GetAwaiter()
          .GetResult();
        var seedMarks = execution.Nodes
          .SelectMany(node => node.Result.Values)
          .OfType<MarkRecord>()
          .ToList();

        // 同一规则可能通过多条路径命中同一个语法节点，这里按规则和语法位置去重。
        return seedMarks
        .DistinctBy(mark => (
          mark.RuleId,
          mark.SyntaxNode.SpanStart,
          mark.SyntaxNode.Span.Length))
        .ToList();
    }

    public static List<MarkRecord> ExecuteRule(RuleContext context, SyntaxNode root, RuleDefinitionMark rule)
    {
        var producedMarks = new List<MarkRecord>();
        foreach (var mark in rule.Mark(context, root))
        {
            var taggedMark = BindDeclaredSemanticTag(rule.Produces, mark);
            ValidateMarkNode(rule, taggedMark.SyntaxNode);
              ValidateProducedSyntax(rule.Produces, taggedMark);
            producedMarks.Add(BindMarkRecord(context, taggedMark));
        }

        foreach (var mark in producedMarks)
        {
            context.Evidence.RecordSeed(mark);
        }

        return producedMarks;
    }

    private static RuleNodeResult CreateResult<T>(
      RuleProducesContract produces,
      IReadOnlyList<T> values)
    {
        var boxed = values.Cast<object>().ToList();
        return RuleNodeResult.FromValues(boxed, produces);
    }

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

    internal static void ValidateProducedSyntax(RuleProducesContract produces, MarkRecord mark)
    {
        if (produces.Outputs.Count > 0 && mark.SemanticTag is not null)
        {
            RuleSyntaxContractValidator.RequireProducedMark(produces, mark);
        }
    }

    internal static MarkRecord BindDeclaredSemanticTag(
      RuleProducesContract produces,
      MarkRecord mark)
    {
        ArgumentNullException.ThrowIfNull(produces);
        ArgumentNullException.ThrowIfNull(mark);

        if (mark.SemanticTag is not null)
        {
            return mark;
        }

        var matches = produces.Outputs
          .Where(output => output.SyntaxKinds.Contains((SyntaxKind)mark.SyntaxNode.RawKind))
          .ToList();
        return matches.Count == 1
          ? mark with { SemanticTag = matches[0].SemanticTag }
          : mark;
    }

    internal static MarkRecord BindMarkRecord(RuleContext context, MarkRecord candidate)
    {
        var annotation = candidate.Annotation ?? new SyntaxAnnotation("RuleHitNode", Guid.NewGuid().ToString("N"));
        var primaryGraphNode = candidate.PrimaryGraphNode;
        if (primaryGraphNode is null)
        {
            context.GraphBinding.TryResolvePrimaryGraphNode(candidate.SyntaxNode, out primaryGraphNode);
        }

        if (primaryGraphNode is null)
        {
            throw new InvalidOperationException(
              $"Could not bind syntax node '{candidate.SyntaxNode.Kind()}' to a graph node.");
        }

        return candidate with
        {
            Annotation = annotation,
            PrimaryGraphNode = primaryGraphNode
        };
    }
}
