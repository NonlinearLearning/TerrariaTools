using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using MinimalRoslynCpg.Builder;

namespace MinimalRoslynCpg.Persistence;

public sealed class CpgShardStore : ICpgShardStore
{
  private static readonly byte[] Magic = "CPGB"u8.ToArray();
  private const int StreamBufferSize = 64 * 1024;
  private const int MaxPooledLocalIndexCount = 16 * 1024 * 1024;
  private const int FormatVersion = 5;
  private static Action<string>? _afterTemporaryWriteForTesting;
  private static Action<CpgShardLocation>? _afterReadForTesting;
  private readonly CpgPersistenceDurabilityMode _durabilityMode;
  private readonly string _shardsRoot;

  public CpgShardStore(
    string storeRoot,
    CpgPersistenceDurabilityMode durabilityMode = CpgPersistenceDurabilityMode.Strict)
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

  public async Task<CpgShardWriteResult> WriteAsync(CpgFrozenShard shard, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(shard);
    cancellationToken.ThrowIfCancellationRequested();
    var shardId = CreateShardId(shard.Lookup);
    var directory = Path.Combine(_shardsRoot, shardId[..2]);
    Directory.CreateDirectory(directory);
    var finalPath = Path.Combine(directory, $"{shardId}.cpgbin");
    var temporaryPath = finalPath + ".tmp";
    var serializationStopwatch = Stopwatch.StartNew();
    var payload = Serialize(shard);
    serializationStopwatch.Stop();
    var shardHash = Convert.ToHexString(SHA256.HashData(payload));
    var flushMilliseconds = 0L;
    var validationMilliseconds = 0L;
    var readBackMilliseconds = 0L;
    var hashMilliseconds = 0L;
    var structuralValidationMilliseconds = 0L;

    try
    {
      await using (var stream = new FileStream(
        temporaryPath,
        FileMode.Create,
        FileAccess.Write,
        FileShare.None,
        StreamBufferSize,
        FileOptions.SequentialScan))
      {
        await stream.WriteAsync(payload, cancellationToken);
        var flushStopwatch = Stopwatch.StartNew();
        stream.Flush(flushToDisk: _durabilityMode == CpgPersistenceDurabilityMode.Strict);
        flushStopwatch.Stop();
        flushMilliseconds += flushStopwatch.ElapsedMilliseconds;
      }

      Volatile.Read(ref _afterTemporaryWriteForTesting)?.Invoke(temporaryPath);

      var validationStopwatch = Stopwatch.StartNew();
      if (_durabilityMode == CpgPersistenceDurabilityMode.Strict)
      {
        var validation = ValidateFile(temporaryPath, shardHash, cancellationToken);
        readBackMilliseconds += validation.ReadBackMilliseconds;
        hashMilliseconds += validation.HashMilliseconds;
        structuralValidationMilliseconds += validation.StructuralValidationMilliseconds;
      }
      else
      {
        readBackMilliseconds += ValidateTemporaryHeader(temporaryPath, cancellationToken);
      }
      validationStopwatch.Stop();
      validationMilliseconds += validationStopwatch.ElapsedMilliseconds;
      File.Move(temporaryPath, finalPath, overwrite: true);
      var location = new CpgShardLocation(
        shardId,
        finalPath,
        shardHash,
        payload.LongLength,
        CpgShardStatus.Complete);
      return new CpgShardWriteResult(location, new CpgShardWriteTelemetry(
        serializationStopwatch.ElapsedMilliseconds,
        validationMilliseconds,
        flushMilliseconds,
        readBackMilliseconds,
        hashMilliseconds,
        structuralValidationMilliseconds));
    }
    finally
    {
      if (File.Exists(temporaryPath))
      {
        File.Delete(temporaryPath);
      }
    }
  }

  public async Task<CpgFrozenShard> ReadAsync(CpgShardLocation location, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(location);
    if (location.Status != CpgShardStatus.Complete)
    {
      throw new InvalidOperationException("Only complete CPG shards can be read.");
    }

    Volatile.Read(ref _afterReadForTesting)?.Invoke(location);
    var payload = await File.ReadAllBytesAsync(location.ShardPath, cancellationToken);
    var shardHash = Convert.ToHexString(SHA256.HashData(payload));
    if (!string.Equals(shardHash, location.ShardHash, StringComparison.Ordinal))
    {
      throw new InvalidDataException("The CPG shard hash does not match its catalog location.");
    }

    return Deserialize(payload);
  }

  internal CpgShardReadBackValidationTelemetry Validate(
    CpgShardLocation location,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(location);
    if (location.Status != CpgShardStatus.Complete)
    {
      throw new InvalidOperationException("Only complete CPG shards can be validated.");
    }

    return ValidateFile(location.ShardPath, location.ShardHash, cancellationToken);
  }

  public async Task<CpgFrozenShard?> TryReadAsync(
    CpgShardLocation location,
    CpgShardLookup lookup,
    CancellationToken cancellationToken)
  {
    var shard = await ReadAsync(location, cancellationToken);
    return shard.Lookup == lookup ? shard : null;
  }

  public async Task<(CpgFrozenShard Shard, CpgShardLocation Location)> ReadFromPathAsync(
    string shardPath,
    CancellationToken cancellationToken)
  {
    var payload = await File.ReadAllBytesAsync(shardPath, cancellationToken);
    var shard = Deserialize(payload);
    var location = new CpgShardLocation(
      CreateShardId(shard.Lookup),
      shardPath,
      Convert.ToHexString(SHA256.HashData(payload)),
      payload.LongLength,
      CpgShardStatus.Complete);
    return (shard, location);
  }

  private static string CreateShardId(CpgShardLookup lookup)
  {
    var identity = string.Join("|", lookup.File.ProjectId, lookup.File.RelativePath,
      lookup.File.SourceHash, lookup.Fragment.Kind, lookup.Fragment.SpanStart,
      lookup.Fragment.SpanLength, lookup.Fragment.FragmentHash, lookup.SchemaVersion,
      lookup.ProfileHash);
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
  }

  private static byte[] Serialize(CpgFrozenShard shard)
  {
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
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
    }

    writer.Write(shard.SymbolLocations.Count);
    foreach (var location in shard.SymbolLocations.OrderBy(location => location.SymbolKey, StringComparer.Ordinal))
    {
      WriteRequired(writer, location.SymbolKey);
      writer.Write(location.LocalIndex);
    }

    writer.Flush();
    return stream.ToArray();
  }

  private static CpgFrozenShard Deserialize(byte[] payload)
  {
    using var stream = new MemoryStream(payload, writable: false);
    using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
    if (!reader.ReadBytes(Magic.Length).SequenceEqual(Magic) || reader.ReadInt32() != FormatVersion)
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
      ReadOptional(reader))).ToArray();
    var boundaryEdges = Enumerable.Range(0, reader.ReadInt32()).Select(_ => new CpgFrozenBoundaryEdge(
      reader.ReadUInt32(), reader.ReadUInt32(), ReadRequired(reader), ReadOptional(reader),
      ReadOptional(reader), ReadOptional(reader), ReadOptionalInt(reader), ReadOptionalInt(reader),
      ReadOptional(reader))).ToArray();
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

    return new CpgFrozenShard(lookup, nodes, edges, symbols, boundaryEdges, role, adjacency);
  }

  private static CpgShardReadBackValidationTelemetry ValidateFile(
    string shardPath,
    string expectedShardHash,
    CancellationToken cancellationToken)
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
    var readBackMilliseconds = reader.ReadBackMilliseconds;
    var hashMilliseconds = reader.HashMilliseconds;
    return new CpgShardReadBackValidationTelemetry(
      readBackMilliseconds,
      hashMilliseconds,
      Math.Max(0, validationStopwatch.ElapsedMilliseconds - readBackMilliseconds - hashMilliseconds));
  }

  private static long ValidateTemporaryHeader(string temporaryPath, CancellationToken cancellationToken)
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
        BinaryPrimitives.ReadInt32LittleEndian(header[Magic.Length..]) != FormatVersion)
    {
      throw new InvalidDataException("The CPG shard header is unsupported or corrupt.");
    }

    stopwatch.Stop();
    return stopwatch.ElapsedMilliseconds;
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

    if (reader.ReadInt32() != FormatVersion)
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

        ReadEdgePayload(reader);
      }

      var boundaryEdgeCount = reader.ReadCount();
      for (var index = 0; index < boundaryEdgeCount; index += 1)
      {
        reader.ReadUInt32();
        reader.ReadUInt32();
        ReadEdgePayload(reader);
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

    internal CpgShardPayloadReader(
      Stream stream,
      IncrementalHash hash,
      CancellationToken cancellationToken)
    {
      _stream = stream;
      _hash = hash;
      _cancellationToken = cancellationToken;
      _buffer = ArrayPool<byte>.Shared.Rent(StreamBufferSize);
    }

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

  internal readonly record struct CpgShardReadBackValidationTelemetry(
    long ReadBackMilliseconds,
    long HashMilliseconds,
    long StructuralValidationMilliseconds);
}
