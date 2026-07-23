using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using MinimalRoslynCpg.Persistence;
using MinimalRoslynCpg.Persistence.Sqlite;
using MinimalRoslynCpg.Builder.Streaming;
using System.Security.Cryptography;
using System.Text;

namespace MinimalRoslynCpg.Builder;

internal sealed record CpgShardBuildResult(
  RoslynCpgStreamingFragmentTelemetry StreamingFragments,
  CpgPersistenceTelemetry Persistence);

internal sealed class CpgShardBuildCoordinator
{
  private static Action<object>? _exportCheckpointObserver;
  private readonly CpgPersistenceOptions _options;

  internal CpgShardBuildCoordinator(CpgPersistenceOptions options)
  {
    _options = options;
    _options.Validate();
  }

  internal static Action<object>? ExportCheckpointObserver
  {
    get => Volatile.Read(ref _exportCheckpointObserver);
    set => Volatile.Write(ref _exportCheckpointObserver, value);
  }

  internal async Task<CpgShardBuildResult> PersistAsync(
    RoslynCpgBuildContext context,
    CancellationToken cancellationToken)
  {
    await using var session = await CpgShardBuildSession.BeginAsync(_options, cancellationToken);
    session.Store.DeleteStaleTemporaryFiles();
    var inputDirectory = Path.GetDirectoryName(context.FilePath);
    var projectRoot = Path.GetFullPath(string.IsNullOrEmpty(inputDirectory) ? "." : inputDirectory);
    var file = new CpgFileKey(
      projectRoot,
      context.FilePath,
      Hash(context.Source));
    var publishedOrders = new List<int>();
    var publishedKinds = new List<string>();
    var releasedFragmentCount = 0;
    var peakRetainedFragmentCount = 0;
    var fragments = FindFragments(context.Root)
      .Select((fragment, order) => new CpgFragmentOwnership(
        fragment.GetType().Name,
        fragment.SpanStart,
        fragment.Span.End,
        order))
      .ToArray();
    var ownership = new FragmentOwnershipIndex(fragments);
    var nodeOwnership = FragmentNodeOwnershipIndex.Create(context.Graph.Nodes, ownership);
    var exportRequests = new List<CpgShardExportRequest>();
    var sourceSequence = 0L;
    if (!_options.StreamingMode)
    {
      exportRequests.Add(new CpgShardExportRequest(
        sourceSequence,
        "file-graph",
        context.Root.FullSpan,
        context.Graph.Nodes.Select(node => node.NodeId!.Value).ToHashSet()));
      sourceSequence += 1;
      publishedKinds.Add("file-graph");
    }

    for (var order = 0; order < fragments.Length; order += 1)
    {
      var fragment = fragments[order];
      var nodeIds = nodeOwnership.GetNodeIds(fragment);
      var span = new TextSpan(fragment.SpanStart, fragment.SpanLength);
      exportRequests.Add(new CpgShardExportRequest(sourceSequence, fragment.Kind, span, nodeIds));
      sourceSequence += 1;
      publishedOrders.Add(order);
      publishedKinds.Add(fragment.Kind);
      if (_options.StreamingMode)
      {
        peakRetainedFragmentCount = Math.Max(peakRetainedFragmentCount, 1);
        releasedFragmentCount += 1;
      }
    }

    var skeletonNodeIds = nodeOwnership.SkeletonNodeIds;
    exportRequests.Add(new CpgShardExportRequest(
      sourceSequence,
      "file-skeleton",
      context.Root.FullSpan,
      skeletonNodeIds));
    sourceSequence += 1;
    publishedKinds.Add("file-skeleton");
    var activeExports = 0;
    await Parallel.ForEachAsync(
      exportRequests,
      new ParallelOptions
      {
        MaxDegreeOfParallelism = _options.MaxConcurrentShardExports,
        CancellationToken = cancellationToken,
      },
      async (request, token) =>
      {
        var active = Interlocked.Increment(ref activeExports);
        session.ObserveShardExport(active);
        try
        {
          ExportCheckpointObserver?.Invoke(new CpgShardExportCheckpoint(
            request.Sequence,
            request.Kind,
            request.Span.Start,
            active));
          var shard = CreateShard(context, file, request.Kind, request.Span, request.NodeIds);
          await session.PublishFragmentAsync(shard, request.Sequence, token);
        }
        finally
        {
          Interlocked.Decrement(ref activeExports);
        }
      });
    var boundaryEdges = context.Graph.Edges
      .Where(edge => nodeOwnership.GetOwner(edge.SourceNodeId) != nodeOwnership.GetOwner(edge.TargetNodeId))
      .OrderBy(edge => edge.SourceNodeId)
      .ThenBy(edge => edge.Kind)
      .ThenBy(edge => edge.TargetNodeId)
      .Select(edge => new CpgFrozenBoundaryEdge(
        edge.SourceNodeId.Value,
        edge.TargetNodeId.Value,
        edge.Kind.ToString(),
        edge.StructuredLabel?.StableKey,
        edge.ContextId?.Value,
        edge.CallSiteContext?.FilePath,
        edge.CallSiteContext?.SpanStart,
        edge.CallSiteContext?.SpanEnd,
        edge.CallSiteContext?.DisplayName))
      .ToArray();
    if (boundaryEdges.Length > 0)
    {
      await session.PublishFragmentAsync(CreateBoundaryShard(
        context,
        file,
        boundaryEdges), sourceSequence, cancellationToken);
      publishedKinds.Add("cross-shard-edges");
    }

    await session.CompleteAsync(cancellationToken);

    return new CpgShardBuildResult(new RoslynCpgStreamingFragmentTelemetry(
      publishedOrders,
      publishedKinds,
      releasedFragmentCount,
      peakRetainedFragmentCount), session.Telemetry);
  }

  internal async Task<Model.RoslynCpgGraph?> TryRestoreAsync(
    RoslynCpgBuildContext context,
    CancellationToken cancellationToken)
  {
    var catalogPath = Path.Combine(_options.StoreRoot, "catalog.db");
    if (File.Exists(catalogPath))
    {
      return await TryRestoreFromCatalogAsync(context, catalogPath, cancellationToken);
    }

    using var storeLock = await CpgShardStoreLock.AcquireAsync(
      _options.StoreRoot,
      TimeSpan.FromMilliseconds(_options.StoreLockWaitMilliseconds),
      cancellationToken);
    var store = new CpgShardStore(_options.StoreRoot);
    store.DeleteStaleTemporaryFiles();
    if (!File.Exists(catalogPath))
    {
      var rebuildingCatalog = new SqliteCpgShardCatalog(catalogPath);
      await rebuildingCatalog.RebuildFromShardHeadersAsync(_options.StoreRoot, cancellationToken);
    }

    return await TryRestoreFromCatalogAsync(context, catalogPath, cancellationToken);
  }

  private async Task<Model.RoslynCpgGraph?> TryRestoreFromCatalogAsync(
    RoslynCpgBuildContext context,
    string catalogPath,
    CancellationToken cancellationToken)
  {
    var store = new CpgShardStore(_options.StoreRoot);
    var catalog = new SqliteCpgShardCatalog(catalogPath);
    var lookup = CreateFileGraphLookup(context);
    if (_options.StreamingMode)
    {
      var locations = await catalog.FindByFileAsync(
        lookup.File,
        _options.SchemaVersion,
        _options.ProfileHash,
        cancellationToken);
      if (locations.Count == 0)
      {
        return null;
      }

      try
      {
        var shards = new List<CpgFrozenShard>(locations.Count);
        foreach (var location in locations)
        {
          shards.Add(await store.ReadAsync(location, cancellationToken));
        }

        return CpgFrozenShardGraphReader.ReadGraph(shards);
      }
      catch (IOException)
      {
        return null;
      }
      catch (InvalidDataException)
      {
        return null;
      }
    }

    var lease = await catalog.TryAcquireAsync(lookup, cancellationToken);
    if (lease is null)
    {
      return null;
    }

    try
    {
      var shard = await store.TryReadAsync(lease.Location, lookup, cancellationToken);
      return shard is null ? null : CpgFrozenShardGraphReader.ReadGraph(shard);
    }
    catch (IOException)
    {
      return null;
    }
    catch (InvalidDataException)
    {
      return null;
    }
  }

  private CpgFrozenShard CreateShard(
    RoslynCpgBuildContext context,
    CpgFileKey file,
    string kind,
    TextSpan span,
    IReadOnlySet<Model.NodeId> nodeIds)
  {
    var fragmentHash = Hash(context.Source.Substring(span.Start, span.Length));
    var lookup = new CpgShardLookup(
      file,
      new CpgFragmentKey(kind, span.Start, span.Length, fragmentHash),
      _options.SchemaVersion,
      _options.ProfileHash);
    return CpgFrozenShardExporter.Export(context.Graph, lookup, nodeIds);
  }

  private CpgFrozenShard CreateBoundaryShard(
    RoslynCpgBuildContext context,
    CpgFileKey file,
    IReadOnlyList<CpgFrozenBoundaryEdge> boundaryEdges)
  {
    var span = context.Root.FullSpan;
    var fragmentHash = Hash(context.Source.Substring(span.Start, span.Length));
    var lookup = new CpgShardLookup(
      file,
      new CpgFragmentKey("cross-shard-edges", span.Start, span.Length, fragmentHash),
      _options.SchemaVersion,
      _options.ProfileHash);
    return new CpgFrozenShard(
      lookup,
      Array.Empty<CpgFrozenNode>(),
      Array.Empty<CpgFrozenEdge>(),
      Array.Empty<CpgSymbolLocation>(),
      boundaryEdges);
  }

  private static IEnumerable<SyntaxNode> FindFragments(SyntaxNode root)
  {
    return root.DescendantNodes()
      .Where(node => node is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax or GlobalStatementSyntax)
      .OrderBy(node => node.SpanStart);
  }

  private static bool IsInside(Model.RoslynCpgNode node, TextSpan span)
  {
    return node.SpanStart.HasValue && node.SpanEnd.HasValue &&
      node.SpanStart.Value >= span.Start && node.SpanEnd.Value <= span.End;
  }

  private static string Hash(string value)
  {
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
  }

  private CpgShardLookup CreateFileGraphLookup(RoslynCpgBuildContext context)
  {
    var inputDirectory = Path.GetDirectoryName(context.FilePath);
    var projectRoot = Path.GetFullPath(string.IsNullOrEmpty(inputDirectory) ? "." : inputDirectory);
    return new CpgShardLookup(
      new CpgFileKey(projectRoot, context.FilePath, Hash(context.Source)),
      new CpgFragmentKey("file-graph", 0, context.Source.Length, Hash(context.Source)),
      _options.SchemaVersion,
      _options.ProfileHash);
  }
}

internal sealed record CpgShardExportRequest(
  long Sequence,
  string Kind,
  TextSpan Span,
  IReadOnlySet<Model.NodeId> NodeIds);

internal sealed record CpgShardExportCheckpoint(
  long SourceSequence,
  string FragmentKind,
  int FragmentSpanStart,
  int ActiveExportCount);
