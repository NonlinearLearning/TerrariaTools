namespace TerrariaTools.Dome.Rules;

using TerrariaTools.Dome.Core;

public interface IMarkingRule
{
    IEnumerable<MarkDecision> Evaluate(AnalysisTarget target);
}

public sealed class DirectiveMarkingRule : IMarkingRule
{
    public IEnumerable<MarkDecision> Evaluate(AnalysisTarget target)
    {
        foreach (var directive in target.Directives)
        {
            yield return MarkDecision.ForTarget(
                target.Target,
                directive.ActionKind,
                directive.RuleId,
                directive.ReasonText,
                directive.Payload);
        }
    }
}

public sealed class MarkingRuleRegistry
{
    private readonly IReadOnlyList<IMarkingRule> _rules;

    public MarkingRuleRegistry(IEnumerable<IMarkingRule> rules)
    {
        _rules = rules.ToArray();
    }

    public IReadOnlyList<IMarkingRule> Rules => _rules;

    public static MarkingRuleRegistry CreateDefault() => new([new DirectiveMarkingRule()]);
}

public sealed class MarkingRuleEngine
{
    private readonly MarkingRuleRegistry _registry;

    public MarkingRuleEngine(MarkingRuleRegistry registry)
    {
        _registry = registry;
    }

    public IReadOnlyList<MarkDecision> Execute(AnalysisView analysisView)
    {
        var seedDecisionsByTarget = new Dictionary<string, List<MarkDecision>>(StringComparer.Ordinal);

        foreach (var target in analysisView.Targets)
        {
            if (target.IsHighRisk)
            {
                continue;
            }

            foreach (var rule in _registry.Rules)
            {
                foreach (var decision in rule.Evaluate(target))
                {
                    if (!seedDecisionsByTarget.TryGetValue(decision.Target.TargetKey, out var list))
                    {
                        list = new List<MarkDecision>();
                        seedDecisionsByTarget[decision.Target.TargetKey] = list;
                    }

                    list.Add(decision);
                }
            }
        }

        var finalDecisions = new List<MarkDecision>();

        foreach (var memberTargets in analysisView.Targets
                     .GroupBy(target => $"{target.Target.DocumentPath}|{target.Target.MemberId.Value}", StringComparer.Ordinal)
                     .Select(group => group.OrderBy(target => target.Target.SpanStart).ToArray()))
        {
            var taintedSymbols = new Dictionary<string, MarkDecision>(StringComparer.Ordinal);

            foreach (var target in memberTargets)
            {
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
                        var chain = AppendPropagationChain(
                            propagation.SourceDecision,
                            target.Target,
                            evidence);

                        emitted.Add(MarkDecision.ForTarget(
                            target.Target,
                            propagation.SourceDecision.Action.Kind,
                            "dataflow-propagation",
                            "Propagated through a use/def dependency.",
                            propagation.SourceDecision.Action.Payload,
                            propagation.SourceDecision.Target.TargetKey,
                            propagation.SourceDecision.Target.DisplayText,
                            evidence.RelatedSymbolKeys,
                            evidence.RelatedSymbolNames,
                            chain: chain));
                    }
                }

                finalDecisions.AddRange(emitted);

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
        }

        return finalDecisions
            .GroupBy(decision => $"{decision.Target.TargetKey}|{decision.Action.Kind}|{decision.Reason.RuleId}|{decision.Reason.SourceTargetKey}", StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
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
