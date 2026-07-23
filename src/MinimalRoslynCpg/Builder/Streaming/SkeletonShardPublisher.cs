using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using MinimalRoslynCpg.Model;
using MinimalRoslynCpg.Persistence;

namespace MinimalRoslynCpg.Builder.Streaming;

/// <summary>
/// Stages the immutable file skeleton and method-boundary shards before operation work starts.
/// The underlying build remains invisible until <see cref="CompleteAsync"/> succeeds.
/// </summary>
internal sealed class SkeletonShardPublisher : IAsyncDisposable
{
  private readonly CpgShardBuildSession _session;
  private readonly CpgPersistenceOptions _options;
  private readonly CpgFileKey _file;
  private readonly FragmentOwnershipIndex _ownership;
  private readonly IReadOnlyList<CpgFragmentOwnership> _fragments;
  private readonly HashSet<NodeId> _publishedNodeIds = new();
  private readonly List<int> _publishedOrders = new();
  private readonly List<string> _publishedKinds = new();
  private readonly Dictionary<NodeId, CpgShardLookup> _primaryLookupByNodeId = new();
  private readonly Dictionary<BoundaryBucket, List<CpgFrozenBoundaryEdge>> _boundaryBatches = new();
  private readonly Dictionary<BoundaryBucket, int> _boundaryShardOrdinals = new();
  private int _bufferedBoundaryEdgeCount;
  private bool _completed;

  private SkeletonShardPublisher(
    CpgShardBuildSession session,
    CpgPersistenceOptions options,
    CpgFileKey file,
    IReadOnlyList<CpgFragmentOwnership> fragments)
  {
    _session = session;
    _options = options;
    _file = file;
    _fragments = fragments;
    _ownership = new FragmentOwnershipIndex(fragments);
  }

  internal static async Task<SkeletonShardPublisher> BeginAsync(
    CpgPersistenceOptions options,
    RoslynCpgBuildContext context,
    CancellationToken cancellationToken)
  {
    var session = await CpgShardBuildSession.BeginAsync(options, cancellationToken);
    try
    {
      session.Store.DeleteStaleTemporaryFiles();
      var inputDirectory = Path.GetDirectoryName(context.FilePath);
      var projectRoot = Path.GetFullPath(string.IsNullOrEmpty(inputDirectory) ? "." : inputDirectory);
      var file = new CpgFileKey(projectRoot, context.FilePath, Hash(context.Source));
      var fragments = FindFragments(context.Root)
        .Select((fragment, order) => new CpgFragmentOwnership(
          fragment.GetType().Name,
          fragment.SpanStart,
          fragment.Span.End,
          order))
        .ToArray();
      var publisher = new SkeletonShardPublisher(session, options, file, fragments);
      await publisher.PublishInitialAsync(context, cancellationToken);
      return publisher;
    }
    catch
    {
      await session.DisposeAsync();
      throw;
    }
  }

  internal async Task<CpgShardBuildResult> CompleteAsync(
    RoslynCpgBuildContext context,
    CancellationToken cancellationToken)
  {
    var nodeOwnership = FragmentNodeOwnershipIndex.Create(context.Graph.Nodes, _ownership);
    foreach (var fragment in _fragments)
    {
      var nodeIds = nodeOwnership.GetNodeIds(fragment)
        .Where(nodeId => !_publishedNodeIds.Contains(nodeId))
        .ToHashSet();
      if (nodeIds.Count == 0)
      {
        continue;
      }

      await PublishFrozenAsync(context, "fragment-support", new TextSpan(fragment.SpanStart, fragment.SpanLength), nodeIds, cancellationToken);
    }

    var supportNodeIds = nodeOwnership.SkeletonNodeIds
      .Where(nodeId => !_publishedNodeIds.Contains(nodeId))
      .ToHashSet();
    if (supportNodeIds.Count > 0)
    {
      await PublishFrozenAsync(context, "file-support", context.Root.FullSpan, supportNodeIds, cancellationToken);
    }

    await PublishBoundaryAdjacenciesAsync(context, cancellationToken);

    await _session.CompleteAsync(cancellationToken);
    _completed = true;
    await _session.DisposeAsync();
    return new CpgShardBuildResult(new RoslynCpgStreamingFragmentTelemetry(
      _publishedOrders,
      _publishedKinds,
      _fragments.Count,
      _fragments.Count == 0 ? 0 : 1), _session.Telemetry);
  }

  internal async Task PublishOperationFragmentAsync(
    RoslynCpgBuildContext context,
    OperationFragmentFacts facts,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(facts);
    var lookup = CreateLookup(
      "operation-fragment",
      TextSpan.FromBounds(facts.DeclarationSpanStart, facts.DeclarationSpanEnd),
      context.Source);
    var ignoredBoundaryEdges = new List<CpgFrozenBoundaryEdge>();
    var shard = StreamingFragmentCommitter.Commit(
      lookup,
      facts,
      context.Graph.RequirePreallocatedNodeIds(),
      ignoredBoundaryEdges);
    var reusableKey = CpgReusableFragmentKey.Create(shard);
    if (!await _session.TryReuseFragmentAsync(shard, reusableKey, cancellationToken))
    {
      await _session.PublishReusableFragmentAsync(shard, reusableKey, cancellationToken);
    }

    RegisterPrimaryNodes(shard);

    _publishedKinds.Add("operation-fragment");
  }

  public async ValueTask DisposeAsync()
  {
    if (!_completed)
    {
      await _session.DisposeAsync();
    }
  }

  private async Task PublishInitialAsync(RoslynCpgBuildContext context, CancellationToken cancellationToken)
  {
    var facts = context.Graph.SnapshotMutableFacts();
    var descriptors = facts.Nodes.Select(CpgNodeDescriptor.FromNode).ToArray();
    var candidates = facts.PendingEdges.Select(CreateCandidate).ToArray();
    var descriptorBuckets = _fragments.ToDictionary(
      fragment => fragment,
      _ => new List<CpgNodeDescriptor>());
    var candidateBuckets = _fragments.ToDictionary(
      fragment => fragment,
      _ => new List<CpgEdgeCandidate>());
    var skeletonDescriptors = new List<CpgNodeDescriptor>();
    var skeletonCandidates = new List<CpgEdgeCandidate>();
    var ownerByAnchor = new Dictionary<StableNodeAnchor, CpgFragmentOwnership?>();
    foreach (var descriptor in descriptors)
    {
      var owner = _ownership.FindOwner(descriptor);
      ownerByAnchor.Add(descriptor.Anchor, owner);
      if (owner is null)
      {
        skeletonDescriptors.Add(descriptor);
      }
      else
      {
        descriptorBuckets[owner].Add(descriptor);
      }
    }

    foreach (var candidate in candidates)
    {
      var sourceOwner = ownerByAnchor[candidate.SourceAnchor];
      var targetOwner = ownerByAnchor[candidate.TargetAnchor];
      AddCandidateToBucket(sourceOwner, candidate, skeletonCandidates, candidateBuckets);
      if (targetOwner != sourceOwner)
      {
        AddCandidateToBucket(targetOwner, candidate, skeletonCandidates, candidateBuckets);
      }
    }

    await PublishDescriptorsAsync(context, "file-skeleton", context.Root.FullSpan, skeletonDescriptors, skeletonCandidates, cancellationToken);

    foreach (var fragment in _fragments)
    {
      await PublishDescriptorsAsync(
        context,
        fragment.Kind,
        new TextSpan(fragment.SpanStart, fragment.SpanLength),
        descriptorBuckets[fragment],
        candidateBuckets[fragment],
        cancellationToken);
      _publishedOrders.Add(fragment.SourceOrder);
    }
  }

  private async Task PublishDescriptorsAsync(
    RoslynCpgBuildContext context,
    string kind,
    TextSpan span,
    IReadOnlyList<CpgNodeDescriptor> descriptors,
    IReadOnlyList<CpgEdgeCandidate> candidates,
    CancellationToken cancellationToken)
  {
    var lookup = CreateLookup(kind, span, context.Source);
    var ignoredBoundaryEdges = new List<CpgFrozenBoundaryEdge>();
    var shard = CpgFrozenShardExporter.ExportDescriptors(
      lookup,
      descriptors,
      candidates,
      context.Graph.RequirePreallocatedNodeIds(),
      ignoredBoundaryEdges);
    await _session.PublishFragmentAsync(shard, cancellationToken);
    RegisterPrimaryNodes(shard);

    _publishedKinds.Add(kind);
  }

  private async Task PublishFrozenAsync(
    RoslynCpgBuildContext context,
    string kind,
    TextSpan span,
    IReadOnlySet<NodeId> nodeIds,
    CancellationToken cancellationToken)
  {
    var shard = CpgFrozenShardExporter.Export(context.Graph, CreateLookup(kind, span, context.Source), nodeIds);
    await _session.PublishFragmentAsync(shard, cancellationToken);
    RegisterPrimaryNodes(shard);

    _publishedKinds.Add(kind);
  }

  private CpgShardLookup CreateLookup(string kind, TextSpan span, string source)
  {
    return new CpgShardLookup(
      _file,
      new CpgFragmentKey(kind, span.Start, span.Length, Hash(source.Substring(span.Start, span.Length))),
      _options.SchemaVersion,
      _options.ProfileHash);
  }

  private void RegisterPrimaryNodes(CpgFrozenShard shard)
  {
    foreach (var node in shard.Nodes)
    {
      var nodeId = new NodeId(node.NodeId);
      _publishedNodeIds.Add(nodeId);
      if (!_primaryLookupByNodeId.TryAdd(nodeId, shard.Lookup))
      {
        throw new InvalidOperationException($"Node '{nodeId.Value}' was assigned to more than one primary shard.");
      }
    }
  }

  private async Task PublishBoundaryAdjacenciesAsync(
    RoslynCpgBuildContext context,
    CancellationToken cancellationToken)
  {
    foreach (var edge in context.Graph.Edges)
    {
      if (!_primaryLookupByNodeId.TryGetValue(edge.SourceNodeId, out var sourceOwner) ||
          !_primaryLookupByNodeId.TryGetValue(edge.TargetNodeId, out var targetOwner))
      {
        throw new InvalidOperationException("Every frozen edge endpoint must have one primary shard owner.");
      }

      if (sourceOwner == targetOwner)
      {
        continue;
      }

      var boundary = CrossShardEdgeCommitter.Create(edge);
      await AppendBoundaryAsync(sourceOwner, CpgBoundaryAdjacencyDirection.Outgoing, boundary, context.Source, cancellationToken);
      await AppendBoundaryAsync(targetOwner, CpgBoundaryAdjacencyDirection.Incoming, boundary, context.Source, cancellationToken);
    }

    foreach (var bucket in _boundaryBatches.Keys.OrderBy(bucket => bucket.Owner.Fragment.Kind, StringComparer.Ordinal)
      .ThenBy(bucket => bucket.Owner.Fragment.SpanStart)
      .ThenBy(bucket => bucket.Direction)
      .ToArray())
    {
      await FlushBoundaryAsync(bucket, context.Source, cancellationToken);
    }
  }

  private async Task AppendBoundaryAsync(
    CpgShardLookup owner,
    CpgBoundaryAdjacencyDirection direction,
    CpgFrozenBoundaryEdge edge,
    string source,
    CancellationToken cancellationToken)
  {
    var bucket = new BoundaryBucket(owner, direction);
    if (!_boundaryBatches.TryGetValue(bucket, out var batch))
    {
      batch = new List<CpgFrozenBoundaryEdge>();
      _boundaryBatches.Add(bucket, batch);
    }

    batch.Add(edge);
    _bufferedBoundaryEdgeCount += 1;
    _session.ObserveRouterBuffer(_bufferedBoundaryEdgeCount);
    if (batch.Count >= _options.MaxBoundaryAdjacencyEdgesPerShard)
    {
      await FlushBoundaryAsync(bucket, source, cancellationToken);
    }
  }

  private async Task FlushBoundaryAsync(BoundaryBucket bucket, string source, CancellationToken cancellationToken)
  {
    if (!_boundaryBatches.TryGetValue(bucket, out var batch) || batch.Count == 0)
    {
      return;
    }

    var ordinal = _boundaryShardOrdinals.GetValueOrDefault(bucket);
    _boundaryShardOrdinals[bucket] = ordinal + 1;
    var ordered = batch
      .OrderBy(edge => edge.SourceNodeId)
      .ThenBy(edge => edge.Kind, StringComparer.Ordinal)
      .ThenBy(edge => edge.TargetNodeId)
      .ThenBy(edge => edge.ContextId, StringComparer.Ordinal)
      .ToArray();
    _bufferedBoundaryEdgeCount -= batch.Count;
    batch.Clear();
    var ownerFragment = bucket.Owner.Fragment;
    var lookup = new CpgShardLookup(
      bucket.Owner.File,
      new CpgFragmentKey(
        "boundary-adjacency",
        ownerFragment.SpanStart,
        ownerFragment.SpanLength,
        Hash($"{ownerFragment.FragmentHash}|{bucket.Direction}|{ordinal}")),
      bucket.Owner.SchemaVersion,
      bucket.Owner.ProfileHash);
    await _session.PublishFragmentAsync(new CpgFrozenShard(
      lookup,
      Array.Empty<CpgFrozenNode>(),
      Array.Empty<CpgFrozenEdge>(),
      Array.Empty<CpgSymbolLocation>(),
      ordered,
      CpgShardRole.BoundaryAdjacency,
      new CpgBoundaryAdjacency(CpgFragmentOwnerIdentity.Create(bucket.Owner), bucket.Direction)), cancellationToken);
    _publishedKinds.Add("boundary-adjacency");
  }

  private static CpgEdgeCandidate CreateCandidate(RoslynCpgGraph.PendingEdge edge)
  {
    return new CpgEdgeCandidate(
      edge.SourceNode.StableAnchor!.Value,
      edge.TargetNode.StableAnchor!.Value,
      edge.Kind,
      edge.StructuredLabel,
      edge.ContextId,
      edge.CallSiteContext);
  }

  private static void AddCandidateToBucket(
    CpgFragmentOwnership? owner,
    CpgEdgeCandidate candidate,
    List<CpgEdgeCandidate> skeletonCandidates,
    IReadOnlyDictionary<CpgFragmentOwnership, List<CpgEdgeCandidate>> candidateBuckets)
  {
    if (owner is null)
    {
      skeletonCandidates.Add(candidate);
    }
    else
    {
      candidateBuckets[owner].Add(candidate);
    }
  }

  private static IEnumerable<SyntaxNode> FindFragments(SyntaxNode root)
  {
    return root.DescendantNodes()
      .Where(node => node is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax or GlobalStatementSyntax)
      .OrderBy(node => node.SpanStart);
  }

  private static string Hash(string value)
  {
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
  }

  private sealed record BoundaryBucket(CpgShardLookup Owner, CpgBoundaryAdjacencyDirection Direction);
}
