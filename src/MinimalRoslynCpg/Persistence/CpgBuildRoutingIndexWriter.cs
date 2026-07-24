using System.Security.Cryptography;
using System.Text;

namespace MinimalRoslynCpg.Persistence;

public sealed class CpgBuildRoutingIndexWriter
{
  private static readonly byte[] Magic = "CPGI"u8.ToArray();
  public const int FormatVersion = 1;

  public async Task<CpgBuildRoutingIndexWriteResult> WriteAsync(
    string indexPath,
    string buildId,
    IReadOnlyCollection<CpgBuildRoutingShardEntry> entries,
    CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(indexPath);
    ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
    ArgumentNullException.ThrowIfNull(entries);
    if (entries.Count == 0)
    {
      throw new InvalidOperationException("A CPG build routing index requires at least one shard.");
    }

    var schemaVersion = entries.First().Shard.Lookup.SchemaVersion;
    var profileHash = entries.First().Shard.Lookup.ProfileHash;
    if (entries.Any(entry => entry.Shard.Lookup.SchemaVersion != schemaVersion ||
        !string.Equals(entry.Shard.Lookup.ProfileHash, profileHash, StringComparison.Ordinal)))
    {
      throw new InvalidOperationException("A CPG build routing index cannot combine incompatible shard profiles.");
    }

    var primaryNodes = entries
      .Where(entry => entry.Shard.Role == CpgShardRole.Primary)
      .SelectMany(entry => entry.Shard.Nodes.Select(node =>
        new CpgBuildRoutingPrimaryNodeRoute(node.NodeId, entry.Location.ShardId, node.LocalIndex)))
      .Distinct()
      .OrderBy(route => route.NodeId)
      .ThenBy(route => route.ShardId, StringComparer.Ordinal)
      .ThenBy(route => route.LocalOffset)
      .ToArray();
    var boundaryNodes = entries
      .Where(entry => entry.Shard.Role == CpgShardRole.BoundaryAdjacency)
      .SelectMany(entry => (entry.Shard.BoundaryEdges ?? Array.Empty<CpgFrozenBoundaryEdge>())
        .SelectMany(edge => new[] { edge.SourceNodeId, edge.TargetNodeId })
        .Select(nodeId => new CpgBuildRoutingBoundaryNodeRoute(nodeId, entry.Location.ShardId)))
      .Distinct()
      .OrderBy(route => route.NodeId)
      .ThenBy(route => route.ShardId, StringComparer.Ordinal)
      .ToArray();
    var spans = entries
      .Where(entry => entry.Shard.Role == CpgShardRole.Primary)
      .SelectMany(entry => entry.Shard.Nodes
        .Where(node => node.SpanStart.HasValue && node.SpanEnd.HasValue)
        .Select(node => new CpgBuildRoutingSpanRoute(
          entry.Shard.Lookup.File,
          node.SpanStart!.Value,
          node.SpanEnd!.Value - node.SpanStart.Value,
          entry.Location.ShardId)))
      .Distinct()
      .OrderBy(route => route.File.ProjectId, StringComparer.Ordinal)
      .ThenBy(route => route.File.RelativePath, StringComparer.Ordinal)
      .ThenBy(route => route.File.SourceHash, StringComparer.Ordinal)
      .ThenBy(route => route.SpanStart)
      .ThenBy(route => route.SpanLength)
      .ThenBy(route => route.ShardId, StringComparer.Ordinal)
      .ToArray();
    var symbols = entries
      .Where(entry => entry.Shard.Role == CpgShardRole.Primary)
      .SelectMany(entry => entry.Shard.SymbolLocations.Select(symbol =>
        new CpgBuildRoutingSymbolRoute(symbol.SymbolKey, entry.Location.ShardId, symbol.LocalIndex)))
      .Distinct()
      .OrderBy(route => route.SymbolKey, StringComparer.Ordinal)
      .ThenBy(route => route.ShardId, StringComparer.Ordinal)
      .ThenBy(route => route.LocalOffset)
      .ToArray();

    var payload = SerializePayload(primaryNodes, boundaryNodes, spans, symbols);
    var payloadHash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    var temporaryPath = indexPath + ".tmp";
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(indexPath))!);
    try
    {
      await using (var stream = new FileStream(
        temporaryPath,
        FileMode.Create,
        FileAccess.Write,
        FileShare.None,
        bufferSize: 81920,
        FileOptions.Asynchronous | FileOptions.SequentialScan))
      await using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
      {
        writer.Write(Magic);
        writer.Write((byte)FormatVersion);
        writer.Write(buildId);
        writer.Write(schemaVersion);
        writer.Write(profileHash);
        writer.Write(payload.Length);
        writer.Write(Convert.FromHexString(payloadHash));
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
      }

      var index = await new CpgBuildRoutingIndexReader().ReadAsync(temporaryPath, cancellationToken);
      if (!string.Equals(index.PayloadHash, payloadHash, StringComparison.Ordinal))
      {
        throw new InvalidDataException("The CPG routing index read-back hash did not match.");
      }

      File.Move(temporaryPath, indexPath, overwrite: true);
      return new CpgBuildRoutingIndexWriteResult(
        indexPath,
        FormatVersion,
        new FileInfo(indexPath).Length,
        payloadHash);
    }
    catch
    {
      if (File.Exists(temporaryPath))
      {
        File.Delete(temporaryPath);
      }

      throw;
    }
  }

  private static byte[] SerializePayload(
    IReadOnlyList<CpgBuildRoutingPrimaryNodeRoute> primaryNodes,
    IReadOnlyList<CpgBuildRoutingBoundaryNodeRoute> boundaryNodes,
    IReadOnlyList<CpgBuildRoutingSpanRoute> spans,
    IReadOnlyList<CpgBuildRoutingSymbolRoute> symbols)
  {
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
    writer.Write(primaryNodes.Count);
    foreach (var route in primaryNodes)
    {
      writer.Write(route.NodeId);
      writer.Write(route.ShardId);
      writer.Write(route.LocalOffset);
    }

    writer.Write(boundaryNodes.Count);
    foreach (var route in boundaryNodes)
    {
      writer.Write(route.NodeId);
      writer.Write(route.ShardId);
    }

    writer.Write(spans.Count);
    foreach (var route in spans)
    {
      writer.Write(route.File.ProjectId);
      writer.Write(route.File.RelativePath);
      writer.Write(route.File.SourceHash);
      writer.Write(route.SpanStart);
      writer.Write(route.SpanLength);
      writer.Write(route.ShardId);
    }

    writer.Write(symbols.Count);
    foreach (var route in symbols)
    {
      writer.Write(route.SymbolKey);
      writer.Write(route.ShardId);
      writer.Write(route.LocalOffset);
    }

    writer.Flush();
    return stream.ToArray();
  }
}
