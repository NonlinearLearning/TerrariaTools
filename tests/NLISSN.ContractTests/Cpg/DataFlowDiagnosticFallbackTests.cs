using System.Reflection;
using NLCPG.Builder;
using NLCPG.Model;
using Xunit;

namespace RoslynPrototype.Tests;

// Correctness-only synthetic facts use the existing DefinitionFact reflection seam
// (NLCPGNodeIdContractTests). These calls are never included in performance samples.
public sealed class DataFlowDiagnosticFallbackTests {
  [Fact]
  public void TryGetCandidates_EmptyLocation_ReportsFallbackWithoutInventingPostings() {
    Type factType = Nested("DefinitionFact");
    object facts = Activator.CreateInstance(typeof(Dictionary<,>)
        .MakeGenericType(typeof(NLCPGNode), factType))!;
    var probe = new DataFlowMethodProbe(DataFlowDiagnosticMode.Detailed);
    object index = Activator.CreateInstance(Nested("DefinitionFactIndex"), AllInstance,
        null, [facts, new Dictionary<NLCPGNode, int>(), Array.Empty<int>(),
          Array.Empty<NLCPGNode>(), probe], null)!;
    object sets = Activator.CreateInstance(Nested("SparseSetStore"), AllInstance,
        null, [1], null)!;
    object emptyFact = Fact("", null, "unknown");
    object?[] arguments = [emptyFact, sets, 0, null];

    bool indexed = (bool)Nested("DefinitionFactIndex")
        .GetMethod("TryGetCandidates", AllInstance)!.Invoke(index, arguments)!;

    Assert.False(indexed);
    Assert.Equal(1, probe.CounterSnapshot()["EmptyLocationReturns"]);
    Assert.Equal(0, probe.CounterSnapshot()["LocationPostings"]);
    Assert.Empty((IEnumerable<NLCPGNode>)arguments[3]!);
  }

  [Fact]
  public void FactsMatch_FailedThenSuccessfulActualCalls_CountsOnlyActualOutcomes() {
    var probe = new DataFlowMethodProbe(DataFlowDiagnosticMode.Detailed);
    MethodInfo match = typeof(NLCPGBuilder).GetMethod("MeasuredFactsMatch",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    object left = Fact("left", null, "local");
    object right = Fact("right", null, "local");

    Assert.False((bool)match.Invoke(null, [left, right, probe, true])!);
    Assert.True((bool)match.Invoke(null, [left, left, probe, true])!);

    Assert.Equal(2, probe.CounterSnapshot()["FactsMatchCalls"]);
    Assert.Equal(1, probe.CounterSnapshot()["FactsMatchTrue"]);
    Assert.Equal(2, probe.CounterSnapshot()["FallbackFactsMatchCalls"]);
    Assert.Equal(0, probe.CounterSnapshot()["FallbackCollectorCalls"]);
  }

  private const BindingFlags AllInstance = BindingFlags.Instance |
      BindingFlags.Public | BindingFlags.NonPublic;

  private static Type Nested(string name) {
    return typeof(NLCPGBuilder).GetNestedType(name, BindingFlags.NonPublic)!;
  }

  private static object Fact(string location, string? baseKey, string category) {
    return Activator.CreateInstance(Nested("DefinitionFact"), AllInstance, null,
        [location, baseKey, category, null], null)!;
  }
}
