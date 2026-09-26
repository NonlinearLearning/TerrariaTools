using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Model;
using RoslynPrototype.Tests.TestCodeSet.Cpg;
using Xunit;

namespace RoslynPrototype.Tests;

// P01 candidate-bucket pruning.
//
// Two independent evidence layers, deliberately kept apart:
//
//  1. Frozen pre-change oracle. The three DataFlow fixtures are built through the real
//     production path (BuildFromSource) and compared against the complete normalized graph and
//     the raw/unique candidate counts and exit reason that the PRE-change binary produced
//     (Build/DataFlowTailMeasurement/p01-pre-r2). This oracle is not regenerated to follow a
//     new implementation.
//
//  2. Synthetic boundary facts through the existing DefinitionFact reflection seam (the same
//     seam DataFlowDiagnosticFallbackTests uses). These cover null / empty base keys, empty
//     location keys, index reuse across calls and the U+001F separator-ambiguity input that the
//     BasePath removal proof depends on. No index or FactsMatch algorithm is copied into the
//     test, and no public entry point is added for testing.
//
// The reaching sets here are built with Union over one-element slots, NOT with
// ApplyDefinitionTransfer: the transfer path deliberately removes conflicting definitions, so
// it cannot express an arbitrary set. Union preserves the ascending, duplicate-free invariant.
public sealed class DataFlowCandidateScanPruningTests {
  // Frozen PRE-change oracle, Build/DataFlowTailMeasurement/p01-pre-r3 (2026-09-25). The
  // document names are the ones the measurement host used ("measurement/" + fixture + ".cs"),
  // because the document path is part of the graph identity.
  //
  // Deliberately NOT DataFlowGraphSnapshot + SHA256: Capture() serializes the *resolved absolute*
  // FullName, so its hash depends on the process working directory (the host runs from the repo
  // root, the test host from Build/test/Debug/net10.0). GraphSnapshotVersion is graph-owned, path
  // independent, and is the same identity the measurement host records per sample. The complete
  // node/edge/dataflow counts are asserted alongside it, plus the publication order hash, which
  // is absolute-path free.
  private const string SparseSnapshotVersion =
      "D0537910C3B8EEACCB618C20496C77A8904548C06D43124C8BA7DB5DAF3F8C78";
  private const string SparsePublicationHash =
      "E19B748F6C755A1C631E2B581B8D46408E3199D935D638EF9D48DC5CC06C12E6";
  private const string CollisionSnapshotVersion =
      "5071C7F2BB489B3B02D5604148D4B5F3604AB21B6EBD94701FA658BFE349F224";
  private const string CollisionPublicationHash =
      "E694D74902E18CACE289FD8F22C725BEAF6E6B1FB24E579B86DC0103CB8DE2C5";
  private const string JoinLoopSnapshotVersion =
      "139EC2649B06985B048F2848E8F45191B7FD1BFC679EDD0DB1F61A58D1081C6E";
  private const string JoinLoopPublicationHash =
      "7BFA1965AF6D49477760A4588164033B4D5EA986A1A3C07AE6BC2D9177AF0427";

  [Fact]
  public void BuildFromSource_Collision_MatchesFrozenPreChangeOracle() {
    var result = BuildWithDiagnostics(DataFlowMeasurementSources.Collision(),
        "measurement/Collision.cs");
    Assert.Equal(CollisionSnapshotVersion, result.Graph.GraphSnapshotVersion);
    Assert.Equal(CollisionPublicationHash, PublicationHash(result.Diagnostic));
    Assert.Equal(3759, result.Graph.Nodes.Count());
    Assert.Equal(10021, result.Graph.Edges.Count());
    Assert.Equal(262, result.Graph.Edges.Count(edge => edge.Kind == NLCPGEdgeKind.DataFlow));
    Assert.Equal(262, result.Diagnostic.Metrics.UniqueCandidateCount);
    Assert.Equal(424, result.Diagnostic.Metrics.RawCandidateCount);
    Assert.Equal("Complete", result.Diagnostic.ExitReason);
  }

  [Fact]
  public void BuildFromSource_SparseAndJoinLoop_MatchFrozenPreChangeOracle() {
    var sparse = BuildWithDiagnostics(DataFlowMeasurementSources.Sparse(), "measurement/Sparse.cs");
    Assert.Equal(SparseSnapshotVersion, sparse.Graph.GraphSnapshotVersion);
    Assert.Equal(SparsePublicationHash, PublicationHash(sparse.Diagnostic));
    Assert.Equal(2218, sparse.Graph.Nodes.Count());
    Assert.Equal(132, sparse.Graph.Edges.Count(edge => edge.Kind == NLCPGEdgeKind.DataFlow));
    Assert.Equal(132, sparse.Diagnostic.Metrics.UniqueCandidateCount);

    var joinLoop = BuildWithDiagnostics(DataFlowMeasurementSources.JoinLoop(),
        "measurement/JoinLoop.cs");
    Assert.Equal(JoinLoopSnapshotVersion, joinLoop.Graph.GraphSnapshotVersion);
    Assert.Equal(JoinLoopPublicationHash, PublicationHash(joinLoop.Diagnostic));
    Assert.Equal(1383, joinLoop.Graph.Nodes.Count());
    Assert.Equal(68, joinLoop.Graph.Edges.Count(edge => edge.Kind == NLCPGEdgeKind.DataFlow));
    Assert.Equal(68, joinLoop.Diagnostic.Metrics.UniqueCandidateCount);
  }

  // T2 structural target: a used fact that carries a base key resolves through Root, so the Base
  // bucket must not be scanned at all. Red before the T2 patch (BaseQueries and BasePostings are
  // 1 and 2), green after.
  [Fact]
  public void TryGetCandidates_WithNonNullBaseKey_DoesNotScanBaseBucket() {
    var probe = new DataFlowMethodProbe(DataFlowDiagnosticMode.Detailed);
    var index = CreateIndex(probe,
        ("L0", "b", "f"),
        ("L1", "b", "f"));
    var used = Fact("Lx", "b", "f");
    object sets = CreateReachingSet((0, "L0", "b", "f"), (1, "L1", "b", "f"));

    Invoke(index, used, sets, out var candidates);

    Assert.Equal(1, probe.CounterSnapshot()["RootQueries"]);
    Assert.Equal(0, probe.CounterSnapshot()["BaseQueries"]);
    Assert.Equal(0, probe.CounterSnapshot()["BasePostings"]);
    Assert.Equal(2, candidates.Count);
  }

  // T3 structural target: the BasePath bucket cannot add a definition that Location or Root did
  // not already handle, so neither its scan nor its build postings may remain. Red before T3:
  // BasePath is still scanned and still contributes a definition-level candidate here.
  [Fact]
  public void TryGetCandidates_WithBaseAndPathKeys_DoesNotScanOrBuildBasePathBucket() {
    var probe = new DataFlowMethodProbe(DataFlowDiagnosticMode.Detailed);
    var index = CreateIndex(probe,
        ("L0", "b", "c"),
        ("L1", "b", "c"));
    var used = Fact("Lx", "b", "c");
    object sets = CreateReachingSet((0, "L0", "b", "c"), (1, "L1", "b", "c"));

    Invoke(index, used, sets, out var candidates);

    Assert.Equal(0, probe.CounterSnapshot()["BasePathQueries"]);
    Assert.Equal(0, probe.CounterSnapshot()["BasePathPostings"]);
    Assert.Equal(0, probe.CounterSnapshot()["BasePathBuildPostings"]);
    Assert.Equal(0, probe.CounterSnapshot()["BasePathKeys"]);
    // Both definitions carry the used base key and are reachable through Root alone.
    Assert.Equal(2, candidates.Count);
    Assert.Equal(2, probe.CounterSnapshot()["RootMatches"]);
  }

  // The null base key branch must keep querying Base on the location key: this is the
  // "whole use matches a component part" case (IsContainerMatch) that Root cannot express.
  [Fact]
  public void TryGetCandidates_WithNullBaseKey_KeepsBaseQueryOnLocationKey() {
    var probe = new DataFlowMethodProbe(DataFlowDiagnosticMode.Detailed);
    var index = CreateIndex(probe, ("L1", "loc", "field"));
    var used = Fact("loc", null, "local");
    object sets = CreateReachingSet((0, "L1", "loc", "field"));

    Invoke(index, used, sets, out var candidates);

    // Root is not queried because the root key is null; Base still is, and it contributes.
    Assert.Equal(0, probe.CounterSnapshot()["RootQueries"]);
    Assert.Equal(1, probe.CounterSnapshot()["BaseQueries"]);
    Assert.Equal(1, probe.CounterSnapshot()["BaseMatches"]);
    Assert.Single(candidates);
  }

  // Empty base key: the old code scanned neither Root nor Base for it. Keep that behaviour.
  [Fact]
  public void TryGetCandidates_WithEmptyBaseKey_ScansNeitherRootNorBase() {
    var probe = new DataFlowMethodProbe(DataFlowDiagnosticMode.Detailed);
    var index = CreateIndex(probe, ("L1", "b", "field"));
    var used = Fact("Lx", "", "field");
    object sets = CreateReachingSet((0, "L1", "b", "field"));

    Invoke(index, used, sets, out var candidates);

    Assert.Equal(0, probe.CounterSnapshot()["RootQueries"]);
    Assert.Equal(0, probe.CounterSnapshot()["BaseQueries"]);
    Assert.Empty(candidates);
  }

  // Empty location key keeps its early-return contract: no bucket is scanned at all.
  [Fact]
  public void TryGetCandidates_WithEmptyLocationKey_KeepsEarlyReturnContract() {
    var probe = new DataFlowMethodProbe(DataFlowDiagnosticMode.Detailed);
    var index = CreateIndex(probe, ("L1", "b", "field"));
    var used = Fact("", "b", "field");
    object sets = CreateReachingSet((0, "L1", "b", "field"));

    bool indexed = Invoke(index, used, sets, out var candidates);

    Assert.False(indexed);
    Assert.Equal(1, probe.CounterSnapshot()["EmptyLocationReturns"]);
    Assert.Equal(0, probe.CounterSnapshot()["LocationQueries"]);
    Assert.Equal(0, probe.CounterSnapshot()["RootQueries"]);
    Assert.Equal(0, probe.CounterSnapshot()["BaseQueries"]);
    Assert.Empty(candidates);
  }

  // Same index instance reused across calls must not leak the previous call's _seen marks and
  // must not return the previous call's matches. The two definitions deliberately carry
  // different base keys so each Root bucket holds exactly one of them.
  [Fact]
  public void TryGetCandidates_ReusedIndexAcrossCalls_DoesNotLeakSeenMarksOrMatches() {
    var probe = new DataFlowMethodProbe(DataFlowDiagnosticMode.Detailed);
    var index = CreateIndex(probe,
        ("L1", "b1", "f"),
        ("L2", "b2", "f"));
    object sets = CreateReachingSet((0, "L1", "b1", "f"), (1, "L2", "b2", "f"));

    Invoke(index, Fact("L1", "b1", "f"), sets, out var first);
    Invoke(index, Fact("L2", "b2", "f"), sets, out var second);
    Invoke(index, Fact("L1", "b1", "f"), sets, out var third);

    Assert.Single(first);
    Assert.Single(second);
    Assert.Single(third);
    // The already-inserted Location fact owns definition 0, so by the time Root is scanned it is
    // already marked seen and cannot contribute again; Root must therefore add nothing. What
    // this test pins is that three calls each return exactly one candidate and that the Base
    // bucket is never consulted when BaseKey is non-null (the T2 gate).
    Assert.Equal(3, probe.CounterSnapshot()["LocationMatches"]);
    Assert.Equal(3, probe.CounterSnapshot()["RootQueries"]);
    Assert.Equal(0, probe.CounterSnapshot()["RootMatches"]);
    Assert.Equal(0, probe.CounterSnapshot()["BaseQueries"]);
    Assert.Equal(0, probe.CounterSnapshot()["BaseMatches"]);
  }

  // U+001F separator ambiguity: two definitions with different base keys compose to the SAME
  // BasePath composite key ("a\u001Fb" + "\u001F" + "c" and "a" + "\u001F" + "b\u001Fc" both
  // yield "a\u001Fb\u001Fc"). Only the definition whose base key actually equals the used
  // fact's base key is reachable through Root, and that one is enough: downstream FactsMatch
  // rejects the separator-ambiguous definition anyway because its base key is neither the used
  // location key nor its base key. This is the boundary the BasePath removal proof relies on,
  // and it is the single case where removing BasePath shrinks the index's internal candidate
  // list (2 -> 1); the plan explicitly permits that while requiring the graph and the collector
  // publication order to stay identical.
  [Fact]
  public void TryGetCandidates_WithSeparatorAmbiguousCompositeKey_MatchesOnlyRootReachableDefinition() {
    var probe = new DataFlowMethodProbe(DataFlowDiagnosticMode.Detailed);
    var index = CreateIndex(probe,
        ("L1", "a\u001Fb", "c"),
        ("L2", "a", "b\u001Fc"));
    var used = Fact("Lx", "a\u001Fb", "c");
    object sets = CreateReachingSet(
        (0, "L1", "a\u001Fb", "c"),
        (1, "L2", "a", "b\u001Fc"));

    Invoke(index, used, sets, out var candidates);

    // Pre-T3 this returns both definitions; post-T3 only the Root-reachable one.
    Assert.Single(candidates);
    Assert.Equal(1, probe.CounterSnapshot()["RootMatches"]);
    Assert.Equal(0, probe.CounterSnapshot()["BaseMatches"]);
  }

  // Same base key, different path keys: both definitions are reachable through Root, and the
  // visible result must not depend on the BasePath bucket.
  [Fact]
  public void TryGetCandidates_SameBaseDifferentPaths_ReturnsBothThroughRoot() {
    var probe = new DataFlowMethodProbe(DataFlowDiagnosticMode.Detailed);
    var index = CreateIndex(probe,
        ("L1", "b", "p1"),
        ("L2", "b", "p2"));
    var used = Fact("Lx", "b", "p1");
    object sets = CreateReachingSet((0, "L1", "b", "p1"), (1, "L2", "b", "p2"));

    Invoke(index, used, sets, out var candidates);

    Assert.Equal(2, candidates.Count);
    Assert.Equal(2, probe.CounterSnapshot()["RootMatches"]);
    Assert.Equal(0, probe.CounterSnapshot()["BasePathMatches"]);
  }

  // Multiple methods in one build must keep counters method-local, and they must not accumulate
  // across repeated BuildFromSource calls on a reused builder.
  [Fact]
  public void BuildFromSource_ReusedBuilderAndTwoMethods_KeepCountersMethodLocal() {
    var builder = CreateBuilder();
    string doubled = DataFlowMeasurementSources.Sparse().Replace("static int Run", "static int First") +
        DataFlowMeasurementSources.Sparse().Replace("class Sample", "class Other");
    builder.BuildFromSource(doubled, "two-methods.cs");
    Assert.Equal(2, builder.LastDataFlowDiagnostics.Count);
    Assert.Equal(builder.LastDataFlowDiagnostics[0].Counters, builder.LastDataFlowDiagnostics[1].Counters);

    builder.BuildFromSource(DataFlowMeasurementSources.Sparse(), "sparse.cs");
    var first = Assert.Single(builder.LastDataFlowDiagnostics);
    builder.BuildFromSource(DataFlowMeasurementSources.Sparse(), "sparse.cs");
    var second = Assert.Single(builder.LastDataFlowDiagnostics);
    Assert.Equal(first.Counters, second.Counters);
  }

  private static (NLCPGGraph Graph, DataFlowDiagnosticResult Diagnostic) BuildWithDiagnostics(
      string source, string document) {
    var builder = CreateBuilder();
    var graph = builder.BuildFromSource(source, document);
    return (graph, Assert.Single(builder.LastDataFlowDiagnostics));
  }

  private static NLCPGBuilder CreateBuilder() {
    return new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with {
      MaxDegreeOfParallelism = 1, LargeFileLineThreshold = 1, LargeFileMethodThreshold = 1,
      LargeMethodLineSpanThreshold = 1, SyntaxLargeFileLineThreshold = 1,
      DataFlowOptions = NLCPGDataFlowOptions.Unbounded,
    }) { DataFlowDiagnostics = new(DataFlowDiagnosticMode.Detailed, "pruning", "fixture") };
  }

  // Same serialization the measurement host uses for PublicationHash, so the frozen publication
  // identity can be compared without re-deriving the host's JSON options.
  private static string PublicationHash(DataFlowDiagnosticResult diagnostic) {
    var options = new JsonSerializerOptions {
      Converters = { new JsonStringEnumConverter() },
    };
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(diagnostic.PublicationOrder, options))));
  }

  private const BindingFlags AllInstance = BindingFlags.Instance |
      BindingFlags.Public | BindingFlags.NonPublic;

  private static Type Nested(string name) {
    return typeof(NLCPGBuilder).GetNestedType(name, BindingFlags.NonPublic)!;
  }

  private static object Fact(string location, string? baseKey, string? pathKey) {
    return Activator.CreateInstance(Nested("DefinitionFact"), AllInstance, null,
        [location, baseKey, "fact", pathKey], null)!;
  }

  // Builds a real DefinitionFactIndex over synthetic facts. Definition ordinal i maps to
  // definitions[i], so the caller controls ordinal -> fact mapping directly.
  private static object CreateIndex(DataFlowMethodProbe probe,
      params (string Location, string? Base, string? Path)[] definitions) {
    var factsType = typeof(Dictionary<,>).MakeGenericType(typeof(NLCPGNode), Nested("DefinitionFact"));
    object facts = Activator.CreateInstance(factsType)!;
    var ordinals = new Dictionary<NLCPGNode, int>();
    var nodes = new List<NLCPGNode>();
    var definitionOrdinals = new int[definitions.Length];
    for (int index = 0; index < definitions.Length; index++) {
      var (location, baseKey, pathKey) = definitions[index];
      NLCPGNode node = new(NLCPGNodeKind.Operation, NameId: (uint)(index + 1));
      object fact = Fact(location, baseKey, pathKey);
      factsType.GetMethod("Add")!.Invoke(facts, [node, fact]);
      ordinals[node] = index;
      nodes.Add(node);
      definitionOrdinals[index] = index;
    }

    return Activator.CreateInstance(Nested("DefinitionFactIndex"), AllInstance, null,
        [facts, ordinals, definitionOrdinals, nodes, probe], null)!;
  }

  // A real SparseSetStore holding exactly the listed definition ordinals in slot 0. Each fact is
  // staged in its own one-element slot (an empty slot plus ApplyDefinitionTransfer writes that
  // ordinal), then merged into slot 0 with Union, which keeps the ascending, duplicate-free
  // invariant without the conflict removal that ApplyDefinitionTransfer applies.
  private static object CreateReachingSet(
      params (int Ordinal, string Location, string? Base, string? Path)[] reaching) {
    int slots = reaching.Length + 1;
    object sets = Activator.CreateInstance(Nested("SparseSetStore"), AllInstance, null,
        [slots + 2], null)!;
    var storeType = Nested("SparseSetStore");
    var listType = typeof(List<>).MakeGenericType(Nested("DefinitionFact"));
    var transfer = storeType.GetMethod("ApplyDefinitionTransfer", AllInstance)!;
    var union = storeType.GetMethod("Union", AllInstance)!;
    for (int index = 0; index < reaching.Length; index++) {
      var entry = reaching[index];
      object facts = Activator.CreateInstance(listType)!;
      listType.GetMethod("Add")!.Invoke(facts, [Fact(entry.Location, entry.Base, entry.Path)]);
      transfer.Invoke(sets, [index + 1, entry.Ordinal, Fact(entry.Location, entry.Base, entry.Path), facts]);
      union.Invoke(sets, [0, index + 1]);
    }

    return sets;
  }

  private static bool Invoke(object index, object usedFact, object sets,
      out IReadOnlyList<NLCPGNode> candidates) {
    object?[] arguments = [usedFact, sets, 0, null];
    bool indexed = (bool)Nested("DefinitionFactIndex")
        .GetMethod("TryGetCandidates", AllInstance)!.Invoke(index, arguments)!;
    candidates = arguments[3] is null
        ? Array.Empty<NLCPGNode>()
        : (IReadOnlyList<NLCPGNode>)arguments[3]!;
    return indexed;
  }
}
