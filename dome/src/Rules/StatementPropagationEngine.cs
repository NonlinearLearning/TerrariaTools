namespace TerrariaTools.Dome.Rules;

using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;

/// <summary>
/// 语句级传播引擎。
/// </summary>
public sealed class StatementPropagationEngine
{
    private readonly MarkingRuleRegistry _registry;

    /// <summary>
    /// 初始化语句级传播引擎。
    /// </summary>
    /// <param name="registry">规则注册表。</param>
    public StatementPropagationEngine(MarkingRuleRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// 基于种子决策在语句依赖图中执行传播。
    /// </summary>
    /// <param name="context">分析上下文。</param>
    /// <param name="executionContext">规则执行上下文。</param>
    /// <param name="seedTarget">当前种子目标。</param>
    /// <param name="seedDecisionsByTarget">按目标分组的种子决策。</param>
    /// <returns>传播产生的决策集合。</returns>
    public IReadOnlyList<MarkDecision> Propagate(
        AnalysisContext context,
        RuleExecutionContext executionContext,
        AnalysisTarget seedTarget,
        IReadOnlyDictionary<string, IReadOnlyList<MarkDecision>> seedDecisionsByTarget)
    {
        executionContext.CancellationToken.ThrowIfCancellationRequested();

        var scopeMode = ResolveScopeMode(context, executionContext, seedTarget);
        var snapshot = context.Statements.Analyze(seedTarget.Target, scopeMode);
        var targetsByKey = context.View.Targets.ToDictionary(target => target.Target.TargetKey, StringComparer.Ordinal);

        var taintedSymbols = new Dictionary<string, MarkDecision>(StringComparer.Ordinal);
        var propagated = new List<MarkDecision>();

        foreach (var target in snapshot.Nodes
                     .Select(nodeKey => targetsByKey[nodeKey])
                     .OrderBy(target => target.Target.SpanStart)
                     .ThenBy(target => target.Target.TargetKey, StringComparer.Ordinal))
        {
            executionContext.CancellationToken.ThrowIfCancellationRequested();

            if (IsProtected(target))
            {
                taintedSymbols.Clear();
                continue;
            }

            IReadOnlyList<MarkDecision> directDecisions = seedDecisionsByTarget.TryGetValue(target.Target.TargetKey, out var seeds)
                ? seeds
                : Array.Empty<MarkDecision>();

            var emitted = new List<MarkDecision>(directDecisions);
            if (emitted.Count == 0)
            {
                var propagatedByAction = new Dictionary<PlanActionKind, (MarkDecision SourceDecision, List<SymbolRef> Symbols)>();
                foreach (var usedSymbol in target.UsesSymbols)
                {
                    if (!taintedSymbols.TryGetValue(usedSymbol.SymbolKey, out var sourceDecision))
                    {
                        continue;
                    }

                    if (_registry.PropagationRules.Any(rule => !rule.CanPropagate(target, usedSymbol, sourceDecision)))
                    {
                        continue;
                    }

                    if (!propagatedByAction.TryGetValue(sourceDecision.Action.Kind, out var propagation))
                    {
                        propagation = (sourceDecision, new List<SymbolRef>());
                        propagatedByAction[sourceDecision.Action.Kind] = propagation;
                    }

                    if (propagation.Symbols.All(symbol => !string.Equals(symbol.SymbolKey, usedSymbol.SymbolKey, StringComparison.Ordinal)))
                    {
                        propagation.Symbols.Add(usedSymbol);
                    }
                }

                foreach (var propagation in propagatedByAction.Values)
                {
                    var evidence = new PropagationEvidence(
                        propagation.Symbols.Select(symbol => symbol.SymbolKey).ToArray(),
                        propagation.Symbols.Select(symbol => symbol.DisplayName).Distinct(StringComparer.Ordinal).ToArray());
                    var propagatedDecision = MarkDecision.ForTarget(
                        target.Target,
                        propagation.SourceDecision.Action.Kind,
                        "dataflow-propagation",
                        "Propagated through a use/def dependency.",
                        propagation.SourceDecision.Action.Payload,
                        propagation.SourceDecision.Target.TargetKey,
                        propagation.SourceDecision.Target.DisplayText,
                        evidence.RelatedSymbolKeys,
                        evidence.RelatedSymbolNames,
                        chain: AppendPropagationChain(propagation.SourceDecision, target.Target, evidence));
                    emitted.Add(propagatedDecision);
                    propagated.Add(propagatedDecision);
                }
            }

            foreach (var definedSymbol in target.DefinesSymbols)
            {
                var sourceDecision = emitted.FirstOrDefault();
                if (sourceDecision != null)
                {
                    taintedSymbols[definedSymbol.SymbolKey] = sourceDecision;
                }
                else
                {
                    taintedSymbols.Remove(definedSymbol.SymbolKey);
                }
            }
        }

        return propagated;
    }

    /// <summary>
    /// 判断目标是否命中保护规则。
    /// </summary>
    /// <param name="target">分析目标。</param>
    /// <returns>是否受保护。</returns>
    private bool IsProtected(AnalysisTarget target) =>
        _registry.ProtectionRules.Any(rule => rule.Blocks(target));

    /// <summary>
    /// 解析实际使用的语句作用域模式。
    /// </summary>
    /// <param name="context">分析上下文。</param>
    /// <param name="executionContext">规则执行上下文。</param>
    /// <param name="seedTarget">种子目标。</param>
    /// <returns>语句作用域模式。</returns>
    private StatementScopeMode ResolveScopeMode(
        AnalysisContext context,
        RuleExecutionContext executionContext,
        AnalysisTarget seedTarget)
    {
        if (executionContext.StatementScopeMode != StatementScopeMode.MinimalBlock)
        {
            return executionContext.StatementScopeMode;
        }

        foreach (var rule in _registry.StatementScopeRules)
        {
            var selected = rule.SelectScopeMode(context, seedTarget);
            if (selected != StatementScopeMode.MinimalBlock)
            {
                return selected;
            }
        }

        return StatementScopeMode.MinimalBlock;
    }

    /// <summary>
    /// 追加传播链路节点。
    /// </summary>
    /// <param name="sourceDecision">源决策。</param>
    /// <param name="target">目标计划项。</param>
    /// <param name="evidence">传播证据。</param>
    /// <returns>新的传播链。</returns>
    private static PropagationChain AppendPropagationChain(
        MarkDecision sourceDecision,
        PlanTarget target,
        PropagationEvidence evidence)
    {
        var existingHops = sourceDecision.Chain?.Hops ?? Array.Empty<PropagationHop>();
        var rootTargetKey = sourceDecision.Chain?.RootTargetKey ?? sourceDecision.Target.TargetKey;
        var rootTargetDisplayText = sourceDecision.Chain?.RootTargetDisplayText ?? sourceDecision.Target.DisplayText;
        var newHop = new PropagationHop(
            sourceDecision.Target.TargetKey,
            sourceDecision.Target.DisplayText,
            target.TargetKey,
            target.DisplayText,
            "dataflow-propagation",
            sourceDecision.Action.Kind,
            evidence);

        return new PropagationChain(
            rootTargetKey,
            rootTargetDisplayText,
            existingHops.Concat([newHop]).ToArray());
    }
}
