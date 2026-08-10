using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using NL.Concurrency;
using NLCPG.Persistence;
using NLCPG.Persistence.Sqlite;
using NLCPG.Builder.Streaming;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace NLCPG.Builder;

internal sealed record CpgRestoreMetrics(
  long CatalogReadMilliseconds,
  long ShardReadMilliseconds,
  int RestoredShardCount,
  long RestoredShardBytes,
  long RestoreFactsElapsedMilliseconds,
  long RestoreFactsAllocatedBytes)
{
    internal static CpgRestoreMetrics Empty { get; } = new(0, 0, 0, 0, 0, 0);
}

internal sealed record CpgBaseRestoreResult(CpgFrozenShardGraphFacts Facts);

internal sealed class CpgShardBuildCoordinator
{
    private static Action<object>? _exportCheckpointObserver;
    private readonly CpgPersistenceOptions _options;
    private readonly IConcurrencyPool _concurrencyPool;

    internal CpgRestoreMetrics LastRestoreMetrics { get; private set; } = CpgRestoreMetrics.Empty;

    internal CpgShardBuildCoordinator(CpgPersistenceOptions options, IConcurrencyPool concurrencyPool)
    {
        _options = options;
        _concurrencyPool = concurrencyPool;
        _options.Validate();
    }

    internal static Action<object>? ExportCheckpointObserver
    {
        get => Volatile.Read(ref _exportCheckpointObserver);
        set => Volatile.Write(ref _exportCheckpointObserver, value);
    }

    internal async Task<NLCPGPersistenceMetrics> PersistAsync(NLCPGBuildContext context, CancellationToken cancellationToken)
    {
        var persistStopwatch = Stopwatch.StartNew();
        await using var session = await CpgShardBuildSession.BeginAsync(_options, cancellationToken);
        session.Store.DeleteStaleTemporaryFiles();
        var inputDirectory = Path.GetDirectoryName(context.FilePath);
        var projectRoot = Path.GetFullPath(string.IsNullOrEmpty(inputDirectory) ? "." : inputDirectory);
        var file = new CpgFileKey(
          projectRoot,
          context.FilePath,
          Hash(context.Source));
        var fragments = FindFragments(context.Root)
          .Select((fragment, order) => new CpgFragmentOwnership(
            fragment.GetType().Name,
            fragment.SpanStart,
            fragment.Span.End,
            order))
          .ToArray();
        var ownership = new FragmentOwnershipIndex(fragments);
        var nodeOwnership = FragmentNodeOwnershipIndex.Create(context.Graph.Nodes, ownership);
        var exportProjection = CpgFrozenShardExporter.Prepare(context.Graph);
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
        }

        for (var order = 0; order < fragments.Length; order += 1)
        {
            var fragment = fragments[order];
            var nodeIds = nodeOwnership.GetNodeIds(fragment);
            var span = new TextSpan(fragment.SpanStart, fragment.SpanLength);
            exportRequests.Add(new CpgShardExportRequest(sourceSequence, fragment.Kind, span, nodeIds));
            sourceSequence += 1;
        }

        var skeletonNodeIds = nodeOwnership.SkeletonNodeIds;
        exportRequests.Add(new CpgShardExportRequest(
          sourceSequence,
          "file-skeleton",
          context.Root.FullSpan,
          skeletonNodeIds));
        sourceSequence += 1;
        var activeExports = 0;
        await _concurrencyPool.ForEachAsync(
          exportRequests,
          _options.MaxConcurrentShardExports,
          async (request, _, token) =>
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
                   var shard = CreateShard(context, file, request.Kind, request.Span, request.NodeIds, exportProjection);
                   if (!_options.StreamingMode && IsReusableFragmentKind(request.Kind))
                   {
                       var reusableKey = CpgReusableFragmentKey.Create(shard);
                       if (!await session.TryReuseFragmentAsync(shard, reusableKey, token, request.Sequence))
                       {
                           await session.PublishReusableFragmentAsync(shard, reusableKey, request.Sequence, token);
                       }
                   }
                   else
                   {
                       await session.PublishFragmentAsync(shard, request.Sequence, token);
                   }
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
            edge.CallSiteContext?.DisplayName,
            CpgFrozenFlowSummaryLabel.From(edge.StructuredLabel)))
          .ToArray();
        if (boundaryEdges.Length > 0)
        {
            await session.PublishFragmentAsync(CreateBoundaryShard(
              context,
              file,
              boundaryEdges), sourceSequence, cancellationToken);
        }

        await session.CompleteAsync(cancellationToken);
        persistStopwatch.Stop();
        return session.CreatePersistenceMetrics(persistStopwatch.ElapsedMilliseconds);
    }

    internal async Task<CpgBaseRestoreResult?> TryRestoreBaseAsync(NLCPGBuildContext context, CancellationToken cancellationToken)
    {
        LastRestoreMetrics = CpgRestoreMetrics.Empty;
        var catalogPath = Path.Combine(_options.StoreRoot, "catalog.db");
        if (File.Exists(catalogPath))
        {
            return await TryRestoreBaseFromCatalogAsync(context, catalogPath, cancellationToken);
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

        return await TryRestoreBaseFromCatalogAsync(context, catalogPath, cancellationToken);
    }

    private async Task<CpgBaseRestoreResult?> TryRestoreBaseFromCatalogAsync(NLCPGBuildContext context, string catalogPath, CancellationToken cancellationToken)
    {
        var store = new CpgShardStore(_options.StoreRoot);
        var catalog = new SqliteCpgShardCatalog(catalogPath);
        var lookup = CreateFileGraphLookup(context);
        if (_options.StreamingMode)
        {
            IReadOnlyList<CpgShardLocation> locations;
            var catalogReadStopwatch = Stopwatch.StartNew();
            try
            {
                locations = await catalog.FindByFileAsync(
                  lookup.File,
                  _options.SchemaVersion,
                  _options.ProfileHash,
                  cancellationToken);
            }
            finally
            {
                catalogReadStopwatch.Stop();
                LastRestoreMetrics = LastRestoreMetrics with
                {
                    CatalogReadMilliseconds = catalogReadStopwatch.ElapsedMilliseconds,
                };
            }

            if (locations.Count == 0)
            {
                return null;
            }

            var restoredShardCount = 0;
            long restoredShardBytes = 0;
            long shardReadMilliseconds = 0;
            long restoreFactsElapsedMilliseconds = 0;
            long restoreFactsAllocatedBytes = 0;
            try
            {
                var accumulator = CpgFrozenShardGraphReader.CreateMutableFactsAccumulator();
                foreach (var location in locations)
                {
                    var shardReadStopwatch = Stopwatch.StartNew();
                    var shard = await store.ReadAsync(location, cancellationToken);
                    shardReadStopwatch.Stop();
                    shardReadMilliseconds += shardReadStopwatch.ElapsedMilliseconds;

                    var factsStopwatch = Stopwatch.StartNew();
                    var factsAllocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
                    accumulator.Add(shard);
                    factsStopwatch.Stop();
                    restoreFactsElapsedMilliseconds += factsStopwatch.ElapsedMilliseconds;
                    restoreFactsAllocatedBytes +=
                      GC.GetTotalAllocatedBytes(precise: false) - factsAllocatedBefore;
                    restoredShardCount += 1;
                    restoredShardBytes += location.ByteLength;
                }

                var completeStopwatch = Stopwatch.StartNew();
                var completeAllocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
                var facts = accumulator.Complete();
                completeStopwatch.Stop();
                restoreFactsElapsedMilliseconds += completeStopwatch.ElapsedMilliseconds;
                restoreFactsAllocatedBytes +=
                  GC.GetTotalAllocatedBytes(precise: false) - completeAllocatedBefore;
                return new CpgBaseRestoreResult(facts);
            }
            catch (IOException)
            {
                return null;
            }
            catch (InvalidDataException)
            {
                return null;
            }
            finally
            {
                LastRestoreMetrics = LastRestoreMetrics with
                {
                    ShardReadMilliseconds = shardReadMilliseconds,
                    RestoredShardCount = restoredShardCount,
                    RestoredShardBytes = restoredShardBytes,
                    RestoreFactsElapsedMilliseconds = restoreFactsElapsedMilliseconds,
                    RestoreFactsAllocatedBytes = restoreFactsAllocatedBytes,
                };
            }
        }

        CpgShardLease? lease;
        var catalogLookupStopwatch = Stopwatch.StartNew();
        try
        {
            lease = await catalog.TryAcquireAsync(lookup, cancellationToken);
        }
        finally
        {
            catalogLookupStopwatch.Stop();
            LastRestoreMetrics = LastRestoreMetrics with
            {
                CatalogReadMilliseconds = catalogLookupStopwatch.ElapsedMilliseconds,
            };
        }

        if (lease is null)
        {
            return null;
        }

        var nonStreamingShardCount = 0;
        long nonStreamingShardBytes = 0;
        long nonStreamingShardReadMilliseconds = 0;
        long nonStreamingRestoreFactsElapsedMilliseconds = 0;
        long nonStreamingRestoreFactsAllocatedBytes = 0;
        try
        {
            var shardReadStopwatch = Stopwatch.StartNew();
            var shard = await store.TryReadAsync(lease.Location, lookup, cancellationToken);
            shardReadStopwatch.Stop();
            nonStreamingShardReadMilliseconds = shardReadStopwatch.ElapsedMilliseconds;
            if (shard is not null)
            {
                nonStreamingShardCount = 1;
                nonStreamingShardBytes = lease.Location.ByteLength;
            }

            if (shard is null)
            {
                return null;
            }

            var factsStopwatch = Stopwatch.StartNew();
            var factsAllocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
            var facts = CpgFrozenShardGraphReader.ReadMutableFacts(new[] { shard });
            factsStopwatch.Stop();
            nonStreamingRestoreFactsElapsedMilliseconds = factsStopwatch.ElapsedMilliseconds;
            nonStreamingRestoreFactsAllocatedBytes =
              GC.GetTotalAllocatedBytes(precise: false) - factsAllocatedBefore;
            return new CpgBaseRestoreResult(facts);
        }
        catch (IOException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        finally
        {
            LastRestoreMetrics = LastRestoreMetrics with
            {
                ShardReadMilliseconds = nonStreamingShardReadMilliseconds,
                RestoredShardCount = nonStreamingShardCount,
                RestoredShardBytes = nonStreamingShardBytes,
                RestoreFactsElapsedMilliseconds = nonStreamingRestoreFactsElapsedMilliseconds,
                RestoreFactsAllocatedBytes = nonStreamingRestoreFactsAllocatedBytes,
            };
        }
    }

    private CpgFrozenShard CreateShard(NLCPGBuildContext context, CpgFileKey file, string kind, TextSpan span, IReadOnlySet<Model.NodeId> nodeIds, CpgFrozenGraphProjection exportProjection)
    {
        var fragmentHash = Hash(context.Source.Substring(span.Start, span.Length));
        var lookup = new CpgShardLookup(
          file,
          new CpgFragmentKey(kind, span.Start, span.Length, fragmentHash),
          _options.SchemaVersion,
          _options.ProfileHash);
        return CpgFrozenShardExporter.Export(exportProjection, lookup, nodeIds);
    }

    private CpgFrozenShard CreateBoundaryShard(NLCPGBuildContext context, CpgFileKey file, IReadOnlyList<CpgFrozenBoundaryEdge> boundaryEdges)
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

    private static bool IsReusableFragmentKind(string kind)
    {
        return kind is
          nameof(MethodDeclarationSyntax) or
          nameof(ConstructorDeclarationSyntax) or
          nameof(OperatorDeclarationSyntax) or
          nameof(ConversionOperatorDeclarationSyntax) or
          nameof(DestructorDeclarationSyntax) or
          nameof(AccessorDeclarationSyntax) or
          nameof(LocalFunctionStatementSyntax) or
          nameof(GlobalStatementSyntax);
    }

    private static bool IsInside(Model.NLCPGNode node, TextSpan span)
    {
        return node.SpanStart.HasValue && node.SpanEnd.HasValue &&
          node.SpanStart.Value >= span.Start && node.SpanEnd.Value <= span.End;
    }

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private CpgShardLookup CreateFileGraphLookup(NLCPGBuildContext context)
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
