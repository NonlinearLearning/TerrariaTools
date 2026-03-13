namespace TerrariaTools.Dome.Analysis.Roslyn;

using TerrariaTools.Dome.Core;

/// <summary>
/// 基于语句事实索引派生局部语句依赖图快照。
/// </summary>
public sealed class StatementAnalysisService : IStatementAnalysisService
{
    private readonly StatementFactsIndex _index;

    /// <summary>
    /// 初始化语句分析服务。
    /// </summary>
    /// <param name="index">语句事实索引。</param>
    public StatementAnalysisService(StatementFactsIndex index)
    {
        _index = index;
    }

    /// <summary>
    /// 分析并生成语句依赖图快照。
    /// </summary>
    /// <param name="seedTarget">种子目标。</param>
    /// <param name="scopeMode">范围模式。</param>
    /// <returns>语句依赖图快照。</returns>
    public StatementGraphSnapshot Analyze(PlanTarget seedTarget, StatementScopeMode scopeMode)
    {
        if (seedTarget.TargetKind != TargetKind.Statement)
        {
            throw new ArgumentException("Statement analysis requires a statement target.", nameof(seedTarget));
        }

        if (!_index.FactsByMemberId.TryGetValue(seedTarget.MemberId.Value, out var bucket))
        {
            throw new InvalidOperationException($"No statement facts found for member '{seedTarget.MemberId.Value}'.");
        }

        var orderedBucket = bucket
            .OrderBy(fact => fact.SpanStart)
            .ThenBy(fact => fact.TargetKey, StringComparer.Ordinal)
            .ToArray();
        var factsByTargetKey = orderedBucket.ToDictionary(fact => fact.TargetKey, StringComparer.Ordinal);
        if (!factsByTargetKey.TryGetValue(seedTarget.TargetKey, out var seedFact))
        {
            throw new InvalidOperationException($"Statement target '{seedTarget.TargetKey}' was not found in statement facts.");
        }

        var includedTargetKeys = scopeMode switch
        {
            StatementScopeMode.MinimalBlock => CollectMinimalBlock(seedFact, orderedBucket),
            StatementScopeMode.ParentBlockPiercing => CollectParentPiercing(seedFact, orderedBucket),
            _ => throw new ArgumentOutOfRangeException(nameof(scopeMode), scopeMode, "Unsupported statement scope mode.")
        };

        var includedFacts = includedTargetKeys
            .Select(key => factsByTargetKey[key])
            .OrderBy(fact => fact.SpanStart)
            .ThenBy(fact => fact.TargetKey, StringComparer.Ordinal)
            .ToArray();

        return new StatementGraphSnapshot(
            seedFact.TargetKey,
            scopeMode,
            seedFact.MemberId,
            includedFacts.Select(fact => fact.TargetKey).ToArray(),
            BuildEdges(includedFacts));
    }

    /// <summary>
    /// 收集最小块范围内的语句。
    /// </summary>
    private static HashSet<string> CollectMinimalBlock(StatementFact seedFact, IReadOnlyList<StatementFact> bucket)
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

    /// <summary>
    /// 收集穿透父级块范围内的语句（用于处理变量定义和使用）。
    /// </summary>
    private static HashSet<string> CollectParentPiercing(StatementFact seedFact, IReadOnlyList<StatementFact> bucket)
    {
        var included = CollectMinimalBlock(seedFact, bucket);
        var includedFacts = bucket.Where(fact => included.Contains(fact.TargetKey)).ToArray();
        var definitions = new HashSet<string>(
            includedFacts.SelectMany(fact => fact.DefinesSymbols).Select(symbol => symbol.SymbolKey),
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
                .SelectMany(fact => fact.UsesSymbols)
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
                    .Where(fact => fact.DefinesSymbols.Any(symbol => string.Equals(symbol.SymbolKey, symbolKey, StringComparison.Ordinal)))
                    .LastOrDefault();

                if (definingFact == null || !included.Add(definingFact.TargetKey))
                {
                    continue;
                }

                added = true;
            }

            includedFacts = bucket.Where(fact => included.Contains(fact.TargetKey)).ToArray();
            definitions = new HashSet<string>(
                includedFacts.SelectMany(fact => fact.DefinesSymbols).Select(symbol => symbol.SymbolKey),
                StringComparer.Ordinal);

            if (!scopeParents.TryGetValue(currentParentScopeId, out currentParentScopeId) && !added)
            {
                break;
            }
        }

        return included;
    }

    /// <summary>
    /// 构建语句依赖边（定义、使用、顺序）。
    /// </summary>
    private static IReadOnlyList<StatementDependencyEdge> BuildEdges(IReadOnlyList<StatementFact> facts)
    {
        var edges = new List<StatementDependencyEdge>();

        foreach (var fact in facts)
        {
            foreach (var symbol in fact.DefinesSymbols)
            {
                edges.Add(new StatementDependencyEdge(
                    fact.TargetKey,
                    fact.TargetKey,
                    StatementDependencyKind.Defines,
                    symbol.SymbolKey));
            }

            foreach (var symbol in fact.UsesSymbols)
            {
                edges.Add(new StatementDependencyEdge(
                    fact.TargetKey,
                    fact.TargetKey,
                    StatementDependencyKind.Uses,
                    symbol.SymbolKey));
            }
        }

        for (var index = 1; index < facts.Count; index++)
        {
            edges.Add(new StatementDependencyEdge(
                facts[index - 1].TargetKey,
                facts[index].TargetKey,
                StatementDependencyKind.Precedes));
        }

        return edges;
    }
}
