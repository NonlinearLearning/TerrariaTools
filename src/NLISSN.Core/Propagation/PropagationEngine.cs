using NLCPG.Analysis;
using NL.Concurrency;
using NLISSN.Core.Analysis;
using NLISSN.Core.Marking;
using NLISSN.Core.Lifting;
using NLISSN.Core.Pipeline;

namespace NLISSN.Core.Propagation;

public sealed class PropagationEngine
{
    // 为规则图执行器执行一个已准备好显式输入的传播节点。
    internal static IReadOnlyList<PropagatedMarkRecord> ExecuteRule(
      AnalysisSession session,
      RuleDefinitionPropagate rule,
      IReadOnlyList<MarkRecord> inputMarks)
    {
        var results = EvaluateRule(session, rule, inputMarks);
        session.Evidence.RecordPropagation(rule.RuleId, inputMarks, results);
        return results;
    }

    internal static IReadOnlyList<PropagatedMarkRecord> EvaluateRule(
      AnalysisSession session,
      RuleDefinitionPropagate rule,
      IReadOnlyList<MarkRecord> inputMarks)
    {
        return rule.Propagate(session.CreatePropagationContext(inputMarks), inputMarks)
          .Select(candidate =>
          {
              var tagged = candidate with
              {
                  Mark = MarkingEngine.BindDeclaredSemanticTag(rule.Produces, candidate.Mark)
              };
              ValidatePropagateNode(rule, tagged.Mark.SyntaxNode);
              ValidatePropagationPayload(rule, tagged.Payload);
              MarkingEngine.ValidateProducedSyntax(rule.Produces, tagged.Mark);
              return BindPropagatedMarkRecord(session, tagged);
          })
          .ToList();
    }

    // 兼容入口在传播区域内执行固定点，且只返回首次接纳的稳定事实。
    internal IReadOnlyList<PropagatedMarkRecord> Run(AnalysisSession session, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<RuleDefinitionPropagate> rules)
    {
        var consumedInputs = rules
          .SelectMany(rule => rule.Consumes.Inputs)
          .ToList();
        var sourceNodes = seedMarks
          .GroupBy(mark => mark.RuleId, StringComparer.Ordinal)
          .Select(group =>
          {
              var produces = RuleSyntaxContractValidator.CreateObservedProduces(
                group.ToList(),
                consumedInputs);
              return new RuleGraphNode(
                RuleNodeId.For(RuleKind.Mark, group.Key),
                RuleKind.Mark,
                Array.Empty<RuleDependency>())
              {
                ProducedSyntax = produces.Outputs
              };
          })
          .ToList();
        _ = new RuleStructureContractGraphCompiler().Compile(sourceNodes
          .Select(node => new RuleStructureContractGraphNode(
            node.NodeId,
            RuleConsumesContract.Empty,
            new RuleProducesContract(node.ProducedSyntax)))
          .Concat(rules
          .Select(rule => new RuleStructureContractGraphNode(
            RuleNodeId.For(RuleKind.Propagate, rule.RuleId),
            rule.Consumes,
            rule.Produces)))
          .ToList(),
          RuleStructureContractGraphMode.PropagationFixedPointRegion);
        return new PropagationFixedPointExecutor().Run(session, seedMarks, rules);
    }

    private static PropagatedMarkRecord BindPropagatedMarkRecord(AnalysisSession session, PropagatedMarkRecord candidate)
    {
        var origins = candidate.Mark.Origins | candidate.SourceMark.Origins;
        return candidate with
        {
            Mark = MarkingEngine.BindMarkRecord(session, candidate.Mark with { Origins = origins }),
            SourceMark = MarkingEngine.BindMarkRecord(session, candidate.SourceMark)
        };
    }

    internal static void ValidatePropagateNode(RuleDefinitionPropagate rule, Microsoft.CodeAnalysis.SyntaxNode syntaxNode)
    {
        var nodeKind = (Microsoft.CodeAnalysis.CSharp.SyntaxKind)syntaxNode.RawKind;
        if (rule.AllowedPropagateNodeKinds.Contains(nodeKind))
        {
            return;
        }

        var allowedKinds = string.Join(", ", rule.AllowedPropagateNodeKinds);
        throw new InvalidOperationException(
          $"Rule '{rule.RuleId}' emitted unsupported propagate node kind '{nodeKind}'. Allowed propagate node kinds: {allowedKinds}.");
    }

    private static void ValidatePropagationPayload(RuleDefinitionPropagate rule, object? payload)
    {
        if (payload is not ILiftPayload)
        {
            return;
        }

        throw new InvalidOperationException(
          $"Rule '{rule.RuleId}' emitted a Lift-owned structural payload from Propagate.");
    }

}
