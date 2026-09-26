namespace NLCPG.Builder.Concurrency;

public sealed record CpgFragmentMetrics
{
    public CpgFragmentMetrics(
      long CollectionElapsedMilliseconds,
      long LocalSolveElapsedMilliseconds,
      int EstimatedNodeCount,
      int EstimatedEdgeCount,
      int ActualNodeCount,
      int ActualEdgeCount,
      int FragmentBytes,
      string? TruncationReason = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(CollectionElapsedMilliseconds);
        ArgumentOutOfRangeException.ThrowIfNegative(LocalSolveElapsedMilliseconds);
        ArgumentOutOfRangeException.ThrowIfNegative(EstimatedNodeCount);
        ArgumentOutOfRangeException.ThrowIfNegative(EstimatedEdgeCount);
        ArgumentOutOfRangeException.ThrowIfNegative(ActualNodeCount);
        ArgumentOutOfRangeException.ThrowIfNegative(ActualEdgeCount);
        ArgumentOutOfRangeException.ThrowIfNegative(FragmentBytes);
        this.CollectionElapsedMilliseconds = CollectionElapsedMilliseconds;
        this.LocalSolveElapsedMilliseconds = LocalSolveElapsedMilliseconds;
        this.EstimatedNodeCount = EstimatedNodeCount;
        this.EstimatedEdgeCount = EstimatedEdgeCount;
        this.ActualNodeCount = ActualNodeCount;
        this.ActualEdgeCount = ActualEdgeCount;
        this.FragmentBytes = FragmentBytes;
        this.TruncationReason = TruncationReason;
    }

    public static CpgFragmentMetrics Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);

    public long CollectionElapsedMilliseconds { get; }

    public long LocalSolveElapsedMilliseconds { get; }

    public int EstimatedNodeCount { get; }

    public int EstimatedEdgeCount { get; }

    public int ActualNodeCount { get; }

    public int ActualEdgeCount { get; }

    public int FragmentBytes { get; }

    public string? TruncationReason { get; }
}

public sealed record CpgMethodSummary(
  string? MethodSymbolKey,
  int SpanStart,
  int SpanEnd);

public enum CpgBoundaryReferenceKind
{
    Local,
    External,
    Unknown,
}

public sealed record CpgBoundaryReference
{
    public CpgBoundaryReference(
      NLCPG.Model.StableNodeAnchor Anchor,
      string Reference,
      CpgBoundaryReferenceKind Kind,
      bool IsAvailable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Reference);
        if (SpanAnchorIsInvalid(Anchor))
        {
            throw new ArgumentException("Boundary references require a valid stable anchor.", nameof(Anchor));
        }

        this.Anchor = Anchor;
        this.Reference = Reference;
        this.Kind = Kind;
        this.IsAvailable = IsAvailable;
    }

    public NLCPG.Model.StableNodeAnchor Anchor { get; }

    public string Reference { get; }

    public CpgBoundaryReferenceKind Kind { get; }

    public bool IsAvailable { get; }

    private static bool SpanAnchorIsInvalid(NLCPG.Model.StableNodeAnchor anchor)
    {
        return anchor.SpanStart < 0 || anchor.SpanEnd < anchor.SpanStart;
    }
}

public sealed record CpgDiagnostic
{
    public CpgDiagnostic(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Code = code;
        Message = message;
    }

    public string Code { get; }

    public string Message { get; }
}
