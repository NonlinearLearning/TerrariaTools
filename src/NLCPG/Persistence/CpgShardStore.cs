using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using NLCPG.Analysis.FlowSummaries;
using NLCPG.Builder;
using NLCPG.Contracts;

namespace NLCPG.Persistence;

public sealed class CpgShardStore : ICpgShardStore
{
    private static readonly byte[] Magic = "CPGB"u8.ToArray();
    private const int StreamBufferSize = 64 * 1024;
    private const int MaxPooledLocalIndexCount = 16 * 1024 * 1024;
    private const int LegacyFormatVersion = 5;
    private const int IncomingEdgeIndexFormatVersion = 6;
    private const int FormatVersion = 7;
    private static Action<string>? _afterTemporaryWriteForTesting;
    private static Action<CpgShardLocation>? _afterReadForTesting;
    private readonly CpgPersistenceDurabilityMode _durabilityMode;
    private readonly string _shardsRoot;

    // 以指定根目录和耐久模式初始化分片存储。
    public CpgShardStore(string storeRoot, CpgPersistenceDurabilityMode durabilityMode = CpgPersistenceDurabilityMode.Strict)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeRoot);
        if (!Enum.IsDefined(durabilityMode))
        {
            throw new ArgumentOutOfRangeException(nameof(durabilityMode));
        }

        _durabilityMode = durabilityMode;
        _shardsRoot = Path.Combine(storeRoot, "shards");
    }

    internal static Action<string>? AfterTemporaryWriteForTesting
    {
        get => Volatile.Read(ref _afterTemporaryWriteForTesting);
        set => Volatile.Write(ref _afterTemporaryWriteForTesting, value);
    }

    internal static Action<CpgShardLocation>? AfterReadForTesting
    {
        get => Volatile.Read(ref _afterReadForTesting);
        set => Volatile.Write(ref _afterReadForTesting, value);
    }

    // 删除遗留的临时分片文件，并返回删除数量。
    public int DeleteStaleTemporaryFiles()
    {
        if (!Directory.Exists(_shardsRoot))
        {
            return 0;
        }

        var deletedCount = 0;
        foreach (var temporaryPath in Directory.EnumerateFiles(
          _shardsRoot,
          "*.tmp",
          SearchOption.AllDirectories))
        {
            File.Delete(temporaryPath);
            deletedCount += 1;
        }

        return deletedCount;
    }

    // 把冻结分片写入磁盘并返回已完成分片的位置描述。
    public async Task<CpgShardLocation> WriteAsync(CpgFrozenShard shard, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shard);
        cancellationToken.ThrowIfCancellationRequested();
        var shardId = CreateShardId(shard.Lookup);
        var directory = Path.Combine(_shardsRoot, shardId[..2]);
        Directory.CreateDirectory(directory);
        var finalPath = Path.Combine(directory, $"{shardId}.cpgbin");
        var temporaryPath = finalPath + ".tmp";
        ShardPayloadWriteResult payload;

        try
        {
            payload = await WritePayloadAsync(shard, temporaryPath, cancellationToken);
            var shardHash = payload.Hash;
            Volatile.Read(ref _afterTemporaryWriteForTesting)?.Invoke(temporaryPath);

            if (_durabilityMode == CpgPersistenceDurabilityMode.Strict)
            {
                ValidateFile(temporaryPath, shardHash, cancellationToken);
            }
            else
            {
                ValidateTemporaryHeader(temporaryPath, cancellationToken);
            }
            File.Move(temporaryPath, finalPath, overwrite: true);
            return new CpgShardLocation(
              shardId,
              finalPath,
              shardHash,
              payload.Length,
              CpgShardStatus.Complete);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    // 读取并校验一个已完成分片的位置记录。
    public Task<CpgFrozenShard> ReadAsync(CpgShardLocation location, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (location.Status != CpgShardStatus.Complete)
        {
            throw new InvalidOperationException("Only complete CPG shards can be read.");
        }

        Volatile.Read(ref _afterReadForTesting)?.Invoke(location);
        var payload = ReadPayloadFromPath(location.ShardPath, cancellationToken);
        if (!string.Equals(payload.Hash, location.ShardHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The CPG shard hash does not match its catalog location.");
        }

        return Task.FromResult(payload.Shard);
    }

    internal void EnsureValid(CpgShardLocation location, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (location.Status != CpgShardStatus.Complete)
        {
            throw new InvalidOperationException("Only complete CPG shards can be validated.");
        }

        ValidateFile(location.ShardPath, location.ShardHash, cancellationToken);
    }

    // 读取分片，并在查找键不匹配时返回空结果。
    public async Task<CpgFrozenShard?> TryReadAsync(CpgShardLocation location, CpgShardLookup lookup, CancellationToken cancellationToken)
    {
        var shard = await ReadAsync(location, cancellationToken);
        return shard.Lookup == lookup ? shard : null;
    }

    // 从任意分片文件路径恢复分片内容与对应的位置元数据。
    public Task<(CpgFrozenShard Shard, CpgShardLocation Location)> ReadFromPathAsync(string shardPath, CancellationToken cancellationToken)
    {
        var payload = ReadPayloadFromPath(shardPath, cancellationToken);
        var location = new CpgShardLocation(
          CreateShardId(payload.Shard.Lookup),
          shardPath,
          payload.Hash,
          payload.Length,
          CpgShardStatus.Complete);
        return Task.FromResult((payload.Shard, location));
    }

    private static string CreateShardId(CpgShardLookup lookup)
    {
        var identity = string.Join("|", lookup.File.ProjectId, lookup.File.RelativePath,
          lookup.File.SourceHash, lookup.Fragment.Kind, lookup.Fragment.SpanStart,
          lookup.Fragment.SpanLength, lookup.Fragment.FragmentHash, lookup.SchemaVersion,
          lookup.ProfileHash);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    private async Task<ShardPayloadWriteResult> WritePayloadAsync(
      CpgFrozenShard shard,
      string temporaryPath,
      CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var stream = new FileStream(
          temporaryPath,
          FileMode.Create,
          FileAccess.Write,
          FileShare.None,
          StreamBufferSize,
          FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using (var hashingStream = new CpgHashingWriteStream(stream, hash))
        using (var writer = new BinaryWriter(hashingStream, Encoding.UTF8, leaveOpen: true))
        {
            WriteSerializedPayload(writer, shard);
            writer.Flush();
        }

        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: _durabilityMode == CpgPersistenceDurabilityMode.Strict);
        return new ShardPayloadWriteResult(
          Convert.ToHexString(hash.GetHashAndReset()),
          stream.Length);
    }

    private static void WriteSerializedPayload(BinaryWriter writer, CpgFrozenShard shard)
    {
        var (incomingEdgeOffsets, incomingEdgeIndexes) = CpgFrozenShardIncomingEdgeIndex.Resolve(shard);
        writer.Write(Magic);
        writer.Write(FormatVersion);
        WriteLookup(writer, shard.Lookup);
        writer.Write((int)shard.Role);
        WriteOptional(writer, shard.BoundaryAdjacency?.OwnerFragmentId);
        writer.Write((int?)shard.BoundaryAdjacency?.Direction ?? -1);
        writer.Write(shard.Nodes.Count);
        foreach (var node in shard.Nodes.OrderBy(node => node.LocalIndex))
        {
            writer.Write(node.LocalIndex);
            writer.Write(node.NodeId);
            WriteRequired(writer, node.Kind);
            WriteOptional(writer, node.FilePath);
            WriteOptionalInt(writer, node.SpanStart);
            WriteOptionalInt(writer, node.SpanEnd);
            WriteRequired(writer, node.DisplayKind);
            WriteOptional(writer, node.Name);
            WriteOptional(writer, node.FullName);
            WriteOptional(writer, node.Signature);
            writer.Write(node.IsImplicit);
            writer.Write(node.StableFilePathId); writer.Write(node.StableSpanStart); writer.Write(node.StableSpanEnd);
            writer.Write(node.StableRole); writer.Write(node.StableOrdinal); writer.Write(node.StableExtraKeyId);
        }

        writer.Write(shard.Edges.Count);
        foreach (var edge in shard.Edges)
        {
            writer.Write(edge.SourceLocalIndex);
            writer.Write(edge.TargetLocalIndex);
            WriteRequired(writer, edge.Kind);
            WriteOptional(writer, edge.Label);
            WriteOptional(writer, edge.ContextId);
            WriteOptional(writer, edge.CallSiteFilePath);
            WriteOptionalInt(writer, edge.CallSiteSpanStart);
            WriteOptionalInt(writer, edge.CallSiteSpanEnd);
            WriteOptional(writer, edge.CallSiteDisplayName);
            WriteFlowSummaryLabel(writer, edge.FlowSummaryLabel);
        }

        foreach (var offset in incomingEdgeOffsets)
        {
            writer.Write(offset);
        }

        foreach (var edgeIndex in incomingEdgeIndexes)
        {
            writer.Write(edgeIndex);
        }

        var boundaryEdges = shard.BoundaryEdges ?? Array.Empty<CpgFrozenBoundaryEdge>();
        writer.Write(boundaryEdges.Count);
        foreach (var edge in boundaryEdges)
        {
            writer.Write(edge.SourceNodeId);
            writer.Write(edge.TargetNodeId);
            WriteRequired(writer, edge.Kind);
            WriteOptional(writer, edge.Label);
            WriteOptional(writer, edge.ContextId);
            WriteOptional(writer, edge.CallSiteFilePath);
            WriteOptionalInt(writer, edge.CallSiteSpanStart);
            WriteOptionalInt(writer, edge.CallSiteSpanEnd);
            WriteOptional(writer, edge.CallSiteDisplayName);
            WriteFlowSummaryLabel(writer, edge.FlowSummaryLabel);
        }

        writer.Write(shard.SymbolLocations.Count);
        foreach (var location in shard.SymbolLocations.OrderBy(location => location.SymbolKey, StringComparer.Ordinal))
        {
            WriteRequired(writer, location.SymbolKey);
            writer.Write(location.LocalIndex);
        }

        writer.Flush();
    }

    private static ShardPayloadReadResult ReadPayloadFromPath(string shardPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardPath);
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new FileStream(
          shardPath,
          FileMode.Open,
          FileAccess.Read,
          FileShare.Read,
          StreamBufferSize,
          FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var hashingStream = new CpgHashingReadStream(stream, hash, cancellationToken);
        var shard = Deserialize(hashingStream);
        return new ShardPayloadReadResult(
          shard,
          Convert.ToHexString(hash.GetHashAndReset()),
          stream.Length);
    }

    private static CpgFrozenShard Deserialize(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (!reader.ReadBytes(Magic.Length).SequenceEqual(Magic))
        {
            throw new InvalidDataException("The CPG shard header is unsupported or corrupt.");
        }

        var formatVersion = reader.ReadInt32();
        if (!IsSupportedFormatVersion(formatVersion))
        {
            throw new InvalidDataException("The CPG shard header is unsupported or corrupt.");
        }

        var lookup = ReadLookup(reader);
        var role = (CpgShardRole)reader.ReadInt32();
        var ownerFragmentId = ReadOptional(reader);
        var directionValue = reader.ReadInt32();
        var adjacency = ownerFragmentId is null
          ? null
          : new CpgBoundaryAdjacency(ownerFragmentId, (CpgBoundaryAdjacencyDirection)directionValue);
        var nodes = Enumerable.Range(0, reader.ReadInt32()).Select(_ => new CpgFrozenNode(
          reader.ReadInt32(), reader.ReadUInt32(), ReadRequired(reader), ReadOptional(reader),
          ReadOptionalInt(reader), ReadOptionalInt(reader), ReadRequired(reader), ReadOptional(reader),
          ReadOptional(reader), ReadOptional(reader), reader.ReadBoolean(), reader.ReadUInt32(), reader.ReadInt32(),
          reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadUInt32())).ToArray();
        var edges = Enumerable.Range(0, reader.ReadInt32()).Select(_ => new CpgFrozenEdge(
          reader.ReadInt32(), reader.ReadInt32(), ReadRequired(reader), ReadOptional(reader),
          ReadOptional(reader), ReadOptional(reader), ReadOptionalInt(reader), ReadOptionalInt(reader),
          ReadOptional(reader), formatVersion >= FormatVersion ? ReadFlowSummaryLabel(reader) : null)).ToArray();
        int[]? incomingEdgeOffsets = null;
        int[]? incomingEdgeIndexes = null;
        if (formatVersion >= IncomingEdgeIndexFormatVersion)
        {
            incomingEdgeOffsets = Enumerable.Range(0, nodes.Length + 1)
              .Select(_ => reader.ReadInt32())
              .ToArray();
            incomingEdgeIndexes = Enumerable.Range(0, edges.Length)
              .Select(_ => reader.ReadInt32())
              .ToArray();
        }

        var boundaryEdges = Enumerable.Range(0, reader.ReadInt32()).Select(_ => new CpgFrozenBoundaryEdge(
          reader.ReadUInt32(), reader.ReadUInt32(), ReadRequired(reader), ReadOptional(reader),
          ReadOptional(reader), ReadOptional(reader), ReadOptionalInt(reader), ReadOptionalInt(reader),
          ReadOptional(reader), formatVersion >= FormatVersion ? ReadFlowSummaryLabel(reader) : null)).ToArray();
        var symbols = Enumerable.Range(0, reader.ReadInt32()).Select(_ => new CpgSymbolLocation(
          ReadRequired(reader), reader.ReadInt32())).ToArray();
        var localIndexes = new HashSet<int>(nodes.Select(node => node.LocalIndex));
        if (stream.Position != stream.Length || localIndexes.Count != nodes.Length ||
            edges.Any(edge => !localIndexes.Contains(edge.SourceLocalIndex) ||
              !localIndexes.Contains(edge.TargetLocalIndex)))
        {
            throw new InvalidDataException("The CPG shard contains an invalid section or orphan edge.");
        }

        if (!Enum.IsDefined(role) || (role == CpgShardRole.BoundaryAdjacency && adjacency is null))
        {
            throw new InvalidDataException("The CPG shard boundary-adjacency metadata is invalid.");
        }

        if (incomingEdgeOffsets is not null && incomingEdgeIndexes is not null)
        {
            CpgFrozenShardIncomingEdgeIndex.Validate(nodes, edges, incomingEdgeOffsets, incomingEdgeIndexes);
        }

        return new CpgFrozenShard(
          lookup,
          nodes,
          edges,
          symbols,
          boundaryEdges,
          role,
          adjacency,
          incomingEdgeOffsets,
          incomingEdgeIndexes);
    }

    private static void ValidateFile(string shardPath, string expectedShardHash, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedShardHash))
        {
            throw new InvalidDataException("The CPG shard failed post-write validation.");
        }

        var validationStopwatch = Stopwatch.StartNew();
        using var stream = new FileStream(
          shardPath,
          FileMode.Open,
          FileAccess.Read,
          FileShare.Read,
          StreamBufferSize,
          FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var reader = new CpgShardPayloadReader(stream, hash, cancellationToken);
        ValidatePayload(reader);
        var actualShardHash = Convert.ToHexString(hash.GetHashAndReset());
        if (!string.Equals(actualShardHash, expectedShardHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The CPG shard hash does not match the bytes written to disk.");
        }

        validationStopwatch.Stop();
    }

    private static void ValidateTemporaryHeader(string temporaryPath, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new FileStream(
          temporaryPath,
          FileMode.Open,
          FileAccess.Read,
          FileShare.Read,
          StreamBufferSize,
          FileOptions.SequentialScan);
        Span<byte> header = stackalloc byte[Magic.Length + sizeof(int)];
        stream.ReadExactly(header);
        if (!header[..Magic.Length].SequenceEqual(Magic) ||
            !IsSupportedFormatVersion(BinaryPrimitives.ReadInt32LittleEndian(header[Magic.Length..])))
        {
            throw new InvalidDataException("The CPG shard header is unsupported or corrupt.");
        }

        stopwatch.Stop();
    }

    private static void ValidatePayload(CpgShardPayloadReader reader)
    {
        foreach (var value in Magic)
        {
            if (reader.ReadByte() != value)
            {
                throw new InvalidDataException("The CPG shard header is unsupported or corrupt.");
            }
        }

        var formatVersion = reader.ReadInt32();
        if (!IsSupportedFormatVersion(formatVersion))
        {
            throw new InvalidDataException("The CPG shard header is unsupported or corrupt.");
        }

        ReadLookup(reader);
        var role = (CpgShardRole)reader.ReadInt32();
        var ownerFragmentIdPresent = reader.ReadOptionalString();
        var direction = reader.ReadInt32();
        if (!Enum.IsDefined(role) ||
            (ownerFragmentIdPresent && !Enum.IsDefined((CpgBoundaryAdjacencyDirection)direction)) ||
            (role == CpgShardRole.BoundaryAdjacency && !ownerFragmentIdPresent))
        {
            throw new InvalidDataException("The CPG shard boundary-adjacency metadata is invalid.");
        }

        var nodeCount = reader.ReadCount();
        if (nodeCount > MaxPooledLocalIndexCount)
        {
            throw new InvalidDataException("The CPG shard local-index section exceeds the validation limit.");
        }

        var localIndexes = ArrayPool<byte>.Shared.Rent(nodeCount);
        try
        {
            localIndexes.AsSpan(0, nodeCount).Clear();
            for (var index = 0; index < nodeCount; index += 1)
            {
                var localIndex = reader.ReadInt32();
                if ((uint)localIndex >= (uint)nodeCount || localIndexes[localIndex] != 0)
                {
                    throw new InvalidDataException("The CPG shard contains an invalid local node index.");
                }

                localIndexes[localIndex] = 1;
                reader.ReadUInt32();
                reader.ReadRequiredString();
                reader.ReadOptionalString();
                reader.ReadOptionalInt32();
                reader.ReadOptionalInt32();
                reader.ReadRequiredString();
                reader.ReadOptionalString();
                reader.ReadOptionalString();
                reader.ReadOptionalString();
                reader.ReadBoolean();
                reader.ReadUInt32();
                reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadUInt32();
            }

            var edgeCount = reader.ReadCount();
            var edgeTargets = formatVersion >= IncomingEdgeIndexFormatVersion
              ? ArrayPool<int>.Shared.Rent(edgeCount)
              : null;
            for (var index = 0; index < edgeCount; index += 1)
            {
                var sourceLocalIndex = reader.ReadInt32();
                var targetLocalIndex = reader.ReadInt32();
                if ((uint)sourceLocalIndex >= (uint)nodeCount ||
                    (uint)targetLocalIndex >= (uint)nodeCount ||
                    localIndexes[sourceLocalIndex] == 0 ||
                    localIndexes[targetLocalIndex] == 0)
                {
                    throw new InvalidDataException("The CPG shard contains an orphan local edge.");
                }

                edgeTargets?[index] = targetLocalIndex;
                ReadEdgePayload(reader);
                if (formatVersion >= FormatVersion)
                {
                    ReadFlowSummaryLabel(reader);
                }
            }

            if (formatVersion >= IncomingEdgeIndexFormatVersion)
            {
                ValidateIncomingEdgeIndex(reader, nodeCount, edgeCount, edgeTargets!);
            }

            var boundaryEdgeCount = reader.ReadCount();
            for (var index = 0; index < boundaryEdgeCount; index += 1)
            {
                reader.ReadUInt32();
                reader.ReadUInt32();
                ReadEdgePayload(reader);
                if (formatVersion >= FormatVersion)
                {
                    ReadFlowSummaryLabel(reader);
                }
            }

            var symbolCount = reader.ReadCount();
            for (var index = 0; index < symbolCount; index += 1)
            {
                reader.ReadRequiredString();
                var localIndex = reader.ReadInt32();
                if ((uint)localIndex >= (uint)nodeCount || localIndexes[localIndex] == 0)
                {
                    throw new InvalidDataException("The CPG shard contains an orphan symbol location.");
                }
            }

            reader.AssertEndOfFile();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(localIndexes, clearArray: true);
        }
    }

    private static bool IsSupportedFormatVersion(int formatVersion)
    {
        return formatVersion is LegacyFormatVersion or IncomingEdgeIndexFormatVersion or FormatVersion;
    }

    private static void ValidateIncomingEdgeIndex(CpgShardPayloadReader reader, int nodeCount, int edgeCount, int[] edgeTargets)
    {
        var offsets = ArrayPool<int>.Shared.Rent(nodeCount + 1);
        var seen = ArrayPool<byte>.Shared.Rent(edgeCount);
        try
        {
            for (var index = 0; index <= nodeCount; index += 1)
            {
                offsets[index] = reader.ReadInt32();
            }

            if (offsets[0] != 0 || offsets[nodeCount] != edgeCount)
            {
                throw new InvalidDataException("The CPG shard incoming-edge index is invalid.");
            }

            var previousOffset = 0;
            for (var targetLocalIndex = 0; targetLocalIndex < nodeCount; targetLocalIndex += 1)
            {
                var nextOffset = offsets[targetLocalIndex + 1];
                if (nextOffset < previousOffset || nextOffset > edgeCount)
                {
                    throw new InvalidDataException("The CPG shard incoming-edge offsets are invalid.");
                }

                previousOffset = nextOffset;
            }

            seen.AsSpan(0, edgeCount).Clear();
            var currentTargetLocalIndex = 0;
            var previousEdgeIndex = -1;
            for (var position = 0; position < edgeCount; position += 1)
            {
                while (currentTargetLocalIndex + 1 < offsets.Length &&
                       position >= offsets[currentTargetLocalIndex + 1])
                {
                    currentTargetLocalIndex += 1;
                    previousEdgeIndex = -1;
                }

                var edgeIndex = reader.ReadInt32();
                if ((uint)edgeIndex >= (uint)edgeCount || seen[edgeIndex] != 0)
                {
                    throw new InvalidDataException("The CPG shard incoming-edge index is invalid.");
                }

                if (edgeTargets[edgeIndex] != currentTargetLocalIndex || edgeIndex < previousEdgeIndex)
                {
                    throw new InvalidDataException("The CPG shard incoming-edge index is invalid.");
                }

                seen[edgeIndex] = 1;
                previousEdgeIndex = edgeIndex;
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(offsets);
            ArrayPool<byte>.Shared.Return(seen, clearArray: true);
            ArrayPool<int>.Shared.Return(edgeTargets);
        }
    }

    private static void ReadLookup(CpgShardPayloadReader reader)
    {
        reader.ReadRequiredString();
        reader.ReadRequiredString();
        reader.ReadRequiredString();
        reader.ReadRequiredString();
        reader.ReadInt32();
        reader.ReadInt32();
        reader.ReadRequiredString();
        reader.ReadInt32();
        reader.ReadRequiredString();
    }

    private static void ReadEdgePayload(CpgShardPayloadReader reader)
    {
        reader.ReadRequiredString();
        reader.ReadOptionalString();
        reader.ReadOptionalString();
        reader.ReadOptionalString();
        reader.ReadOptionalInt32();
        reader.ReadOptionalInt32();
        reader.ReadOptionalString();
    }

    private static void WriteFlowSummaryLabel(BinaryWriter writer, CpgFrozenFlowSummaryLabel? label)
    {
        writer.Write(label is not null);
        if (label is null)
        {
            return;
        }

        writer.Write((int)label.BridgeKind);
        writer.Write((int)label.Resolution);
        WriteRequired(writer, label.MethodKey);
        WriteFlowSummaryEndpoint(writer, label.Source);
        WriteFlowSummaryEndpoint(writer, label.Target);
    }

    private static CpgFrozenFlowSummaryLabel? ReadFlowSummaryLabel(BinaryReader reader)
    {
        if (!reader.ReadBoolean())
        {
            return null;
        }

        var bridgeKind = (NLCPGInterproceduralBridgeKind)reader.ReadInt32();
        var resolution = (FlowSummaryResolution)reader.ReadInt32();
        var methodKey = ReadRequired(reader);
        var source = ReadFlowSummaryEndpoint(reader);
        var target = ReadFlowSummaryEndpoint(reader);
        if (!Enum.IsDefined(bridgeKind) || !Enum.IsDefined(resolution))
        {
            throw new InvalidDataException("The CPG shard contains an invalid flow summary label.");
        }

        return new CpgFrozenFlowSummaryLabel(bridgeKind, resolution, methodKey, source, target);
    }

    private static void WriteFlowSummaryEndpoint(BinaryWriter writer, FlowSummaryEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        writer.Write((int)endpoint.Kind);
        writer.Write(endpoint.ParameterOrdinal);
    }

    private static FlowSummaryEndpoint ReadFlowSummaryEndpoint(BinaryReader reader)
    {
        var kind = (FlowSummaryEndpointKind)reader.ReadInt32();
        var ordinal = reader.ReadInt32();
        if (!Enum.IsDefined(kind))
        {
            throw new InvalidDataException("The CPG shard contains an invalid flow summary endpoint.");
        }

        return new FlowSummaryEndpoint(kind, ordinal);
    }

    private static void ReadFlowSummaryLabel(CpgShardPayloadReader reader)
    {
        if (!reader.ReadBoolean())
        {
            return;
        }

        reader.ReadInt32();
        reader.ReadInt32();
        reader.ReadRequiredString();
        ReadFlowSummaryEndpoint(reader);
        ReadFlowSummaryEndpoint(reader);
    }

    private static void ReadFlowSummaryEndpoint(CpgShardPayloadReader reader)
    {
        reader.ReadInt32();
        reader.ReadInt32();
    }

    private static void WriteLookup(BinaryWriter writer, CpgShardLookup lookup)
    {
        WriteRequired(writer, lookup.File.ProjectId);
        WriteRequired(writer, lookup.File.RelativePath);
        WriteRequired(writer, lookup.File.SourceHash);
        WriteRequired(writer, lookup.Fragment.Kind);
        writer.Write(lookup.Fragment.SpanStart);
        writer.Write(lookup.Fragment.SpanLength);
        WriteRequired(writer, lookup.Fragment.FragmentHash);
        writer.Write(lookup.SchemaVersion);
        WriteRequired(writer, lookup.ProfileHash);
    }

    private static CpgShardLookup ReadLookup(BinaryReader reader)
    {
        var file = new CpgFileKey(ReadRequired(reader), ReadRequired(reader), ReadRequired(reader));
        var fragment = new CpgFragmentKey(ReadRequired(reader), reader.ReadInt32(), reader.ReadInt32(), ReadRequired(reader));
        return new CpgShardLookup(file, fragment, reader.ReadInt32(), ReadRequired(reader));
    }

    private static void WriteRequired(BinaryWriter writer, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        writer.Write(value);
    }

    private static string ReadRequired(BinaryReader reader)
    {
        var value = reader.ReadString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException("A required CPG shard value is missing.");
        }

        return value;
    }

    private static void WriteOptional(BinaryWriter writer, string? value)
    {
        writer.Write(value is not null);
        if (value is not null)
        {
            writer.Write(value);
        }
    }

    private static string? ReadOptional(BinaryReader reader)
    {
        return reader.ReadBoolean() ? reader.ReadString() : null;
    }

    private static void WriteOptionalInt(BinaryWriter writer, int? value)
    {
        writer.Write(value.HasValue);
        if (value.HasValue)
        {
            writer.Write(value.Value);
        }
    }

    private static int? ReadOptionalInt(BinaryReader reader)
    {
        return reader.ReadBoolean() ? reader.ReadInt32() : null;
    }

    private readonly record struct ShardPayloadWriteResult(string Hash, long Length);

    private readonly record struct ShardPayloadReadResult(CpgFrozenShard Shard, string Hash, long Length);

    // 将序列化字节直接写入文件，并同步更新增量哈希，避免暂存完整 payload。
    private sealed class CpgHashingWriteStream : Stream
    {
        private readonly Stream _stream;
        private readonly IncrementalHash _hash;

        internal CpgHashingWriteStream(Stream stream, IncrementalHash hash)
        {
            _stream = stream;
            _hash = hash;
        }

        public override bool CanRead => false;

        public override bool CanSeek => _stream.CanSeek;

        public override bool CanWrite => _stream.CanWrite;

        public override long Length => _stream.Length;

        public override long Position
        {
            get => _stream.Position;
            set => _stream.Position = value;
        }

        public override void Flush()
        {
            _stream.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return _stream.FlushAsync(cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _stream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            _stream.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _hash.AppendData(buffer, offset, count);
            _stream.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _hash.AppendData(buffer);
            _stream.Write(buffer);
        }

        public override Task WriteAsync(
          byte[] buffer,
          int offset,
          int count,
          CancellationToken cancellationToken)
        {
            _hash.AppendData(buffer, offset, count);
            return _stream.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(
          ReadOnlyMemory<byte> buffer,
          CancellationToken cancellationToken = default)
        {
            _hash.AppendData(buffer.Span);
            return _stream.WriteAsync(buffer, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            // The owning method disposes the underlying file stream after the writer exits.
        }
    }

    // 从文件流增量反序列化分片，同时避免把整个 payload 保留在 byte[] 中。
    private sealed class CpgHashingReadStream : Stream
    {
        private readonly Stream _stream;
        private readonly IncrementalHash _hash;
        private readonly CancellationToken _cancellationToken;

        internal CpgHashingReadStream(Stream stream, IncrementalHash hash, CancellationToken cancellationToken)
        {
            _stream = stream;
            _hash = hash;
            _cancellationToken = cancellationToken;
        }

        public override bool CanRead => _stream.CanRead;

        public override bool CanSeek => _stream.CanSeek;

        public override bool CanWrite => false;

        public override long Length => _stream.Length;

        public override long Position
        {
            get => _stream.Position;
            set => _stream.Position = value;
        }

        public override void Flush()
        {
            _stream.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var read = _stream.Read(buffer, offset, count);
            if (read > 0)
            {
                _hash.AppendData(buffer, offset, read);
            }

            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var read = _stream.Read(buffer);
            if (read > 0)
            {
                _hash.AppendData(buffer[..read]);
            }

            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _stream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            // The owning method disposes the underlying file stream after the reader exits.
        }
    }

    private sealed class CpgShardPayloadReader : IDisposable
    {
        private readonly Stream _stream;
        private readonly IncrementalHash _hash;
        private readonly CancellationToken _cancellationToken;
        private readonly byte[] _buffer;
        private int _offset;
        private int _count;

        internal long ReadBackMilliseconds { get; private set; }
        internal long HashMilliseconds { get; private set; }

        internal CpgShardPayloadReader(Stream stream, IncrementalHash hash, CancellationToken cancellationToken)
        {
            _stream = stream;
            _hash = hash;
            _cancellationToken = cancellationToken;
            _buffer = ArrayPool<byte>.Shared.Rent(StreamBufferSize);
        }

        // 归还读取缓冲区。
        public void Dispose()
        {
            ArrayPool<byte>.Shared.Return(_buffer);
        }

        internal byte ReadByte()
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (_offset == _count)
            {
                var readStopwatch = Stopwatch.StartNew();
                _count = _stream.Read(_buffer, 0, _buffer.Length);
                readStopwatch.Stop();
                ReadBackMilliseconds += readStopwatch.ElapsedMilliseconds;
                _offset = 0;
                if (_count == 0)
                {
                    throw new InvalidDataException("The CPG shard ended before its declared section was complete.");
                }

                var hashStopwatch = Stopwatch.StartNew();
                _hash.AppendData(_buffer, 0, _count);
                hashStopwatch.Stop();
                HashMilliseconds += hashStopwatch.ElapsedMilliseconds;
            }

            var value = _buffer[_offset];
            _offset += 1;
            return value;
        }

        internal bool ReadBoolean()
        {
            return ReadByte() != 0;
        }

        internal int ReadInt32()
        {
            return ReadByte() |
              (ReadByte() << 8) |
              (ReadByte() << 16) |
              (ReadByte() << 24);
        }

        internal uint ReadUInt32()
        {
            return (uint)ReadByte() |
              ((uint)ReadByte() << 8) |
              ((uint)ReadByte() << 16) |
              ((uint)ReadByte() << 24);
        }

        internal int ReadCount()
        {
            var count = ReadInt32();
            if (count < 0)
            {
                throw new InvalidDataException("The CPG shard contains a negative section count.");
            }

            return count;
        }

        internal bool ReadOptionalString()
        {
            var present = ReadBoolean();
            if (present)
            {
                ReadString(required: false);
            }

            return present;
        }

        internal void ReadRequiredString()
        {
            ReadString(required: true);
        }

        internal void ReadOptionalInt32()
        {
            if (ReadBoolean())
            {
                ReadInt32();
            }
        }

        internal void AssertEndOfFile()
        {
            if (_offset != _count || _stream.ReadByte() >= 0)
            {
                throw new InvalidDataException("The CPG shard contains trailing data.");
            }
        }

        private void ReadString(bool required)
        {
            var byteCount = Read7BitEncodedInt();
            var hasNonWhitespaceRune = false;
            Span<byte> runeBytes = stackalloc byte[4];
            for (var remaining = byteCount; remaining > 0;)
            {
                runeBytes[0] = ReadByte();
                var runeByteCount = GetUtf8RuneByteCount(runeBytes[0]);
                if (runeByteCount > remaining)
                {
                    throw new InvalidDataException("The CPG shard contains invalid UTF-8 string data.");
                }

                for (var index = 1; index < runeByteCount; index += 1)
                {
                    runeBytes[index] = ReadByte();
                }

                if (Rune.DecodeFromUtf8(runeBytes[..runeByteCount], out var rune, out var consumed) !=
                      OperationStatus.Done ||
                    consumed != runeByteCount)
                {
                    throw new InvalidDataException("The CPG shard contains invalid UTF-8 string data.");
                }

                if (!Rune.IsWhiteSpace(rune))
                {
                    hasNonWhitespaceRune = true;
                }

                remaining -= runeByteCount;
            }

            if (required && !hasNonWhitespaceRune)
            {
                throw new InvalidDataException("A required CPG shard value is missing.");
            }
        }

        private int Read7BitEncodedInt()
        {
            var value = 0;
            for (var shift = 0; shift < 35; shift += 7)
            {
                var current = ReadByte();
                value |= (current & 0x7f) << shift;
                if ((current & 0x80) == 0)
                {
                    if (value < 0)
                    {
                        throw new InvalidDataException("The CPG shard string length is invalid.");
                    }

                    return value;
                }
            }

            throw new InvalidDataException("The CPG shard string length is invalid.");
        }

        private static int GetUtf8RuneByteCount(byte firstByte)
        {
            return firstByte switch
            {
                < 0x80 => 1,
                >= 0xc2 and <= 0xdf => 2,
                >= 0xe0 and <= 0xef => 3,
                >= 0xf0 and <= 0xf4 => 4,
                _ => throw new InvalidDataException("The CPG shard contains invalid UTF-8 string data."),
            };
        }
    }
}
