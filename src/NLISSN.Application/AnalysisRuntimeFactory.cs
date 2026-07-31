using NLISSN.Core.Pipeline;

namespace NLISSN.Application;

public static class AnalysisRuntimeFactory
{
  public static AnalysisRuntime CreateFromOptions(
    IReadOnlyDictionary<string, string> options)
  {
    ArgumentNullException.ThrowIfNull(options);

    var executionOptions = new RoslynPrototypeExecutionOptions(
      ResolveMaxDegreeOfParallelism(options),
      EnableDirectoryParallelism: !IsTrueOption(options, "disable-directory-parallelism"),
      EnableGroupParallelism: IsTrueOption(options, "enable-group-parallelism"),
      EnableHelperParallelism: !IsTrueOption(options, "disable-helper-parallelism"),
      CpgMaxDegreeOfParallelism: ResolveCpgMaxDegreeOfParallelism(options));
    return new AnalysisRuntime(executionOptions, new AnalysisEpoch(0, 0, 0));
  }

  private static bool IsTrueOption(IReadOnlyDictionary<string, string> options, string key)
  {
    return options.TryGetValue(key, out var rawValue) &&
      string.Equals(rawValue, "true", StringComparison.OrdinalIgnoreCase);
  }

  private static int ResolveMaxDegreeOfParallelism(
    IReadOnlyDictionary<string, string> options)
  {
    if (!options.TryGetValue("max-degree-of-parallelism", out var rawValue) ||
        string.IsNullOrWhiteSpace(rawValue) ||
        !int.TryParse(rawValue, out var parsedValue))
    {
      return Math.Max(1, Environment.ProcessorCount);
    }

    return Math.Max(1, parsedValue);
  }

  private static int? ResolveCpgMaxDegreeOfParallelism(
    IReadOnlyDictionary<string, string> options)
  {
    if (!options.TryGetValue("cpg-max-degree-of-parallelism", out var rawValue))
    {
      return null;
    }

    if (!int.TryParse(rawValue, out var parsedValue) || parsedValue <= 0)
    {
      throw new ArgumentException(
        "--cpg-max-degree-of-parallelism requires a positive integer.");
    }

    return parsedValue;
  }
}
