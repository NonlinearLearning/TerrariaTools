using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NL.Concurrency;
using NLCPG.Analysis;
using NLISSN.Core.Analysis;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Core.Pipeline;

namespace NLISSN.Core.Lifting;

public sealed class MarkLiftingEngine
{
    // 为规则图执行器执行一个已准备好显式输入的提升节点。
    internal static IReadOnlyList<LiftedMarkRecord> ExecuteRule(
      AnalysisSession session,
      RuleDefinitionLift rule,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
      IReadOnlyList<LiftedMarkRecord> existingLiftedMarks)
    {
        var ruleContext = session.CreateLiftContext(seedMarks, propagatedMarks);
        var results = rule.Lift(ruleContext, seedMarks, propagatedMarks, existingLiftedMarks)
          .Select(candidate =>
          {
              var tagged = candidate with
              {
                  Mark = MarkingEngine.BindDeclaredFactKind(rule.Produces, candidate.Mark)
              };
              ValidateLiftNode(rule, tagged.Mark.SyntaxNode);
              ValidateStructureKind(rule, tagged);
              MarkingEngine.ValidateProducedSyntax(rule.Produces, tagged.Mark);
              return BindLiftedMarkRecord(
                session,
                tagged,
                seedMarks,
                propagatedMarks,
                existingLiftedMarks);
          })
          .ToList();
        session.Evidence.RecordLift(rule.RuleId, seedMarks, propagatedMarks, results);
        return results;
    }

    // 兼容入口也按规则图执行。
    internal IReadOnlyList<LiftedMarkRecord> Run(AnalysisSession session, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<RuleDefinitionLift> rules)
    {
        var propagatedInputs = propagatedMarks.ToList();
        var consumedInputs = rules
          .SelectMany(rule => rule.Consumes.Inputs)
          .ToList();
        var sourceNodes = CreateSourceNodes(
          seedMarks,
          propagatedInputs,
          consumedInputs);
        var sourceNodeIds = sourceNodes.Select(node => node.NodeId).ToHashSet();
        var liftNodeIds = rules.Select(rule => RuleNodeId.For(RuleKind.Lift, rule.RuleId)).ToHashSet();
        var contractGraph = new RuleStructureContractGraphCompiler().Compile(sourceNodes
          .Select(node => new RuleStructureContractGraphNode(
            node.NodeId,
            RuleConsumesContract.Empty,
            new RuleProducesContract(node.ProducedSyntax)))
          .Concat(rules
          .Select(rule => new RuleStructureContractGraphNode(
            RuleNodeId.For(RuleKind.Lift, rule.RuleId),
            rule.Consumes,
            rule.Produces)))
          .ToList());
        var ruleNodes = rules.Select(rule => new RuleGraphNode(
          RuleNodeId.For(RuleKind.Lift, rule.RuleId),
          RuleKind.Lift,
          ResolveDependencies(rule, sourceNodes, sourceNodeIds, liftNodeIds, contractGraph))
        {
          ProducedSyntax = rule.Produces.Outputs
        }).ToList();
        var graph = new RuleGraphCompiler().Compile(sourceNodes.Concat(ruleNodes).ToList());
        var executionNodes = sourceNodes
          .Select(node => new RuleGraphExecutionNode(
            node,
            (_, _) => Task.FromResult(CreateSourceResult(node, seedMarks, propagatedInputs))))
          .Concat(rules.Select(rule =>
          {
              var node = graph.Nodes.Single(candidate => candidate.NodeId == RuleNodeId.For(RuleKind.Lift, rule.RuleId));
              return new RuleGraphExecutionNode(
                node,
                (inputs, _) =>
                {
                    var values = GetValues(node, inputs);
                    return Task.FromResult(CreateResult(
                      rule.Produces,
                      ExecuteRule(
                        session,
                        rule,
                        values.OfType<MarkRecord>().ToList(),
                        values.OfType<PropagatedMarkRecord>().ToList(),
                        values.OfType<LiftedMarkRecord>().ToList())));
                });
          }))
          .ToList();
        var graphDegree = ConcurrencyExecutionPolicy.ResolveMaxDegreeOfParallelism(
          session.Runtime.ExecutionOptions.EnableGroupParallelism,
          session.Runtime.ExecutionOptions.EffectiveGroupMaxDegreeOfParallelism);
        var execution = new RuleGraphExecutor(session.Runtime.Scheduler).ExecuteAsync(
            graph,
            executionNodes,
            graphDegree,
            session.Runtime.ExecutionOptions.CancellationToken)
          .GetAwaiter()
          .GetResult();

        return execution.Nodes
          .Where(node => node.NodeId.Kind == RuleKind.Lift)
          .SelectMany(node => node.Result.Values)
          .OfType<LiftedMarkRecord>()
          .DistinctBy(mark => (
            mark.RuleId,
            mark.Mark.SyntaxNode.SpanStart,
            mark.Mark.SyntaxNode.Span.Length,
            mark.Mark.SyntaxNode.RawKind))
          .ToList();
    }

    private static IReadOnlyList<RuleGraphNode> CreateSourceNodes(
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
      IReadOnlyList<RuleConsumedSyntax> consumedInputs)
    {
        return seedMarks
          .GroupBy(mark => RuleNodeId.For(RuleKind.Mark, mark.RuleId))
          .Select(group => CreateSourceNode(
            group.Key,
            RuleKind.Mark,
            group.ToList(),
            consumedInputs))
          .Concat(propagatedMarks
            .GroupBy(mark => RuleNodeId.For(RuleKind.Propagate, mark.RuleId))
            .Select(group => CreateSourceNode(
              group.Key,
              RuleKind.Propagate,
              group.Select(mark => mark.Mark).ToList(),
              consumedInputs)))
          .ToList();
    }

    private static RuleGraphNode CreateSourceNode(
      RuleNodeId nodeId,
      RuleKind kind,
      IReadOnlyList<MarkRecord> marks,
      IReadOnlyList<RuleConsumedSyntax> consumedInputs)
    {
        var produces = RuleSyntaxContractValidator.CreateObservedProduces(marks, consumedInputs);
        return new RuleGraphNode(nodeId, kind, Array.Empty<RuleDependency>())
        {
            ProducedSyntax = produces.Outputs
        };
    }

    private static IReadOnlyList<RuleDependency> ResolveDependencies(
      RuleDefinitionLift rule,
      IReadOnlyList<RuleGraphNode> sourceNodes,
      IReadOnlySet<RuleNodeId> sourceNodeIds,
      IReadOnlySet<RuleNodeId> liftNodeIds,
      CompiledRuleStructureContractGraph contractGraph)
    {
        IReadOnlyList<RuleDependency> declared = rule.Consumes.Inputs.Count > 0
          ? contractGraph.Edges
            .Where(edge => edge.Consumer == RuleNodeId.For(RuleKind.Lift, rule.RuleId))
            .Select(edge => new RuleDependency(edge.Producer, edge.Input))
            .ToList()
          : Array.Empty<RuleDependency>();
        return declared
          .Where(dependency => sourceNodeIds.Contains(dependency.Producer) || liftNodeIds.Contains(dependency.Producer))
          .Distinct()
          .ToList();
    }

    private static RuleNodeResult CreateSourceResult(
      RuleGraphNode node,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        var values = node.NodeId.Kind == RuleKind.Mark
          ? seedMarks.Where(mark => string.Equals(mark.RuleId, node.NodeId.RuleId, StringComparison.Ordinal)).Cast<object>()
          : propagatedMarks.Where(mark => string.Equals(mark.RuleId, node.NodeId.RuleId, StringComparison.Ordinal)).Cast<object>();
        return RuleNodeResult.FromObservedValues(
          values.ToList(),
          new RuleProducesContract(node.ProducedSyntax));
    }

    private static RuleNodeResult CreateResult<T>(
      RuleProducesContract produces,
      IReadOnlyList<T> values)
    {
        var boxed = values.Cast<object>().ToList();
        return RuleNodeResult.FromValues(boxed, produces);
    }

    private static IReadOnlyList<object> GetValues(RuleGraphNode node, RuleNodeInputs inputs)
    {
        return node.Dependencies
          .SelectMany(dependency => dependency.RequiredInput is { } input
            ? inputs.GetOutputs(dependency.Producer, input)
            : inputs.GetValues(dependency.Producer))
          .ToList();
    }

    internal static void ValidateLiftNode(RuleDefinitionLift rule, SyntaxNode syntaxNode)
    {
        var nodeKind = (SyntaxKind)syntaxNode.RawKind;
        if (rule.AllowedLiftNodeKinds.Contains(nodeKind))
        {
            return;
        }

        var allowedKinds = string.Join(", ", rule.AllowedLiftNodeKinds);
        throw new InvalidOperationException(
          $"Rule '{rule.RuleId}' emitted unsupported lift node kind '{nodeKind}'. Allowed lift node kinds: {allowedKinds}.");
    }

    private static void ValidateStructureKind(RuleDefinitionLift rule, LiftedMarkRecord mark)
    {
        if (mark.StructureKind is not { } structureKind)
        {
            return;
        }

        if (!Enum.IsDefined(structureKind) || !IsCompatibleStructureNode(structureKind, mark.Mark.SyntaxNode))
        {
            throw new InvalidOperationException(
              $"Rule '{rule.RuleId}' emitted incompatible structural conclusion '{structureKind}' for '{mark.Mark.SyntaxNode.Kind()}'.");
        }
    }

    private static bool IsCompatibleStructureNode(StructuralKind structureKind, SyntaxNode syntaxNode)
    {
        var kind = (SyntaxKind)syntaxNode.RawKind;
        return structureKind switch
        {
            StructuralKind.MethodDeletion => kind == SyntaxKind.MethodDeclaration,
            StructuralKind.Assignment => kind is SyntaxKind.SimpleAssignmentExpression or SyntaxKind.AddAssignmentExpression or
              SyntaxKind.SubtractAssignmentExpression or SyntaxKind.MultiplyAssignmentExpression or SyntaxKind.DivideAssignmentExpression,
            StructuralKind.LocalDefinition => kind is SyntaxKind.VariableDeclarator or SyntaxKind.LocalDeclarationStatement,
            StructuralKind.If => kind is SyntaxKind.IfStatement or SyntaxKind.ElseClause,
            StructuralKind.Loop => kind is SyntaxKind.ForStatement or SyntaxKind.WhileStatement or SyntaxKind.DoStatement or SyntaxKind.ForEachStatement,
            StructuralKind.Switch => kind is SyntaxKind.SwitchStatement or SyntaxKind.SwitchSection,
            StructuralKind.ConditionalExpression => kind == SyntaxKind.ConditionalExpression,
            StructuralKind.Return => kind == SyntaxKind.ReturnStatement,
            _ => false
        };
    }

    internal static LiftedMarkRecord BindLiftedMarkRecord(
      AnalysisSession session,
      LiftedMarkRecord candidate,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
      IReadOnlyList<LiftedMarkRecord> existingLiftedMarks)
    {
        var origins = candidate.Mark.Origins |
          candidate.SourceMark.Origins |
          FindCoveredInputOrigins(
            candidate.Mark.SyntaxNode,
            seedMarks,
            propagatedMarks,
            existingLiftedMarks);
        return candidate with
        {
            Mark = MarkingEngine.BindMarkRecord(session, candidate.Mark with { Origins = origins }),
            SourceMark = MarkingEngine.BindMarkRecord(session, candidate.SourceMark)
        };
    }

    private static RuleEvidenceOrigin FindCoveredInputOrigins(
      SyntaxNode host,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
      IReadOnlyList<LiftedMarkRecord> existingLiftedMarks)
    {
        return seedMarks
          .Concat(propagatedMarks.Select(mark => mark.Mark))
          .Concat(existingLiftedMarks.Select(mark => mark.Mark))
          .Where(mark => host.Span.Contains(mark.SyntaxNode.Span))
          .Aggregate(
            RuleEvidenceOrigin.None,
            (origins, mark) => origins | mark.Origins);
    }
}
