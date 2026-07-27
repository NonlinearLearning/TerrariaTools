namespace NLCPG.Builder.Streaming;

/// 片段释放 Roslyn 事实后仍可提交的、不可变的方法局部流边。
internal sealed class LocalFlowCandidateSet
{
  internal LocalFlowCandidateSet(IReadOnlyList<CpgEdgeCandidate> edgeCandidates)
  {
    ArgumentNullException.ThrowIfNull(edgeCandidates);
    EdgeCandidates = edgeCandidates.ToArray();
  }

  internal IReadOnlyList<CpgEdgeCandidate> EdgeCandidates { get; }
}
