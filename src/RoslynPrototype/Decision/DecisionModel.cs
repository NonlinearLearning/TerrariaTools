using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;
using RoslynPrototype.Lifting;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;
using Rules;

namespace RoslynPrototype.Decision;

/// <summary>
/// 决策阶段允许的最小动作集合。
/// </summary>
public enum DecisionActionKind
{
    /// <summary>
    /// 当前节点不产生实际改写。
    /// </summary>
    Skip = 0,

    /// <summary>
    /// 当前节点会被直接删除。
    /// </summary>
    Delete = 1,

    /// <summary>
    /// 当前节点会被替换为另一个节点。
    /// </summary>
    Replace = 2
}

/// <summary>
/// 决策引擎输出的最终结果，供 rewrite 阶段直接消费。
/// </summary>
public sealed record RuleDecision
{
    /// <summary>
    /// 决策最初绑定的原始语法节点。
    /// </summary>
    public SyntaxNode OriginalNode { get; init; }

    /// <summary>
    /// 当前决策最终作用的语法节点。
    /// </summary>
    public SyntaxNode FinalNode { get; init; }

    /// <summary>
    /// rewrite 阶段应执行的动作类型。
    /// </summary>
    public DecisionActionKind Action { get; init; }

    /// <summary>
    /// 当前决策的人类可读原因说明。
    /// </summary>
    public string Reason { get; init; }

    /// <summary>
    /// 当动作是 Replace 时使用的替换目标节点；否则为空。
    /// </summary>
    public SyntaxNode? ReplacementNode { get; init; }

    public RuleDecision(SyntaxNode originalNode, SyntaxNode finalNode, DecisionActionKind action, string reason, SyntaxNode? replacementNode = null)
    {
        OriginalNode = originalNode;
        FinalNode = finalNode;
        Action = action;
        Reason = reason;
        ReplacementNode = replacementNode;
    }
}

/// <summary>
/// 单条规则针对一组相关节点提出的候选决策。
/// </summary>
public sealed record DecisionUnit
{
    /// <summary>
    /// 产出当前决策单元的规则标识。
    /// </summary>
    public string RuleId { get; init; }

    /// <summary>
    /// 当前决策单元建议执行的动作类型。
    /// </summary>
    public DecisionActionKind Action { get; init; }

    /// <summary>
    /// 代表整个决策单元的 CPG 抽象节点。
    /// </summary>
    public RoslynCpgNode UnitNode { get; init; }

    /// <summary>
    /// 当前决策单元包含的决策片段节点集合。
    /// </summary>
    public IReadOnlyList<RoslynCpgNode> Fragments { get; init; }

    /// <summary>
    /// 当前决策单元内部片段之间的关系边集合。
    /// </summary>
    public IReadOnlyList<RoslynCpgEdge> Relations { get; init; }

    /// <summary>
    /// 从决策片段节点回到真实 Roslyn 语法节点的绑定表。
    /// </summary>
    public IReadOnlyDictionary<string, SyntaxNode> SyntaxBindings { get; init; }

    /// <summary>
    /// 显式声明的冲突域键；为空时由引擎按锚点结构推导。
    /// </summary>
    public string? ConflictKey { get; init; }

    /// <summary>
    /// 显式声明的合并域键；为空时由策略按锚点推导。
    /// </summary>
    public string? MergeKey { get; init; }

    /// <summary>
    /// 当前决策单元的人类可读原因说明。
    /// </summary>
    public string Reason { get; init; }

    public string? GroupKey { get; init; }

    public DecisionUnit(string ruleId, DecisionActionKind action, RoslynCpgNode unitNode, IReadOnlyList<RoslynCpgNode> fragments, IReadOnlyList<RoslynCpgEdge> relations, IReadOnlyDictionary<string, SyntaxNode> syntaxBindings, string? conflictKey = null, string? mergeKey = null, string reason = "", string? groupKey = null)
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

/// <summary>
/// 决策策略接口，负责判断候选是否可合并，以及在冲突域内如何选出最终结果。
/// </summary>
public interface DecisionPolicy
{
    /// <summary>
    /// 在同一冲突域内解析出唯一的最终决策。
    /// </summary>
    RuleDecision Resolve(RuleContext context, IReadOnlyList<DecisionUnit> units);
}

/// <summary>
/// 默认决策策略。
/// 当前做法按语法覆盖关系合并：父节点覆盖子节点，并继承子节点携带的关系。
/// </summary>
public sealed class DefaultDecisionPolicy : DecisionPolicy
{
    /// <summary>
     /// 从同一冲突域内选出一个最终决策。
    /// 若存在父子覆盖关系，则父节点成为唯一锚点，并继承子节点携带的片段和关系。
     /// </summary>
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

    private static SyntaxNode ResolveBoundSyntaxNode(DecisionUnit unit, RoslynCpgNode fragment)
    {
        if (unit.SyntaxBindings.TryGetValue(fragment.Id, out var node))
        {
            return node;
        }

        throw new InvalidOperationException(
          $"Decision fragment '{fragment.Id}' does not have a bound syntax node.");
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
        var inheritedRelation = DecisionCpgFactory.CreateRelation("inherits", rootAnchor, childAnchor);
        var fragments = coveringRoot.Fragments
          .Concat(child.Fragments.Where(fragment => !coveringRoot.Fragments.Any(existing => existing.Id == fragment.Id)))
          .ToList();
        var relations = coveringRoot.Relations
          .Concat(child.Relations)
          .Append(inheritedRelation)
          .GroupBy(edge => $"{edge.SourceId}|{edge.TargetId}|{edge.Kind}|{edge.Label}", StringComparer.Ordinal)
          .Select(group => group.First())
          .ToList();
        var syntaxBindings = coveringRoot.SyntaxBindings
          .Concat(child.SyntaxBindings.Where(pair => !coveringRoot.SyntaxBindings.ContainsKey(pair.Key)))
          .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
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
        return unit.SyntaxBindings.TryGetValue(unit.Fragments[0].Id, out var node)
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

/// <summary>
/// 规则决策引擎。
/// 负责把 mark/propagation 阶段的结果收束成最终 rewrite 决策。
/// </summary>
public sealed class RuleDecisionEngine
{
    private readonly DecisionPolicy _policy;

    /// <summary>
    /// 构造决策引擎。未显式提供策略时，使用默认最小策略。
    /// </summary>
    public RuleDecisionEngine(DecisionPolicy? policy = null)
    {
        _policy = policy ?? new DefaultDecisionPolicy();
    }

    /// <summary>
    /// 汇总所有规则的候选决策，并按冲突域收口为最终决策列表。
    /// </summary>
    public IReadOnlyList<RuleDecision> Decide(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks, IReadOnlyList<RuleDefinitionPropose> rules)
    {
        var units = new List<DecisionUnit>();

        // 先按规则分桶，避免每条规则在 Propose 阶段重复扫描全量 marks。
        var seedMarksByGroupKey = seedMarks
          .GroupBy(RuleStageGroupKey.Get, StringComparer.Ordinal)
          .ToDictionary(group => group.Key, group => (IReadOnlyList<MarkRecord>)group.ToList(), StringComparer.Ordinal);
        var propagatedMarksByGroupKey = propagatedMarks
          .GroupBy(RuleStageGroupKey.Get, StringComparer.Ordinal)
          .ToDictionary(group => group.Key, group => (IReadOnlyList<PropagatedMarkRecord>)group.ToList(), StringComparer.Ordinal);
        var liftedMarksByGroupKey = liftedMarks
          .GroupBy(RuleStageGroupKey.Get, StringComparer.Ordinal)
          .ToDictionary(group => group.Key, group => (IReadOnlyList<LiftedMarkRecord>)group.ToList(), StringComparer.Ordinal);

        foreach (var rule in rules)
        {
            // 每条规则只消费自己的 seed/propagated marks，然后产出局部 decision units。
            seedMarksByGroupKey.TryGetValue(rule.GroupKey, out var ruleSeedMarks);
            propagatedMarksByGroupKey.TryGetValue(rule.GroupKey, out var rulePropagatedMarks);
            liftedMarksByGroupKey.TryGetValue(rule.GroupKey, out var ruleLiftedMarks);

            units.AddRange(rule.Propose(
              context,
              ruleSeedMarks ?? Array.Empty<MarkRecord>(),
              rulePropagatedMarks ?? Array.Empty<PropagatedMarkRecord>(),
              ruleLiftedMarks ?? Array.Empty<LiftedMarkRecord>())
              .Select(unit => unit.GroupKey is null
                ? unit with { GroupKey = rule.GroupKey }
                : unit));
        }

        var decisions = new List<RuleDecision>();
        // 同一冲突域内只保留一个最终决策，避免 seed mark、传播 mark、结构宿主重复下刀。
        foreach (var unitGroup in units.GroupBy(unit => BuildConflictGroupKey(unit, rules), StringComparer.Ordinal))
        {
            decisions.Add(_policy.Resolve(context, FilterCompetingAncestors(unitGroup.ToList())));
        }

        return decisions;
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

    /// <summary>
    /// 为一个决策单元确定冲突域键。
    /// 目标不是完整建模 CPG，而是用最小语法结构把明显互斥的候选收口到一起。
    /// </summary>
    private static string BuildConflictGroupKey(DecisionUnit unit, IReadOnlyList<RuleDefinitionPropose> rules)
    {
        if (!string.IsNullOrWhiteSpace(unit.ConflictKey))
        {
            return unit.ConflictKey;
        }

        // 约定第一个片段始终是决策锚点，冲突域也从它开始向外推导。
        var anchorFragment = unit.Fragments[0];
        if (!unit.SyntaxBindings.TryGetValue(anchorFragment.Id, out var anchorNode))
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
        return unit.SyntaxBindings.TryGetValue(unit.Fragments[0].Id, out var node)
          ? node
          : null;
    }
}

public static class DecisionCpgFactory
{
    /// <summary>
    /// 为一个语法节点创建决策片段对应的 CPG 抽象节点。
    /// </summary>
    /// <param name="fragmentId">决策片段稳定标识。</param>
    /// <param name="node">片段绑定的真实语法节点。</param>
    /// <param name="role">片段在决策单元中的角色，例如 anchor 或 replacement。</param>
    /// <param name="localAction">片段局部动作；若为空则表示仅承担结构角色。</param>
    /// <returns>对应的决策片段 CPG 节点。</returns>
    public static RoslynCpgNode CreateFragment(string fragmentId, SyntaxNode node, string role, DecisionActionKind? localAction = null)
    {
        return new RoslynCpgNode(
          Id: fragmentId,
          Kind: RoslynCpgNodeKind.DecisionFragment,
          DisplayKind: node.Kind().ToString(),
          Name: role,
          FullName: BuildNodeKey(node),
          DispatchKind: localAction?.ToString(),
          FilePath: node.SyntaxTree.FilePath,
          SpanStart: node.Span.Start,
          SpanEnd: node.Span.End,
          Text: node.ToString());
    }

    /// <summary>
    /// 为一个候选决策创建单元级 CPG 抽象节点。
    /// </summary>
    /// <param name="ruleId">产出该单元的规则标识。</param>
    /// <param name="action">该单元建议执行的动作类型。</param>
    /// <param name="anchorFragment">作为单元锚点的决策片段节点。</param>
    /// <param name="reason">人类可读原因说明。</param>
    /// <param name="conflictKey">显式冲突域键。</param>
    /// <param name="mergeKey">显式合并域键。</param>
    /// <returns>对应的决策单元 CPG 节点。</returns>
    public static RoslynCpgNode CreateUnit(string ruleId, DecisionActionKind action, RoslynCpgNode anchorFragment, string reason, string? conflictKey = null, string? mergeKey = null)
    {
        return new RoslynCpgNode(
          Id: $"decision-unit:{ruleId}:{anchorFragment.Id}:{action}",
          Kind: RoslynCpgNodeKind.DecisionUnit,
          DisplayKind: nameof(RoslynCpgNodeKind.DecisionUnit),
          Name: ruleId,
          FullName: conflictKey ?? mergeKey ?? BuildNodeKey(anchorFragment),
          Signature: action.ToString(),
          FilePath: anchorFragment.FilePath,
          SpanStart: anchorFragment.SpanStart,
          SpanEnd: anchorFragment.SpanEnd,
          Text: reason);
    }

    /// <summary>
    /// 创建决策单元到决策片段的包含边。
    /// </summary>
    /// <param name="unitNode">决策单元节点。</param>
    /// <param name="fragmentNode">被包含的决策片段节点。</param>
    /// <returns>表示包含关系的决策边。</returns>
    public static RoslynCpgEdge CreateContainment(RoslynCpgNode unitNode, RoslynCpgNode fragmentNode)
    {
        return new RoslynCpgEdge(unitNode.Id, fragmentNode.Id, RoslynCpgEdgeKind.DecisionContains);
    }

    /// <summary>
    /// 创建两个决策片段之间的语义关系边。
    /// </summary>
    /// <param name="kind">关系标签，例如 derived-from 或 reduced-to。</param>
    /// <param name="fromFragment">关系起点片段。</param>
    /// <param name="toFragment">关系终点片段。</param>
    /// <returns>表示片段语义关系的决策边。</returns>
    public static RoslynCpgEdge CreateRelation(string kind, RoslynCpgNode fromFragment, RoslynCpgNode toFragment)
    {
        return new RoslynCpgEdge(
          fromFragment.Id,
          toFragment.Id,
          RoslynCpgEdgeKind.DecisionRelation,
          kind);
    }

    /// <summary>
    /// 建立决策片段节点到真实语法节点的绑定表。
    /// </summary>
    /// <param name="bindings">片段节点与语法节点的绑定对集合。</param>
    /// <returns>以片段节点 id 为键的语法绑定表。</returns>
    public static Dictionary<string, SyntaxNode> CreateSyntaxBindings(params (RoslynCpgNode Fragment, SyntaxNode Node)[] bindings)
    {
        return bindings.ToDictionary(binding => binding.Fragment.Id, binding => binding.Node, StringComparer.Ordinal);
    }

    /// <summary>
    /// 把语法节点编码成稳定键，供冲突域和合并域分组使用。
    /// </summary>
    /// <param name="node">需要编码的语法节点。</param>
    /// <returns>由文件路径、跨度和语法种类组成的稳定键。</returns>
    public static string BuildNodeKey(SyntaxNode node)
    {
        return $"{node.SyntaxTree?.FilePath}|{node.Span.Start}|{node.Span.End}|{node.RawKind}";
    }

    /// <summary>
    /// 为一个 CPG 节点生成稳定键。
    /// </summary>
    /// <param name="node">需要编码的 CPG 节点。</param>
    /// <returns>优先使用节点 FullName，否则回退到文件位置和显示种类组成的键。</returns>
    public static string BuildNodeKey(RoslynCpgNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.FullName))
        {
            return node.FullName;
        }

        return $"{node.FilePath}|{node.SpanStart}|{node.SpanEnd}|{node.DisplayKind}";
    }

    /// <summary>
    /// 读取决策片段节点的角色名称。
    /// </summary>
    /// <param name="fragment">目标决策片段节点。</param>
    /// <returns>片段角色名；若未设置则返回空字符串。</returns>
    public static string GetFragmentRole(RoslynCpgNode fragment)
    {
        return fragment.Name ?? string.Empty;
    }
}
