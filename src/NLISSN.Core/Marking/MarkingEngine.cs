using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NL.Concurrency;
using NLISSN.Core.Lifting;
using NLISSN.Core.Pipeline;

namespace NLISSN.Core.Marking;

public sealed class MarkingEngine
{
    // 执行所有标记规则，补齐图绑定后按规则节点和语法位置去重返回种子标记。
    internal IReadOnlyList<MarkRecord> Run(AnalysisSession session, SyntaxNode root, IReadOnlyList<RuleDefinitionMark> rules)
    {
        var nodes = rules.Select(rule => new RuleGraphNode(
          RuleNodeId.For(RuleKind.Mark, rule.RuleId),
          RuleKind.Mark,
          Array.Empty<RuleDependency>())
        {
          ProducedSyntax = rule.Produces.Outputs
        }).ToList();
        var graph = new RuleGraphCompiler().Compile(nodes);
        var executionNodes = rules.Select(rule =>
        {
            var node = graph.Nodes.Single(candidate => candidate.NodeId == RuleNodeId.For(RuleKind.Mark, rule.RuleId));
            return new RuleGraphExecutionNode(
              node,
              (_, _) => Task.FromResult(CreateResult(
                rule.Produces,
                ExecuteRule(session, root, rule))));
        }).ToList();
        var graphDegree = ConcurrencyExecutionPolicy.ResolveMaxDegreeOfParallelism(
          session.Runtime.ExecutionOptions.EnableGroupParallelism,
          session.Runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism);
        var execution = new RuleGraphExecutor(session.Runtime.ConcurrencyPool).ExecuteAsync(
            graph,
            executionNodes,
            graphDegree,
            session.Runtime.ExecutionOptions.CancellationToken)
          .GetAwaiter()
          .GetResult();
        var seedMarks = execution.Nodes
          .SelectMany(node => node.Result.Values)
          .OfType<MarkRecord>()
          .ToList();

        // 同一规则可能通过多条路径命中同一个语法节点；只合并真正相同的
        // fact identity，不能让不同 FactKind 或 provenance 静默互相覆盖。
        return seedMarks
        .DistinctBy(mark => new
        {
            mark.RuleId,
            Identity = FactIdentity.Create(
              mark.SyntaxNode,
              mark.SourceTreeVersion,
              mark.FactKind,
              payload: null,
              mark.Provenance ?? FactProvenance.ForMark(mark.RuleId)).StableKey
        })
        .ToList();
    }

    internal static List<MarkRecord> ExecuteRule(AnalysisSession session, SyntaxNode root, RuleDefinitionMark rule)
    {
        var producedMarks = new List<MarkRecord>();
        foreach (var mark in rule.Mark(session.CreateMarkContext(), root))
        {
            var taggedMark = BindDeclaredFactKind(rule.Produces, mark);
            ValidateMarkNode(rule, taggedMark.SyntaxNode);
              ValidateProducedSyntax(rule.Produces, taggedMark);
            producedMarks.Add(BindMarkRecord(session, taggedMark));
        }

        foreach (var mark in producedMarks)
        {
            session.Evidence.RecordSeed(mark);
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
        if (produces.Outputs.Count > 0 && (mark.FactKind is not null || mark.SemanticTag is not null))
        {
            RuleSyntaxContractValidator.RequireProducedMark(produces, mark);
        }
    }

    internal static MarkRecord BindDeclaredFactKind(
      RuleProducesContract produces,
      MarkRecord mark)
    {
        ArgumentNullException.ThrowIfNull(produces);
        ArgumentNullException.ThrowIfNull(mark);

        if (mark.FactKind is not null || mark.SemanticTag is not null)
        {
            return mark;
        }

        var matches = produces.Outputs
          .Where(output => output.SyntaxKinds.Contains((SyntaxKind)mark.SyntaxNode.RawKind))
          .ToList();
        return matches.Count == 1
          ? mark with
          {
              SemanticTag = matches[0].SemanticTag,
              FactKind = matches[0].FactKind
          }
          : mark;
    }

    internal static MarkRecord BindMarkRecord(AnalysisSession session, MarkRecord candidate)
    {
        var annotation = candidate.Annotation ?? new SyntaxAnnotation("RuleHitNode", Guid.NewGuid().ToString("N"));
        var primaryGraphNode = candidate.PrimaryGraphNode;
        if (primaryGraphNode is null)
        {
            session.TryResolvePrimaryGraphNode(candidate.SyntaxNode, out primaryGraphNode);
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
