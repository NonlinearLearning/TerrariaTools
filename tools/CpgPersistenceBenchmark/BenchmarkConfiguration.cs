using MinimalRoslynCpg.Builder;

namespace CpgPersistenceBenchmark;

public sealed record BenchmarkConfiguration(
  int WarmupCount,
  int SampleCount,
  IReadOnlyList<int> DegreesOfParallelism,
  IReadOnlyList<int> ShardExportConcurrencyLimits,
  IReadOnlyList<int> FileWriteConcurrencyLimits,
  IReadOnlyList<int> CatalogBatchRowLimits,
  IReadOnlyList<CpgPersistenceDurabilityMode> DurabilityModes,
  IReadOnlyList<string> Fixtures,
  string? SourceRoot,
  int? SourceRootMaxFiles,
  long? SourceRootMaxBytes,
  string? TemporaryStoreRoot,
  string? OutputPath)
{
  public static BenchmarkConfiguration Parse(string[] args)
  {
    var outputIndex = Array.IndexOf(args, "--output");
    return new BenchmarkConfiguration(
      WarmupCount: ParseNonNegativeInteger(args, "--warmup-count", 1),
      SampleCount: ParsePositiveInteger(args, "--sample-count", 3),
      DegreesOfParallelism: ParsePositiveIntegerList(args, "--dop", new[] { 1, 8, 12, 14, 16 }),
      ShardExportConcurrencyLimits: ParsePositiveIntegerList(args, "--shard-export-concurrency", new[] { 2 }),
      FileWriteConcurrencyLimits: ParsePositiveIntegerList(args, "--file-write-concurrency", new[] { 2 }),
      CatalogBatchRowLimits: ParsePositiveIntegerList(args, "--catalog-batch-rows", new[] { 1024 }),
      DurabilityModes: ParseDurabilityModes(args),
      Fixtures: ParseStringList(args, "--fixture"),
      SourceRoot: ParseOptionalValue(args, "--source-root"),
      SourceRootMaxFiles: ParseOptionalPositiveInteger(args, "--source-root-max-files"),
      SourceRootMaxBytes: ParseOptionalPositiveLong(args, "--source-root-max-bytes"),
      TemporaryStoreRoot: ParseOptionalValue(args, "--temporary-store-root"),
      OutputPath: outputIndex >= 0 && outputIndex + 1 < args.Length ? args[outputIndex + 1] : null);
  }

  private static int? ParseOptionalPositiveInteger(IReadOnlyList<string> args, string optionName)
  {
    var value = ParseOptionalValue(args, optionName);
    return value is null ? null : int.TryParse(value, out var parsed) && parsed > 0
      ? parsed
      : throw new ArgumentException($"{optionName} must be a positive integer.", optionName);
  }

  private static long? ParseOptionalPositiveLong(IReadOnlyList<string> args, string optionName)
  {
    var value = ParseOptionalValue(args, optionName);
    return value is null ? null : long.TryParse(value, out var parsed) && parsed > 0
      ? parsed
      : throw new ArgumentException($"{optionName} must be a positive integer.", optionName);
  }

  private static string? ParseOptionalValue(IReadOnlyList<string> args, string optionName)
  {
    var optionIndex = Array.IndexOf(args.ToArray(), optionName);
    if (optionIndex < 0)
    {
      return null;
    }

    if (optionIndex + 1 >= args.Count || string.IsNullOrWhiteSpace(args[optionIndex + 1]))
    {
      throw new ArgumentException($"Missing value for {optionName}.", optionName);
    }

    return args[optionIndex + 1];
  }

  private static int ParseNonNegativeInteger(
    IReadOnlyList<string> args,
    string optionName,
    int defaultValue)
  {
    var optionIndex = Array.IndexOf(args.ToArray(), optionName);
    if (optionIndex < 0)
    {
      return defaultValue;
    }

    if (optionIndex + 1 >= args.Count)
    {
      throw new ArgumentException($"Missing value for {optionName}.", optionName);
    }

    return int.TryParse(args[optionIndex + 1], out var parsed) && parsed >= 0
      ? parsed
      : throw new ArgumentException($"{optionName} must be a non-negative integer.", optionName);
  }

  private static int ParsePositiveInteger(
    IReadOnlyList<string> args,
    string optionName,
    int defaultValue)
  {
    var optionIndex = Array.IndexOf(args.ToArray(), optionName);
    if (optionIndex < 0)
    {
      return defaultValue;
    }

    if (optionIndex + 1 >= args.Count)
    {
      throw new ArgumentException($"Missing value for {optionName}.", optionName);
    }

    return int.TryParse(args[optionIndex + 1], out var parsed) && parsed > 0
      ? parsed
      : throw new ArgumentException($"{optionName} must be a positive integer.", optionName);
  }

  private static IReadOnlyList<CpgPersistenceDurabilityMode> ParseDurabilityModes(IReadOnlyList<string> args)
  {
    var values = ParseStringList(args, "--durability");
    if (values.Count == 0)
    {
      return new[] { CpgPersistenceDurabilityMode.Strict, CpgPersistenceDurabilityMode.Throughput };
    }

    return values.Select(value => Enum.TryParse<CpgPersistenceDurabilityMode>(value, ignoreCase: true, out var mode)
      ? mode
      : throw new ArgumentException("--durability must contain Strict or Throughput.", "--durability"))
      .Distinct()
      .OrderBy(mode => mode)
      .ToArray();
  }

  private static IReadOnlyList<int> ParsePositiveIntegerList(
    IReadOnlyList<string> args,
    string optionName,
    IReadOnlyList<int> defaultValues)
  {
    var optionIndex = Array.IndexOf(args.ToArray(), optionName);
    if (optionIndex < 0)
    {
      return defaultValues;
    }

    if (optionIndex + 1 >= args.Count)
    {
      throw new ArgumentException($"Missing value for {optionName}.", optionName);
    }

    var values = args[optionIndex + 1]
      .Split(',', StringSplitOptions.TrimEntries)
      .Select(value => int.TryParse(value, out var parsed) && parsed > 0
        ? parsed
        : throw new ArgumentException($"{optionName} must contain positive integers.", optionName))
      .Distinct()
      .OrderBy(value => value)
      .ToArray();
    if (values.Length == 0)
    {
      throw new ArgumentException($"{optionName} must contain at least one value.", optionName);
    }

    return values;
  }

  private static IReadOnlyList<string> ParseStringList(IReadOnlyList<string> args, string optionName)
  {
    var optionIndex = Array.IndexOf(args.ToArray(), optionName);
    if (optionIndex < 0)
    {
      return Array.Empty<string>();
    }

    if (optionIndex + 1 >= args.Count)
    {
      throw new ArgumentException($"Missing value for {optionName}.", optionName);
    }

    var values = args[optionIndex + 1]
      .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .OrderBy(value => value, StringComparer.Ordinal)
      .ToArray();
    if (values.Length == 0)
    {
      throw new ArgumentException($"{optionName} must contain at least one value.", optionName);
    }

    return values;
  }
}
