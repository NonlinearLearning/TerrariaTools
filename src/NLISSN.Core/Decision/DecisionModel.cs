using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLCPG.Contracts;
using NLCPG.Model;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Core.Pipeline;

namespace NLISSN.Core.Decision;

/// 决策阶段允许的最小动作集合。
public enum DecisionActionKind
{
    /// 当前节点不产生实际改写。
    Skip = 0,

    /// 当前节点会被直接删除。
    Delete = 1,

    /// 当前节点会被替换为另一个节点。
    Replace = 2
}

/// 决策引擎输出的最终结果，供 rewrite 阶段直接消费。
public sealed record RuleDecision
{
    /// 决策最初绑定的原始语法节点。
    public SyntaxNode OriginalNode { get; init; }

    /// 当前决策最终作用的语法节点。
    public SyntaxNode FinalNode { get; init; }

    /// rewrite 阶段应执行的动作类型。
    public DecisionActionKind Action { get; init; }

    /// 当前决策的人类可读原因说明。
    public string Reason { get; init; }

    /// 当动作是 Replace 时使用的替换目标节点；否则为空。
    public SyntaxNode? ReplacementNode { get; init; }

    /// Stable root ID in the analysis evidence graph for this decision.
    public string? EvidenceRootId { get; init; }

    /// Immutable budget summary associated with the evidence root.
    public DecisionEvidence? Evidence { get; init; }

    // 记录一条最终决策及其改写原因，供 rewrite 阶段直接消费。
    public RuleDecision(SyntaxNode originalNode, SyntaxNode finalNode, DecisionActionKind action, string reason, SyntaxNode? replacementNode = null)
    {
        OriginalNode = originalNode;
        FinalNode = finalNode;
        Action = action;
        Reason = reason;
        ReplacementNode = replacementNode;
    }
}

/// 单条规则针对一组相关节点提出的候选决策。
public sealed record DecisionUnit
{
    /// 产出当前决策单元的规则标识。
    public string RuleId { get; init; }

    /// 当前决策单元建议执行的动作类型。
    public DecisionActionKind Action { get; init; }

    /// 代表整个决策单元的 CPG 抽象节点。
    public NLCPGNode UnitNode { get; init; }

    /// 当前决策单元包含的决策片段节点集合。
    public IReadOnlyList<NLCPGNode> Fragments { get; init; }

    /// 当前决策单元内部片段之间的关系边集合。
    public IReadOnlyList<NLCPGEdge> Relations { get; init; }

    /// 从决策片段节点回到真实 Roslyn 语法节点的绑定表。
    public IReadOnlyDictionary<NodeId, SyntaxNode> SyntaxBindings { get; init; }

    /// 显式声明的冲突域键；为空时由引擎按锚点结构推导。
    public string? ConflictKey { get; init; }

    /// 显式声明的合并域键；为空时由策略按锚点推导。
    public string? MergeKey { get; init; }

    /// 当前决策单元的人类可读原因说明。
    public string Reason { get; init; }

    // 描述单条规则提出的一组相关片段、关系和冲突信息，供决策策略统一收口。
    public DecisionUnit(string ruleId, DecisionActionKind action, NLCPGNode unitNode, IReadOnlyList<NLCPGNode> fragments, IReadOnlyList<NLCPGEdge> relations, IReadOnlyDictionary<NodeId, SyntaxNode> syntaxBindings, string? conflictKey = null, string? mergeKey = null, string reason = "")
    {
        RuleId = ruleId;
        Action = action;
        UnitNode = unitNode;
        Fragments = fragments;
        Relations = relations;
        SyntaxBindings = syntaxBindings;
        ConflictKey = conflictKey;
        MergeKey = mergeKey;
        Reason = reason;
    }
}

/// 决策策略接口，负责判断候选是否可合并，以及在冲突域内如何选出最终结果。
public interface DecisionPolicy
{
    // 在同一冲突域内解析出唯一最终决策。
    RuleDecision Resolve(RuleContext context, IReadOnlyList<DecisionUnit> units);
}

/// 默认决策策略。
/// 当前做法按语法覆盖关系合并：父节点覆盖子节点，并继承子节点携带的关系。
public sealed class DefaultDecisionPolicy : DecisionPolicy
{
    // 在同一冲突域内解析出唯一最终决策，并在 Replace 场景下保留替换节点绑定。
    public RuleDecision Resolve(RuleContext context, IReadOnlyList<DecisionUnit> units)
    {
        if (units.Count == 0)
        {
            throw new InvalidOperationException("Cannot resolve an empty decision unit set.");
        }

        var winner = ResolveUnit(units);

        var anchorFragment = winner.Fragments[0];
        var node = ResolveBoundSyntaxNode(winner, anchorFragment);
        if (winner.Action == DecisionActionKind.Replace && winner.Fragments.Count > 1)
        {
            var replacementFragment = winner.Fragments
              .FirstOrDefault(fragment => string.Equals(DecisionCpgFactory.GetFragmentRole(fragment), "replacement", StringComparison.Ordinal))
              ?? winner.Fragments.Last();
            var replacement = ResolveBoundSyntaxNode(winner, replacementFragment);
            return new RuleDecision(node, node, winner.Action, winner.Reason, replacement);
        }

        return new RuleDecision(node, node, winner.Action, winner.Reason);
    }

    internal DecisionUnit ResolveToUnitForTesting(RuleContext context, IReadOnlyList<DecisionUnit> units)
    {
        _ = context;
        return ResolveUnit(units);
    }

    private static SyntaxNode ResolveBoundSyntaxNode(DecisionUnit unit, NLCPGNode fragment)
    {
        if (fragment.NodeId.HasValue && unit.SyntaxBindings.TryGetValue(fragment.NodeId.Value, out var node))
        {
            return node;
        }

        throw new InvalidOperationException(
          $"Decision fragment '{DecisionCpgFactory.BuildNodeKey(fragment)}' does not have a bound syntax node.");
    }

    private static IEnumerable<DecisionUnit> MergeBySyntaxCoverage(IReadOnlyList<DecisionUnit> units)
    {
        var remaining = units.ToList();
        var merged = new List<DecisionUnit>();

        while (remaining.Count > 0)
        {
            var root = remaining
              .OrderBy(GetAnchorDepth)
              .ThenByDescending(GetAnchorSpanLength)
              .ThenBy(GetAnchorStart)
              .First();
            remaining.Remove(root);

            var coveredChildren = remaining
              .Where(candidate => IsCoveredBy(root, candidate))
              .ToList();
            foreach (var child in coveredChildren)
            {
                remaining.Remove(child);
                root = MergeIntoCoveringRoot(root, child);
            }

            merged.Add(root);
        }

        return merged;
    }

    private static bool IsCoveredBy(DecisionUnit coveringRoot, DecisionUnit candidate)
    {
        var coveringNode = TryResolveAnchorNode(coveringRoot);
        var candidateNode = TryResolveAnchorNode(candidate);
        if (coveringNode is null || candidateNode is null)
        {
            return false;
        }

        return !ReferenceEquals(coveringNode, candidateNode) &&
          coveringNode.Span.Contains(candidateNode.Span) &&
          candidateNode.Ancestors().Any(ancestor => ReferenceEquals(ancestor, coveringNode));
    }

    private static DecisionUnit MergeIntoCoveringRoot(DecisionUnit coveringRoot, DecisionUnit child)
    {
        var rootAnchor = coveringRoot.Fragments[0];
        var childAnchor = child.Fragments[0];
        var inheritedRelation = DecisionCpgFactory.CreateRelation(
          NLCPGDecisionRelationKind.Inherits,
          rootAnchor,
          childAnchor);
        var fragments = coveringRoot.Fragments
          .Concat(child.Fragments.Where(fragment =>
            !coveringRoot.Fragments.Any(existing => existing.NodeId == fragment.NodeId)))
          .ToList();
        var relations = coveringRoot.Relations
          .Concat(child.Relations)
          .Append(inheritedRelation)
          .GroupBy(
            edge => $"{edge.SourceNodeId}|{edge.TargetNodeId}|{edge.Kind}|{edge.StructuredLabel?.StableKey}",
            StringComparer.Ordinal)
          .Select(group => group.First())
          .ToList();
        var syntaxBindings = coveringRoot.SyntaxBindings
          .Concat(child.SyntaxBindings.Where(pair => !coveringRoot.SyntaxBindings.ContainsKey(pair.Key)))
          .ToDictionary(pair => pair.Key, pair => pair.Value);
        var reason = string.IsNullOrWhiteSpace(child.Reason)
          ? coveringRoot.Reason
          : string.IsNullOrWhiteSpace(coveringRoot.Reason)
            ? child.Reason
            : $"{coveringRoot.Reason} Inherited: {child.Reason}";

        return new DecisionUnit(
          coveringRoot.RuleId,
          coveringRoot.Action,
          coveringRoot.UnitNode,
          fragments,
          relations,
          syntaxBindings,
          conflictKey: coveringRoot.ConflictKey,
          mergeKey: coveringRoot.MergeKey,
          reason: reason);
    }

    private static SyntaxNode? TryResolveAnchorNode(DecisionUnit unit)
    {
        var nodeId = unit.Fragments[0].NodeId;
        return nodeId is { } value &&
          unit.SyntaxBindings.TryGetValue(value, out var node)
          ? node
          : null;
    }

    private static int GetAnchorDepth(DecisionUnit unit)
    {
        return TryResolveAnchorNode(unit)?.Ancestors().Count() ?? -1;
    }

    private static int GetAnchorSpanLength(DecisionUnit unit)
    {
        return TryResolveAnchorNode(unit)?.Span.Length ?? -1;
    }

    private static int GetAnchorStart(DecisionUnit unit)
    {
        return TryResolveAnchorNode(unit)?.SpanStart ?? int.MaxValue;
    }

    private static DecisionUnit ResolveUnit(IReadOnlyList<DecisionUnit> units)
    {
        var mergedUnits = MergeBySyntaxCoverage(units).ToList();
        return mergedUnits
          .OrderBy(GetAnchorDepth)
          .ThenByDescending(GetAnchorSpanLength)
          .ThenBy(GetAnchorStart)
          .First();
    }
}

/// 规则决策引擎。
/// 负责把 mark/propagation 阶段的结果收束成最终 rewrite 决策。
public sealed class RuleDecisionEngine
{
    private readonly DecisionPolicy _policy;

    // 使用给定决策策略初始化引擎；未提供时退回默认覆盖合并策略。
    public RuleDecisionEngine(DecisionPolicy? policy = null)
    {
        _policy = policy ?? new DefaultDecisionPolicy();
    }

    // 兼容入口也按规则图执行。
    public IReadOnlyList<RuleDecision> Decide(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks, IReadOnlyList<RuleDefinitionPropose> rules)
    {
        var consumedInputs = rules
          .SelectMany(rule => rule.Consumes.Inputs)
          .ToList();
        var sourceNodes = CreateSourceNodes(
          seedMarks,
          propagatedMarks,
          liftedMarks,
          consumedInputs);
        var contractGraph = new RuleStructureContractGraphCompiler().Compile(sourceNodes
          .Select(node => new RuleStructureContractGraphNode(
            node.NodeId,
            node.Kind,
            RuleConsumesContract.Empty,
            new RuleProducesContract(node.ProducedSyntax)))
          .Concat(rules
            .Select(rule => new RuleStructureContractGraphNode(
              rule.NodeId,
              RuleKind.Propose,
              rule.Consumes,
              rule.Produces)))
          .ToList());
        var ruleNodes = rules.Select(rule => new RuleGraphNode(
          RuleNodeId.For(RuleKind.Propose, rule.RuleId),
          RuleKind.Propose,
          ResolveDependencies(rule, sourceNodes, contractGraph))
        {
          ProducedSyntax = rule.Produces.Outputs,
          ConsumedSyntax = rule.Consumes.Inputs
        }).ToList();
        var graph = new RuleGraphCompiler().Compile(sourceNodes.Concat(ruleNodes).ToList());
        var executionNodes = sourceNodes
          .Select(node => new RuleGraphExecutionNode(
            node,
            (_, _) => Task.FromResult(CreateSourceResult(node, seedMarks, propagatedMarks, liftedMarks))))
          .Concat(rules.Select(rule =>
          {
              var node = graph.Nodes.Single(candidate => candidate.NodeId == RuleNodeId.For(RuleKind.Propose, rule.RuleId));
              return new RuleGraphExecutionNode(
                node,
                (inputs, _) =>
                {
                    var values = GetValues(node, inputs);
                    var units = rule.Propose(
                        context,
                        values.OfType<MarkRecord>().ToList(),
                        values.OfType<PropagatedMarkRecord>().ToList(),
                        values.OfType<LiftedMarkRecord>().ToList())
                      .ToList();
                    return Task.FromResult(CreateResult(rule.Produces, units));
                });
          }))
          .ToList();
        var execution = new RuleGraphExecutor(context.Runtime.ConcurrencyPool).ExecuteAsync(
            graph,
            executionNodes,
            context.Runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism,
            context.Runtime.ExecutionOptions.CancellationToken)
          .GetAwaiter()
          .GetResult();
        var units = execution.Nodes
          .Where(node => node.NodeId.Value.StartsWith("Propose:", StringComparison.Ordinal))
          .SelectMany(node => node.Result.Values)
          .OfType<DecisionUnit>()
          .ToList();

        return ResolveUnits(context, units, rules);
    }

    // 在所有相关 Proposal 节点完成后按冲突域收口，供规则图终端节点复用。
    public IReadOnlyList<RuleDecision> ResolveUnits(
      RuleContext context,
      IReadOnlyList<DecisionUnit> units,
      IReadOnlyList<RuleDefinitionPropose> rules)
    {
        var conflictDomains = units
          .GroupBy(unit => BuildConflictGroupKey(unit, rules), StringComparer.Ordinal)
          .Select(group => (IReadOnlyList<DecisionUnit>)group.ToList())
          .ToList();
        if (conflictDomains.Count == 0)
        {
            return Array.Empty<RuleDecision>();
        }

        var resolved = context.Runtime.ConcurrencyPool.SelectOrderedAsync(
            conflictDomains.Count,
            context.Runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism,
            (index, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(_policy.Resolve(context, FilterCompetingAncestors(conflictDomains[index])));
            },
            context.Runtime.ExecutionOptions.CancellationToken)
          .GetAwaiter()
          .GetResult();
        return FilterCoveredDecisions(resolved);
    }

    private static IReadOnlyList<RuleGraphNode> CreateSourceNodes(
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
      IReadOnlyList<LiftedMarkRecord> liftedMarks,
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
          .Concat(liftedMarks
            .GroupBy(mark => RuleNodeId.For(RuleKind.Lift, mark.RuleId))
            .Select(group => CreateSourceNode(
              group.Key,
              RuleKind.Lift,
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
      RuleDefinitionPropose rule,
      IReadOnlyList<RuleGraphNode> sourceNodes,
      CompiledRuleStructureContractGraph contractGraph)
    {
        return contractGraph.Edges
          .Where(edge => edge.Consumer == rule.NodeId)
          .Select(edge => new RuleDependency(edge.Producer, edge.Input))
          .ToList();
    }

    private static RuleNodeResult CreateSourceResult(
      RuleGraphNode node,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
      IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        var ruleId = node.NodeId.Value[(node.NodeId.Value.IndexOf(':') + 1)..];
        var values = node.Kind switch
        {
            RuleKind.Mark => seedMarks.Where(mark => string.Equals(mark.RuleId, ruleId, StringComparison.Ordinal)).Cast<object>(),
            RuleKind.Propagate => propagatedMarks.Where(mark => string.Equals(mark.RuleId, ruleId, StringComparison.Ordinal)).Cast<object>(),
            RuleKind.Lift => liftedMarks.Where(mark => string.Equals(mark.RuleId, ruleId, StringComparison.Ordinal)).Cast<object>(),
            _ => Array.Empty<object>()
        };
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
          .SelectMany(dependency => inputs.GetOutputs(dependency.Producer, dependency.RequiredInput))
          .ToList();
    }

    private static IReadOnlyList<DecisionUnit> FilterCompetingAncestors(IReadOnlyList<DecisionUnit> units)
    {
        var replaceAnchors = units
          .Where(unit => unit.Action == DecisionActionKind.Replace)
          .Select(TryResolveAnchorNode)
          .Where(node => node is not null)
          .Cast<SyntaxNode>()
          .ToList();
        if (replaceAnchors.Count == 0)
        {
            return units;
        }

        return units
          .Where(unit =>
          {
              if (unit.Action != DecisionActionKind.Delete)
              {
                  return true;
              }

              var anchorNode = TryResolveAnchorNode(unit);
              if (anchorNode is null)
              {
                  return true;
              }

              return !replaceAnchors.Any(replaceAnchor =>
                  ReferenceEquals(anchorNode, replaceAnchor) ||
                  (!ReferenceEquals(anchorNode, replaceAnchor) &&
                   anchorNode.Span.Contains(replaceAnchor.Span) &&
                   replaceAnchor.Ancestors().Any(ancestor => ReferenceEquals(ancestor, anchorNode))));
          })
          .ToList();
    }

    // 不同冲突键的决策在最终 rewrite 前仍可能嵌套；外层动作已覆盖子节点时只保留外层。
    private static IReadOnlyList<RuleDecision> FilterCoveredDecisions(
      IReadOnlyList<RuleDecision> decisions)
    {
        return decisions
          .Where(decision => !decisions.Any(candidate =>
            !ReferenceEquals(candidate, decision) &&
            candidate.FinalNode.Span.Contains(decision.FinalNode.Span) &&
            !candidate.FinalNode.Span.Equals(decision.FinalNode.Span)))
          .ToList();
    }

    /// 为一个决策单元确定冲突域键。
    private static string BuildConflictGroupKey(DecisionUnit unit, IReadOnlyList<RuleDefinitionPropose> rules)
    {
        // 约定第一个片段始终是决策锚点，冲突域也从它开始向外推导。
        var anchorFragment = unit.Fragments[0];
        if (!anchorFragment.NodeId.HasValue ||
            !unit.SyntaxBindings.TryGetValue(anchorFragment.NodeId.Value, out var anchorNode))
        {
            return unit.ConflictKey ?? DecisionCpgFactory.BuildNodeKey(anchorFragment);
        }

        var anchorKey = DecisionCpgFactory.BuildNodeKey(anchorNode);
        if (!string.IsNullOrWhiteSpace(unit.ConflictKey) &&
            !string.Equals(unit.ConflictKey, anchorKey, StringComparison.Ordinal))
        {
            return unit.ConflictKey;
        }

        var rule = rules.FirstOrDefault(candidate => string.Equals(candidate.RuleId, unit.RuleId, StringComparison.Ordinal));
        if (rule is null)
        {
            // 找不到规则定义时，退化回显式 conflict key 或节点键，保证引擎仍可工作。
            return unit.ConflictKey ?? anchorKey;
        }

        // Replace 决策必须绑定到当前替换锚点，不能再向外层结构合并，否则会丢掉局部规约语义。
        if (unit.Action == DecisionActionKind.Replace)
        {
            return anchorKey;
        }

        // 如果当前片段已经落在可规约的逻辑表达式子树里，优先把冲突域收口到逻辑宿主。
        var reducibleLogicalHost = anchorNode
          .DescendantNodesAndSelf()
          .OfType<BinaryExpressionSyntax>()
          .FirstOrDefault(node =>
            node.IsKind(SyntaxKind.LogicalAndExpression) ||
            node.IsKind(SyntaxKind.LogicalOrExpression));
        if (reducibleLogicalHost is not null)
        {
            return DecisionCpgFactory.BuildNodeKey(reducibleLogicalHost);
        }

        // 否则沿祖先向上，找到规则显式声明的冲突节点，把局部命中统一归并到该结构节点下。
        for (var current = anchorNode; current is not null; current = current.Parent)
        {
            var currentKind = (SyntaxKind)current.RawKind;
            if (rule.DecisionConflictNodeKinds.Contains(currentKind))
            {
                return DecisionCpgFactory.BuildNodeKey(current);
            }
        }

        // 再找不到更合理的宿主时，回退到单元自身给出的冲突键或锚点键。
        return DecisionCpgFactory.BuildNodeKey(anchorFragment);
    }

    private static SyntaxNode? TryResolveAnchorNode(DecisionUnit unit)
    {
        var nodeId = unit.Fragments[0].NodeId;
        return nodeId is { } value &&
          unit.SyntaxBindings.TryGetValue(value, out var node)
          ? node
          : null;
    }
}

public static class DecisionCpgFactory
{
    // 为真实语法节点创建决策片段 CPG 节点，并保留角色、位置和局部动作信息。
    public static NLCPGNode CreateFragment(string fragmentId, SyntaxNode node, string role, DecisionActionKind? localAction = null)
    {
        return new NLCPGNode(
          Kind: NLCPGNodeKind.DecisionFragment,
          DisplayKind: node.Kind().ToString(),
          Name: role,
          FullName: BuildNodeKey(node),
          DispatchKind: localAction is null
            ? null
            : NLCPGDispatchKind.ForDecisionAction(MapDecisionActionKind(localAction.Value)),
          FilePath: node.SyntaxTree.FilePath,
          SpanStart: node.Span.Start,
          SpanEnd: node.Span.End,
          Text: node.ToString(),
          NodeId: CreateDecisionNodeId(fragmentId));
    }

    // 为一个候选决策创建单元级 CPG 节点，承载规则标识、动作和冲突域信息。
    public static NLCPGNode CreateUnit(string ruleId, DecisionActionKind action, NLCPGNode anchorFragment, string reason, string? conflictKey = null, string? mergeKey = null)
    {
        var unitIdentity = $"decision-unit:{ruleId}:{anchorFragment.NodeId}:{action}";
        return new NLCPGNode(
          Kind: NLCPGNodeKind.DecisionUnit,
          DisplayKind: nameof(NLCPGNodeKind.DecisionUnit),
          Name: ruleId,
          FullName: conflictKey ?? mergeKey ?? BuildNodeKey(anchorFragment),
          Signature: action.ToString(),
          FilePath: anchorFragment.FilePath,
          SpanStart: anchorFragment.SpanStart,
          SpanEnd: anchorFragment.SpanEnd,
          Text: reason,
          NodeId: CreateDecisionNodeId(unitIdentity));
    }

    // 创建决策单元到片段节点的包含边。
    public static NLCPGEdge CreateContainment(NLCPGNode unitNode, NLCPGNode fragmentNode)
    {
        return new NLCPGEdge(unitNode.NodeId!.Value, fragmentNode.NodeId!.Value, NLCPGEdgeKind.DecisionContains);
    }

    // 创建两个决策片段之间的语义关系边。
    public static NLCPGEdge CreateRelation(NLCPGDecisionRelationKind kind, NLCPGNode fromFragment, NLCPGNode toFragment)
    {
        return new NLCPGEdge(
          fromFragment.NodeId!.Value,
          toFragment.NodeId!.Value,
          NLCPGEdgeKind.DecisionRelation,
          NLCPGEdgeLabel.ForDecisionRelation(kind));
    }

    // 建立决策片段节点到真实语法节点的绑定表。
    public static Dictionary<NodeId, SyntaxNode> CreateSyntaxBindings(params (NLCPGNode Fragment, SyntaxNode Node)[] bindings)
    {
        return bindings.ToDictionary(binding => binding.Fragment.NodeId!.Value, binding => binding.Node);
    }

    private static NodeId CreateDecisionNodeId(string value)
    {
        unchecked
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;
            var hash = offset;
            foreach (var character in value)
            {
                hash ^= character;
                hash *= prime;
            }

            return new NodeId(hash == 0 ? 1u : hash);
        }
    }

    // 把语法节点编码成稳定键，供冲突域和合并域分组使用。
    public static string BuildNodeKey(SyntaxNode node)
    {
        return $"{node.SyntaxTree?.FilePath}|{node.Span.Start}|{node.Span.End}|{node.RawKind}";
    }

    // 为一个 CPG 节点生成稳定键，优先复用已有 FullName。
    public static string BuildNodeKey(NLCPGNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.FullName))
        {
            return node.FullName;
        }

        return $"{node.FilePath}|{node.SpanStart}|{node.SpanEnd}|{node.DisplayKind}";
    }

    // 读取决策片段节点的角色名称，缺失时返回空字符串。
    public static string GetFragmentRole(NLCPGNode fragment)
    {
        return fragment.Name ?? string.Empty;
    }

    private static NLCPGDecisionActionKind MapDecisionActionKind(DecisionActionKind action)
    {
        return action switch
        {
            DecisionActionKind.Skip => NLCPGDecisionActionKind.Skip,
            DecisionActionKind.Delete => NLCPGDecisionActionKind.Delete,
            DecisionActionKind.Replace => NLCPGDecisionActionKind.Replace,
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
    }
}
