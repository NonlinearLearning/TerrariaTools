using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NLCPG.Model;

namespace NLCPG.Builder;

public sealed partial class NLCPGBuilder {
  private static IEnumerable<T> ObserveVisits<T>(
      IEnumerable<T> source, DataFlowMethodProbe? detail, DataFlowCounter counter) {
    return detail is null ? source : EnumerateObserved(source, detail, counter);
  }

  private static IEnumerable<T> EnumerateObserved<T>(
      IEnumerable<T> source, DataFlowMethodProbe detail, DataFlowCounter counter) {
    foreach (T item in source) {
      detail.Count(counter);
      yield return item;
    }
  }

  private static bool ContainsMeasuredExplicitReturn(
      IBlockOperation block, DataFlowMethodProbe? detail) {
    return ObserveVisits(block.Descendants(), detail, DataFlowCounter.ImplicitReturnScanVisits)
        .OfType<IReturnOperation>().Any();
  }

  private static bool TryGetMeasuredDefinition(
      Dictionary<NLCPGNode, DefinitionFact> facts, NLCPGNode node,
      DataFlowMethodProbe? detail, bool fallback, out DefinitionFact fact) {
    long start = detail is null ? 0 : Stopwatch.GetTimestamp();
    bool found = facts.TryGetValue(node, out fact);
    detail?.Elapsed(DataFlowDetail.DefinitionLookup, start);
    detail?.Count(fallback ? DataFlowCounter.FallbackDefinitionLookups
        : DataFlowCounter.IndexedDefinitionLookups);
    if (!found) {
      detail?.Count(fallback ? DataFlowCounter.FallbackDefinitionMissing
          : DataFlowCounter.IndexedDefinitionMissing);
    }
    return found;
  }

  private static bool MeasuredFactsMatch(
      DefinitionFact reaching, DefinitionFact used, DataFlowMethodProbe? detail, bool fallback) {
    long start = detail is null ? 0 : Stopwatch.GetTimestamp();
    bool matches = FactsMatch(reaching, used);
    detail?.Elapsed(DataFlowDetail.FactsMatch, start);
    detail?.Count(DataFlowCounter.FactsMatchCalls);
    detail?.Count(fallback ? DataFlowCounter.FallbackFactsMatchCalls
        : DataFlowCounter.IndexedFactsMatchCalls);
    if (matches) {
      detail?.Count(DataFlowCounter.FactsMatchTrue);
      detail?.Count(fallback ? DataFlowCounter.FallbackFactsMatchTrue
          : DataFlowCounter.IndexedFactsMatchTrue);
    }
    return matches;
  }
}
