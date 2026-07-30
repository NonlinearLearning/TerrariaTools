using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using NL.Concurrency;
using NLCPG.Persistence;
using NLCPG.Persistence.Sqlite;
using NLCPG.Builder.Streaming;
using System.Security.Cryptography;
using System.Text;

namespace NLCPG.Builder;

internal sealed record CpgBaseRestoreResult(CpgFrozenShardGraphFacts Facts);

internal sealed class CpgShardBuildCoordinator
{
    private static Action<object>? _exportCheckpointObserver;
    private readonly CpgPersistenceOptions _options;
    private readonly IConcurrencyPool _concurrencyPool;

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

    internal async Task PersistAsync(NLCPGBuildContext context, CancellationToken cancellationToken)
    {
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
    }

    internal async Task<CpgBaseRestoreResult?> TryRestoreBaseAsync(NLCPGBuildContext context, CancellationToken cancellationToken)
    {
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

                return new CpgBaseRestoreResult(CpgFrozenShardGraphReader.ReadMutableFacts(shards));
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
            return shard is null
              ? null
              : new CpgBaseRestoreResult(CpgFrozenShardGraphReader.ReadMutableFacts(new[] { shard }));
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

    private CpgFrozenShard CreateShard(NLCPGBuildContext context, CpgFileKey file, string kind, TextSpan span, IReadOnlySet<Model.NodeId> nodeIds)
    {
        var fragmentHash = Hash(context.Source.Substring(span.Start, span.Length));
        var lookup = new CpgShardLookup(
          file,
          new CpgFragmentKey(kind, span.Start, span.Length, fragmentHash),
          _options.SchemaVersion,
          _options.ProfileHash);
        return CpgFrozenShardExporter.Export(context.Graph, lookup, nodeIds);
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
