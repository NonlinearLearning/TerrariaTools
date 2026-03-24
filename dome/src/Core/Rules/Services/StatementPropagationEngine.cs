namespace TerrariaTools.Dome.Core.Rules.Services;

using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using ModelPrimitives = TerrariaTools.Dome.Core.Common;
using ModelRules = TerrariaTools.Dome.Core.Rules.Model;

/// <summary>
/// 负责在语句图内执行基于 use/def 关系的决策传播。
/// </summary>
public sealed class StatementPropagationEngine
{
    private readonly MarkingRuleRegistry _registry;

    /// <summary>
    /// 初始化语句传播引擎。
    /// </summary>
    public StatementPropagationEngine(MarkingRuleRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// 从种子目标出发执行语句级传播。
    /// </summary>
    public IReadOnlyList<ModelRules.MarkDecision> Propagate(
        ModelAnalysis.AnalysisContext context,
        ModelRules.RuleExecutionContext executionContext,
        ModelAnalysis.AnalysisTarget seedTarget,
        IReadOnlyDictionary<string, IReadOnlyList<ModelRules.MarkDecision>> seedDecisionsByTarget)
    {
        executionContext.CancellationToken.ThrowIfCancellationRequested();

        var scopeMode = ResolveScopeMode(context, executionContext, seedTarget);
        var snapshot = context.Statements.Analyze(GetTargetKey(seedTarget), scopeMode);
        var targetsByKey = context.View.Targets.ToDictionary(GetTargetKey, StringComparer.Ordinal);

        var taintedSymbols = new Dictionary<string, ModelRules.MarkDecision>(StringComparer.Ordinal);
        var propagated = new List<ModelRules.MarkDecision>();

        foreach (var target in snapshot.Nodes
                     .Select(nodeKey => targetsByKey[nodeKey])
                     .OrderBy(target => target.Locator.SpanStart)
                     .ThenBy(GetTargetKey, StringComparer.Ordinal))
        {
            executionContext.CancellationToken.ThrowIfCancellationRequested();

            if (IsProtected(target))
            {
                taintedSymbols.Clear();
                continue;
            }

            IReadOnlyList<ModelRules.MarkDecision> directDecisions = seedDecisionsByTarget.TryGetValue(GetTargetKey(target), out var seeds)
                ? seeds
                : Array.Empty<ModelRules.MarkDecision>();

            var emitted = new List<ModelRules.MarkDecision>(directDecisions);
            if (emitted.Count == 0)
            {
                var propagatedByAction = new Dictionary<ModelPrimitives.PlanActionKind, (ModelRules.MarkDecision SourceDecision, List<ModelAnalysis.SymbolRef> Symbols)>();
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
                        propagation = (sourceDecision, []);
                        propagatedByAction[sourceDecision.Action.Kind] = propagation;
                    }

                    if (propagation.Symbols.All(symbol => !string.Equals(symbol.SymbolKey, usedSymbol.SymbolKey, StringComparison.Ordinal)))
                    {
                        propagation.Symbols.Add(usedSymbol);
                    }
                }

                foreach (var propagation in propagatedByAction.Values)
                {
                    var evidence = new ModelRules.PropagationEvidence(
                        propagation.Symbols.Select(symbol => symbol.SymbolKey).ToArray(),
                        propagation.Symbols.Select(symbol => symbol.DisplayName).Distinct(StringComparer.Ordinal).ToArray());
                    var propagatedDecision = new ModelRules.MarkDecision(
                        target.Target,
                        target.Locator,
                        new ModelPrimitives.PlanAction(propagation.SourceDecision.Action.Kind, propagation.SourceDecision.Action.Payload),
                        new ModelRules.PlanReason(
                            "dataflow-propagation",
                            "Propagated through a use/def dependency.",
                            propagation.SourceDecision.TargetKey,
                            propagation.SourceDecision.Locator.DisplayText,
                            evidence.RelatedSymbolKeys,
                            evidence.RelatedSymbolNames,
                            Origin: ModelPrimitives.DecisionOrigin.Propagation),
                        AppendPropagationChain(propagation.SourceDecision, target, evidence));
                    emitted.Add(propagatedDecision);
                    propagated.Add(propagatedDecision);
                }
            }

            foreach (var definedSymbol in target.DefinesSymbols)
            {
                var sourceDecision = emitted.FirstOrDefault();
                if (sourceDecision is not null)
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
    /// 判断目标是否被保护规则拦截。
    /// </summary>
    private bool IsProtected(ModelAnalysis.AnalysisTarget target) =>
        _registry.ProtectionRules.Any(rule => rule.Blocks(target));

    /// <summary>
    /// 解析当前传播应当使用的语句作用域模式。
    /// </summary>
    private ModelPrimitives.StatementScopeMode ResolveScopeMode(
        ModelAnalysis.AnalysisContext context,
        ModelRules.RuleExecutionContext executionContext,
        ModelAnalysis.AnalysisTarget seedTarget)
    {
        if (executionContext.StatementScopeMode != ModelPrimitives.StatementScopeMode.MinimalBlock)
        {
            return executionContext.StatementScopeMode;
        }

        foreach (var rule in _registry.StatementScopeRules)
        {
            var selected = rule.SelectScopeMode(context, seedTarget);
            if (selected != ModelPrimitives.StatementScopeMode.MinimalBlock)
            {
                return selected;
            }
        }

        return ModelPrimitives.StatementScopeMode.MinimalBlock;
    }

    /// <summary>
    /// 将新的传播跳转追加到来源决策的传播链末尾。
    /// </summary>
    private static ModelRules.PropagationChain AppendPropagationChain(
        ModelRules.MarkDecision sourceDecision,
        ModelAnalysis.AnalysisTarget target,
        ModelRules.PropagationEvidence evidence)
    {
        var existingHops = sourceDecision.Chain?.Hops ?? Array.Empty<ModelRules.PropagationHop>();
        var rootTargetKey = sourceDecision.Chain?.RootTargetKey ?? sourceDecision.TargetKey;
        var rootTargetDisplayText = sourceDecision.Chain?.RootTargetDisplayText ?? sourceDecision.Locator.DisplayText;
        var newHop = new ModelRules.PropagationHop(
            sourceDecision.TargetKey,
            sourceDecision.Locator.DisplayText,
            GetTargetKey(target),
            target.Locator.DisplayText,
            "dataflow-propagation",
            sourceDecision.Action.Kind,
            evidence);

        return new ModelRules.PropagationChain(
            rootTargetKey,
            rootTargetDisplayText,
            existingHops.Concat([newHop]).ToArray());
    }

    /// <summary>
    /// 为分析目标构造稳定的目标键。
    /// </summary>
    private static string GetTargetKey(ModelAnalysis.AnalysisTarget target) =>
        $"{target.Target.IdentityKey}|{target.Locator.EffectiveResolutionKey.SpanStart}|{target.Locator.EffectiveResolutionKey.SpanLength}";
}
