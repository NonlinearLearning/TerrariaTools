using System.Collections.ObjectModel;
using System.Diagnostics;

namespace NLCPG.Builder;

internal enum DataFlowPhase {
  UsedFacts, PlanBuild, DefinitionSetup, Fixpoint, CandidateIndexBuild,
  CandidateLoop, ExplicitSources, ReturnBoundary, ResultMaterialization,
}

internal enum DataFlowDetail { TryGetCandidates, DefinitionLookup, FactsMatch, CandidateCollect }

internal enum DataFlowCounter {
  UsedOperationVisits, DirectUsedFacts, UsedChildVisits, UsedChildFactVisits,
  PlanOperationVisits, PlanParameterVisits, PlanOperationNodeVisits, PlanReturnScanVisits,
  PlanIncomingNodeInitializations, PlanOutgoingNodeInitializations,
  PlanIncomingNodeVisits, PlanOutgoingNodeVisits, PlanIncomingEdgeVisits, PlanOutgoingEdgeVisits,
  // PlanNeighborMaterializationVisits 随旧物化链（BuildFlowNeighborsFromCache/SnapshotNeighbors）
  // 一并退役：新构造路径 BuildFlowNeighborCsr 无等价写入点，保留它只会恒为 0 并伪装成"仍在计数"。
  PlanIncomingEdgesRetained, PlanOutgoingEdgesRetained,
  DefinitionOperationVisits, DefinitionFlowVisits, FlowOrdinalVisits,
  DefinitionCount, FlowNodeCount, InitializedSetSlots, InitializedQueueSlots,
  Enqueues, Dequeues, FixpointPredecessorVisits, FixpointSuccessorVisits,
  Transfers, FixpointUnions, SetComparisons,
  IndexSourceDefinitionVisits, IndexAcceptedDefinitions,
  LocationKeys, RootKeys, BaseKeys, BasePathKeys,
  LocationBuildPostings, RootBuildPostings, BaseBuildPostings, BasePathBuildPostings,
  TryGetCandidatesCalls, EmptyLocationReturns, IndexedReturns, ReturnedCandidates,
  LocationQueries, LocationHits, LocationPostings, LocationContains,
  LocationUnreachable, LocationSeenChecks, LocationSeenRejects, LocationMatches,
  RootQueries, RootHits, RootPostings, RootContains,
  RootUnreachable, RootSeenChecks, RootSeenRejects, RootMatches,
  BaseQueries, BaseHits, BasePostings, BaseContains,
  BaseUnreachable, BaseSeenChecks, BaseSeenRejects, BaseMatches,
  BasePathQueries, BasePathHits, BasePathPostings, BasePathContains,
  BasePathUnreachable, BasePathSeenChecks, BasePathSeenRejects, BasePathMatches,
  SeenRejects, TouchedMarks, TouchedClears, MatchesCountHighWater, MatchesCapacityHighWater,
  TouchedCountHighWater, TouchedCapacityHighWater,
  CandidateOperationVisits, CandidatePredecessorVisits, CandidateUnions, UsedFactVisits,
  CandidateZeroFactSkips,
  IndexedDefinitionLookups, IndexedDefinitionMissing, IndexedFactsMatchCalls, IndexedFactsMatchTrue,
  IndexedCollectorCalls, FallbackOrdinals, FallbackDefinitionLookups, FallbackDefinitionMissing,
  FallbackFactsMatchCalls, FallbackFactsMatchTrue, FallbackCollectorCalls,
  FactsMatchCalls, FactsMatchTrue, RawCandidates, UniqueCandidates, DuplicateCandidates,
  CandidateBudgetRejects, CandidateLoopRaw, CandidateLoopUnique,
  ExplicitOperationVisits, ExplicitSourceVisits, ExplicitRaw, ExplicitUnique,
  ReturnScanVisits, ReturnValueVisits, TerminalVisits, ImplicitReturnScanVisits,
  ReturnRaw, ReturnUnique, MaterializedCandidates,
}

// One worker owns a probe. Only the existing ordered reducer publishes its frozen snapshot.
internal sealed class DataFlowMethodProbe {
  private readonly long[] _phases = new long[Enum.GetValues<DataFlowPhase>().Length];
  private readonly long[] _details = new long[Enum.GetValues<DataFlowDetail>().Length];
  private readonly long[] _counters = new long[Enum.GetValues<DataFlowCounter>().Length];
  private long _phaseStart;
  private DataFlowPhase? _phase;

  internal DataFlowMethodProbe(DataFlowDiagnosticMode mode) { Mode = mode; }

  internal DataFlowDiagnosticMode Mode { get; }
  internal DataFlowPhase? Phase => _phase;
  internal long MethodTotalTicks { get; set; }
  internal long QueueWaitTicks { get; set; }
  internal long CommitTicks { get; set; }

  internal void Begin(DataFlowPhase phase) {
    if (Mode == DataFlowDiagnosticMode.Off) {
      return;
    }
    long now = Stopwatch.GetTimestamp();
    End(now);
    _phase = phase;
    _phaseStart = now;
  }

  internal void End(long now) {
    if (_phase.HasValue) {
      _phases[(int)_phase.Value] += now - _phaseStart;
      _phase = null;
    }
  }

  internal void Count(DataFlowCounter counter, long amount = 1) {
    _counters[(int)counter] += amount;
  }

  internal void Maximum(DataFlowCounter counter, long value) {
    _counters[(int)counter] = Math.Max(_counters[(int)counter], value);
  }

  internal void Elapsed(DataFlowDetail detail, long start) {
    _details[(int)detail] += Stopwatch.GetTimestamp() - start;
  }

  internal IReadOnlyDictionary<string, long> PhaseSnapshot() {
    return Snapshot<DataFlowPhase>(_phases);
  }

  internal IReadOnlyDictionary<string, long> DetailSnapshot() {
    return Snapshot<DataFlowDetail>(_details);
  }

  internal IReadOnlyDictionary<string, long> CounterSnapshot() {
    return Snapshot<DataFlowCounter>(_counters);
  }

  private static ReadOnlyDictionary<string, long> Snapshot<T>(long[] values) where T : Enum {
    return new ReadOnlyDictionary<string, long>(Enum.GetValues(typeof(T)).Cast<T>()
        .ToDictionary(key => key.ToString(), key => values[Convert.ToInt32(key)]));
  }
}
