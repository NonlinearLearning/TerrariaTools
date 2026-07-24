using System.Security.Cryptography;
using System.Text;

namespace MinimalRoslynCpg.Persistence;

public sealed class CpgBuildRoutingIndexReader
{
  private const int HashLength = 32;
  private const int MaximumRouteCount = 100000000;
  private static readonly byte[] Magic = "CPGI"u8.ToArray();

  public async Task<CpgBuildRoutingIndex> ReadAsync(string indexPath, CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(indexPath);
    byte[] bytes;
    try
    {
      bytes = await File.ReadAllBytesAsync(indexPath, cancellationToken);
    }
    catch (EndOfStreamException exception)
    {
      throw new InvalidDataException("The CPG routing index is truncated.", exception);
    }

    try
    {
      using var stream = new MemoryStream(bytes, writable: false);
      using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
      if (!reader.ReadBytes(Magic.Length).SequenceEqual(Magic))
      {
        throw new InvalidDataException("The CPG routing index magic is invalid.");
      }

      var version = reader.ReadByte();
      if (version != CpgBuildRoutingIndexWriter.FormatVersion)
      {
        throw new InvalidDataException("The CPG routing index format version is unsupported.");
      }

      var buildId = reader.ReadString();
      var schemaVersion = reader.ReadInt32();
      var profileHash = reader.ReadString();
      var payloadLength = reader.ReadInt32();
      var expectedHash = reader.ReadBytes(HashLength);
      if (schemaVersion <= 0 || payloadLength < 0 || expectedHash.Length != HashLength ||
          stream.Length - stream.Position != payloadLength)
      {
        throw new InvalidDataException("The CPG routing index header is invalid.");
      }

      var payload = reader.ReadBytes(payloadLength);
      var actualHash = SHA256.HashData(payload);
      if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
      {
        throw new InvalidDataException("The CPG routing index payload hash does not match.");
      }

      return ReadPayload(buildId, schemaVersion, profileHash, Convert.ToHexString(actualHash).ToLowerInvariant(), payload);
    }
    catch (EndOfStreamException exception)
    {
      throw new InvalidDataException("The CPG routing index is truncated.", exception);
    }
  }

  private static CpgBuildRoutingIndex ReadPayload(
    string buildId,
    int schemaVersion,
    string profileHash,
    string payloadHash,
    byte[] payload)
  {
    using var stream = new MemoryStream(payload, writable: false);
    using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
    var primaryNodes = ReadRoutes(reader, static source => new CpgBuildRoutingPrimaryNodeRoute(
      source.ReadUInt32(), source.ReadString(), source.ReadInt32()));
    var boundaryNodes = ReadRoutes(reader, static source => new CpgBuildRoutingBoundaryNodeRoute(
      source.ReadUInt32(), source.ReadString()));
    var spans = ReadRoutes(reader, static source => new CpgBuildRoutingSpanRoute(
      new CpgFileKey(source.ReadString(), source.ReadString(), source.ReadString()),
      source.ReadInt32(), source.ReadInt32(), source.ReadString()));
    var symbols = ReadRoutes(reader, static source => new CpgBuildRoutingSymbolRoute(
      source.ReadString(), source.ReadString(), source.ReadInt32()));
    if (stream.Position != stream.Length)
    {
      throw new InvalidDataException("The CPG routing index payload has trailing data.");
    }

    return new CpgBuildRoutingIndex(
      buildId,
      schemaVersion,
      profileHash,
      payloadHash,
      primaryNodes,
      boundaryNodes,
      spans,
      symbols);
  }

  private static IReadOnlyList<T> ReadRoutes<T>(BinaryReader reader, Func<BinaryReader, T> readRoute)
  {
    var count = reader.ReadInt32();
    if (count < 0 || count > MaximumRouteCount)
    {
      throw new InvalidDataException("The CPG routing index route count is invalid.");
    }

    var routes = new T[count];
    for (var index = 0; index < count; index += 1)
    {
      routes[index] = readRoute(reader);
    }

    return routes;
  }
}
