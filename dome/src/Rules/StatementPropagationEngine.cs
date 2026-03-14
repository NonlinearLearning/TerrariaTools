namespace TerrariaTools.Dome.Rules;

using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;

public sealed class StatementPropagationEngine
{
    private readonly MarkingRuleRegistry _registry;

    public StatementPropagationEngine(MarkingRuleRegistry registry)
    {
        _registry = registry;
    }

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

    private bool IsProtected(AnalysisTarget target) =>
        _registry.ProtectionRules.Any(rule => rule.Blocks(target));

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
