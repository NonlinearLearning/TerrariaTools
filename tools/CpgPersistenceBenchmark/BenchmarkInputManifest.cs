using System.Security.Cryptography;
using System.Text;

namespace CpgPersistenceBenchmark;

public sealed record BenchmarkInputFile(string RelativePath, long ByteLength, string ContentHash);

public sealed record BenchmarkInputManifest(
  string? SourceRoot,
  int FileCount,
  long TotalBytes,
  string ContentHash,
  IReadOnlyList<BenchmarkInputFile> Files)
{
  public static BenchmarkInputManifest Create(
    IReadOnlyList<(string Source, string FilePath)> files,
    string? sourceRoot = null)
  {
    ArgumentNullException.ThrowIfNull(files);
    var manifestFiles = files
      .Select(file =>
      {
        var relativePath = file.FilePath.Replace('\\', '/');
        var bytes = Encoding.UTF8.GetBytes(file.Source);
        return new BenchmarkInputFile(
          relativePath,
          bytes.Length,
          Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
      })
      .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
      .ToArray();
    if (manifestFiles.Select(file => file.RelativePath).Distinct(StringComparer.Ordinal).Count() != manifestFiles.Length)
    {
      throw new ArgumentException("Benchmark input paths must be unique.", nameof(files));
    }

    var content = string.Join(
      "\n",
      manifestFiles.Select(file => $"{file.RelativePath}\t{file.ByteLength}\t{file.ContentHash}"));
    return new BenchmarkInputManifest(
      sourceRoot is null ? null : Path.GetFullPath(sourceRoot),
      manifestFiles.Length,
      manifestFiles.Sum(file => file.ByteLength),
      Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant(),
      manifestFiles);
  }
}
