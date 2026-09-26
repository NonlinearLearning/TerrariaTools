using System.Collections.ObjectModel;
using NLCPG.Builder.Streaming;

namespace NLCPG.Builder;

internal enum DataFlowDiagnosticMode { Off, Coarse, Detailed }

internal sealed record DataFlowDiagnosticRequest(
    DataFlowDiagnosticMode Mode, string RunId, string InputHash);

internal sealed record DataFlowDiagnosticResult(
    string RunId, string InputHash, string Document, string MethodSignature,
    int SpanStart, int SpanEnd, int StableOrder, DataFlowDiagnosticMode Mode,
    long Frequency, long MethodTotalTicks, long QueueWaitTicks, long CommitTicks,
    IReadOnlyDictionary<string, long> PhaseTicks,
    IReadOnlyDictionary<string, long> DetailTicks,
    IReadOnlyDictionary<string, long> Counters,
    IReadOnlyList<CpgEdgeCandidate> PublicationOrder,
    NLCPGDataFlowMethodMetrics Metrics, string ExitReason);

public sealed partial class NLCPGBuilder {
  private readonly List<DataFlowDiagnosticResult> _dataFlowDiagnostics = new();

  internal DataFlowDiagnosticRequest? DataFlowDiagnostics { get; set; }

  internal ReadOnlyCollection<DataFlowDiagnosticResult> LastDataFlowDiagnostics =>
      _dataFlowDiagnostics.AsReadOnly();
}
