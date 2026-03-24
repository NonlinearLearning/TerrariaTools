namespace TerrariaTools.Dome.Adapters.Analysis.Roslyn;

using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using ModelPrimitives = TerrariaTools.Dome.Core.Common;

public sealed partial class StatementAnalysisService : ModelAnalysis.IStatementAnalysisService
{
    private readonly ModelAnalysis.StatementFactsIndex _index;

    public StatementAnalysisService(ModelAnalysis.StatementFactsIndex index)
    {
        _index = index;
    }

    public ModelAnalysis.StatementGraphSnapshot Analyze(string targetKey) =>
        Analyze(targetKey, ModelPrimitives.StatementScopeMode.MinimalBlock);

    public ModelAnalysis.StatementGraphSnapshot Analyze(string targetKey, ModelPrimitives.StatementScopeMode scopeMode)
    {
        if (!_index.FactsByTargetKey.TryGetValue(targetKey, out var seedFact))
        {
            throw new InvalidOperationException($"Statement target '{targetKey}' was not found in statement facts.");
        }

        if (!_index.FactsByMemberId.TryGetValue(seedFact.MemberId.Value, out var bucket))
        {
            throw new InvalidOperationException($"No statement facts found for member '{seedFact.MemberId.Value}'.");
        }

        var orderedBucket = bucket
            .OrderBy(fact => fact.SpanStart)
            .ThenBy(fact => fact.TargetKey, StringComparer.Ordinal)
            .ToArray();
        var factsByTargetKey = orderedBucket.ToDictionary(fact => fact.TargetKey, StringComparer.Ordinal);

        var includedTargetKeys = scopeMode switch
        {
            ModelPrimitives.StatementScopeMode.MinimalBlock => CollectMinimalBlock(seedFact, orderedBucket),
            ModelPrimitives.StatementScopeMode.ParentBlockPiercing => CollectParentPiercing(seedFact, orderedBucket),
            _ => throw new ArgumentOutOfRangeException(nameof(scopeMode), scopeMode, "Unsupported statement scope mode.")
        };

        var includedFacts = includedTargetKeys
            .Select(key => factsByTargetKey[key])
            .OrderBy(fact => fact.SpanStart)
            .ThenBy(fact => fact.TargetKey, StringComparer.Ordinal)
            .ToArray();

        return new ModelAnalysis.StatementGraphSnapshot(
            seedFact.TargetKey,
            scopeMode,
            seedFact.MemberId,
            includedFacts.Select(fact => fact.TargetKey).ToArray(),
            BuildEdges(includedFacts));
    }

    private static HashSet<string> CollectMinimalBlock(ModelAnalysis.StatementFact seedFact, IReadOnlyList<ModelAnalysis.StatementFact> bucket)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fact in bucket)
        {
            if (string.Equals(fact.ScopeId, seedFact.ScopeId, StringComparison.Ordinal))
            {
                keys.Add(fact.TargetKey);
            }
        }

        keys.Add(seedFact.TargetKey);
        return keys;
    }

    private static HashSet<string> CollectParentPiercing(ModelAnalysis.StatementFact seedFact, IReadOnlyList<ModelAnalysis.StatementFact> bucket)
    {
        var included = CollectMinimalBlock(seedFact, bucket);
        var includedFacts = bucket.Where(fact => included.Contains(fact.TargetKey)).ToArray();
        var definitions = new HashSet<string>(
            includedFacts.SelectMany(fact => fact.DefinedSymbols).Select(symbol => symbol.SymbolKey),
            StringComparer.Ordinal);

        var scopeParents = bucket
            .Where(fact => !string.IsNullOrEmpty(fact.ScopeId))
            .GroupBy(fact => fact.ScopeId!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(fact => fact.ParentScopeId).FirstOrDefault(parent => !string.IsNullOrEmpty(parent)),
                StringComparer.Ordinal);

        var currentParentScopeId = seedFact.ParentScopeId;
        while (!string.IsNullOrEmpty(currentParentScopeId))
        {
            var unresolvedSymbols = includedFacts
                .SelectMany(fact => fact.UsedSymbols)
                .Where(symbol => !definitions.Contains(symbol.SymbolKey))
                .Select(symbol => symbol.SymbolKey)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (unresolvedSymbols.Length == 0)
            {
                break;
            }

            var parentFacts = bucket
                .Where(fact =>
                    string.Equals(fact.ScopeId, currentParentScopeId, StringComparison.Ordinal) &&
                    fact.SpanStart < seedFact.SpanStart)
                .OrderBy(fact => fact.SpanStart)
                .ToArray();

            var added = false;
            foreach (var symbolKey in unresolvedSymbols)
            {
                var definingFact = parentFacts
                    .Where(fact => fact.DefinedSymbols.Any(symbol => string.Equals(symbol.SymbolKey, symbolKey, StringComparison.Ordinal)))
                    .LastOrDefault();

                if (definingFact == null || !included.Add(definingFact.TargetKey))
                {
                    continue;
                }

                added = true;
            }

            includedFacts = bucket.Where(fact => included.Contains(fact.TargetKey)).ToArray();
            definitions = new HashSet<string>(
                includedFacts.SelectMany(fact => fact.DefinedSymbols).Select(symbol => symbol.SymbolKey),
                StringComparer.Ordinal);

            if (!scopeParents.TryGetValue(currentParentScopeId, out currentParentScopeId) && !added)
            {
                break;
            }
        }

        return included;
    }

    private static IReadOnlyList<ModelAnalysis.StatementDependencyEdge> BuildEdges(IReadOnlyList<ModelAnalysis.StatementFact> facts)
    {
        var edges = new List<ModelAnalysis.StatementDependencyEdge>();

        foreach (var fact in facts)
        {
            foreach (var symbol in fact.DefinedSymbols)
            {
                edges.Add(new ModelAnalysis.StatementDependencyEdge(
                    fact.TargetKey,
                    fact.TargetKey,
                    ModelAnalysis.StatementDependencyKind.Defines,
                    symbol.SymbolKey));
            }

            foreach (var symbol in fact.UsedSymbols)
            {
                edges.Add(new ModelAnalysis.StatementDependencyEdge(
                    fact.TargetKey,
                    fact.TargetKey,
                    ModelAnalysis.StatementDependencyKind.Uses,
                    symbol.SymbolKey));
            }
        }

        for (var index = 1; index < facts.Count; index++)
        {
            edges.Add(new ModelAnalysis.StatementDependencyEdge(
                facts[index - 1].TargetKey,
                facts[index].TargetKey,
                ModelAnalysis.StatementDependencyKind.Precedes));
        }

        return edges;
    }
}



