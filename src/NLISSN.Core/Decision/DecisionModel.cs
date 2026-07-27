using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLCPG.Contracts;
using NLCPG.Model;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Rules;

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

    public string? GroupKey { get; init; }

    // 描述单条规则提出的一组相关片段、关系和冲突信息，供决策策略统一收口。
    public DecisionUnit(string ruleId, DecisionActionKind action, NLCPGNode unitNode, IReadOnlyList<NLCPGNode> fragments, IReadOnlyList<NLCPGEdge> relations, IReadOnlyDictionary<NodeId, SyntaxNode> syntaxBindings, string? conflictKey = null, string? mergeKey = null, string reason = "", string? groupKey = null)
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
        GroupKey = groupKey;
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
          reason: reason,
          groupKey: coveringRoot.GroupKey);
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

    // 让提案规则按组消费三类标记，并在每个冲突域内收口成最终 rewrite 决策。
    public IReadOnlyList<RuleDecision> Decide(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks, IReadOnlyList<RuleDefinitionPropose> rules)
    {
        // 先按 group 分桶，避免同组规则各自重复扫描全量 marks。
        var seedMarksByGroupKey = seedMarks
          .GroupBy(RuleStageGroupKey.Get, StringComparer.Ordinal)
          .ToDictionary(group => group.Key, group => (IReadOnlyList<MarkRecord>)group.ToList(), StringComparer.Ordinal);
        var propagatedMarksByGroupKey = propagatedMarks
          .GroupBy(RuleStageGroupKey.Get, StringComparer.Ordinal)
          .ToDictionary(group => group.Key, group => (IReadOnlyList<PropagatedMarkRecord>)group.ToList(), StringComparer.Ordinal);
        var liftedMarksByGroupKey = liftedMarks
          .GroupBy(RuleStageGroupKey.Get, StringComparer.Ordinal)
          .ToDictionary(group => group.Key, group => (IReadOnlyList<LiftedMarkRecord>)group.ToList(), StringComparer.Ordinal);
        var groupedRules = rules
          .GroupBy(rule => rule.GroupKey, StringComparer.Ordinal)
          .Select(group => new ProposalRuleGroup(group.Key, group.ToList()))
          .Where(group =>
            seedMarksByGroupKey.ContainsKey(group.GroupKey) ||
            propagatedMarksByGroupKey.ContainsKey(group.GroupKey) ||
            liftedMarksByGroupKey.ContainsKey(group.GroupKey))
          .ToList();
        var units = ShouldRunGroupsInParallel(context, groupedRules.Count)
          ? RunGroupsInParallel(
            context,
            groupedRules,
            seedMarksByGroupKey,
            propagatedMarksByGroupKey,
            liftedMarksByGroupKey)
          : RunGroupsSerial(
            context,
            groupedRules,
            seedMarksByGroupKey,
            propagatedMarksByGroupKey,
            liftedMarksByGroupKey);

        var decisions = new List<RuleDecision>();
        // 同一冲突域内只保留一个最终决策，避免 seed mark、传播 mark、结构宿主重复下刀。
        foreach (var unitGroup in units.GroupBy(unit => BuildConflictGroupKey(unit, rules), StringComparer.Ordinal))
        {
            decisions.Add(_policy.Resolve(context, FilterCompetingAncestors(unitGroup.ToList())));
        }

        return decisions;
    }

    private static List<DecisionUnit> RunGroupsSerial(RuleContext context, IReadOnlyList<ProposalRuleGroup> groupedRules, IReadOnlyDictionary<string, IReadOnlyList<MarkRecord>> seedMarksByGroupKey, IReadOnlyDictionary<string, IReadOnlyList<PropagatedMarkRecord>> propagatedMarksByGroupKey, IReadOnlyDictionary<string, IReadOnlyList<LiftedMarkRecord>> liftedMarksByGroupKey)
    {
        var units = new List<DecisionUnit>();
        foreach (var ruleGroup in groupedRules)
        {
            units.AddRange(RunGroup(
              context,
              ruleGroup,
              seedMarksByGroupKey,
              propagatedMarksByGroupKey,
              liftedMarksByGroupKey));
        }

        return units;
    }

    private static List<DecisionUnit> RunGroupsInParallel(RuleContext context, IReadOnlyList<ProposalRuleGroup> groupedRules, IReadOnlyDictionary<string, IReadOnlyList<MarkRecord>> seedMarksByGroupKey, IReadOnlyDictionary<string, IReadOnlyList<PropagatedMarkRecord>> propagatedMarksByGroupKey, IReadOnlyDictionary<string, IReadOnlyList<LiftedMarkRecord>> liftedMarksByGroupKey)
    {
        var orderedUnits = context.Runtime.Scheduler.RunOrderedAsync(
            groupedRules.Count,
            context.Runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism,
            (index, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.Run(
                  () => (IReadOnlyList<DecisionUnit>)RunGroup(
                    context,
                    groupedRules[index],
                    seedMarksByGroupKey,
                    propagatedMarksByGroupKey,
                    liftedMarksByGroupKey),
                  cancellationToken);
            },
            context.Runtime.ExecutionOptions.CancellationToken)
          .GetAwaiter()
          .GetResult();

        return orderedUnits.SelectMany(units => units).ToList();
    }

    private static List<DecisionUnit> RunGroup(RuleContext context, ProposalRuleGroup ruleGroup, IReadOnlyDictionary<string, IReadOnlyList<MarkRecord>> seedMarksByGroupKey, IReadOnlyDictionary<string, IReadOnlyList<PropagatedMarkRecord>> propagatedMarksByGroupKey, IReadOnlyDictionary<string, IReadOnlyList<LiftedMarkRecord>> liftedMarksByGroupKey)
    {
        var units = new List<DecisionUnit>();
        foreach (var rule in ruleGroup.Rules)
        {
            units.AddRange(RunRule(
              context,
              rule,
              seedMarksByGroupKey,
              propagatedMarksByGroupKey,
              liftedMarksByGroupKey));
        }

        return units;
    }

    private static List<DecisionUnit> RunRule(RuleContext context, RuleDefinitionPropose rule, IReadOnlyDictionary<string, IReadOnlyList<MarkRecord>> seedMarksByGroupKey, IReadOnlyDictionary<string, IReadOnlyList<PropagatedMarkRecord>> propagatedMarksByGroupKey, IReadOnlyDictionary<string, IReadOnlyList<LiftedMarkRecord>> liftedMarksByGroupKey)
    {
        seedMarksByGroupKey.TryGetValue(rule.GroupKey, out var ruleSeedMarks);
        propagatedMarksByGroupKey.TryGetValue(rule.GroupKey, out var rulePropagatedMarks);
        liftedMarksByGroupKey.TryGetValue(rule.GroupKey, out var ruleLiftedMarks);

        return rule.Propose(
            context,
            ruleSeedMarks ?? Array.Empty<MarkRecord>(),
            rulePropagatedMarks ?? Array.Empty<PropagatedMarkRecord>(),
            ruleLiftedMarks ?? Array.Empty<LiftedMarkRecord>())
          .Select(unit => unit.GroupKey is null
            ? unit with { GroupKey = rule.GroupKey }
            : unit)
          .ToList();
    }

    private static bool ShouldRunGroupsInParallel(RuleContext context, int groupCount)
    {
        return context.Runtime.ExecutionOptions.EnableGroupParallelism &&
          context.Runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism > 1 &&
          groupCount > 1;
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

    /// 为一个决策单元确定冲突域键。
    private static string BuildConflictGroupKey(DecisionUnit unit, IReadOnlyList<RuleDefinitionPropose> rules)
    {
        if (!string.IsNullOrWhiteSpace(unit.ConflictKey))
        {
            return unit.ConflictKey;
        }

        // 约定第一个片段始终是决策锚点，冲突域也从它开始向外推导。
        var anchorFragment = unit.Fragments[0];
        if (!anchorFragment.NodeId.HasValue ||
            !unit.SyntaxBindings.TryGetValue(anchorFragment.NodeId.Value, out var anchorNode))
        {
            return DecisionCpgFactory.BuildNodeKey(anchorFragment);
        }

        var rule = rules.FirstOrDefault(candidate => string.Equals(candidate.RuleId, unit.RuleId, StringComparison.Ordinal));
        if (rule is null)
        {
            // 找不到规则定义时，退化回显式 conflict key 或节点键，保证引擎仍可工作。
            return DecisionCpgFactory.BuildNodeKey(anchorFragment);
        }

        // Replace 决策必须绑定到当前替换锚点，不能再向外层结构合并，否则会丢掉局部规约语义。
        if (unit.Action == DecisionActionKind.Replace)
        {
            return DecisionCpgFactory.BuildNodeKey(anchorFragment);
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

    private sealed record ProposalRuleGroup(
      string GroupKey,
      IReadOnlyList<RuleDefinitionPropose> Rules);
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
