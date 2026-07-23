namespace RoslynPrototype.Testing.TestInfrastructure;

public static class CpgSnapshotNormalizer
{
  private static readonly string[] VolatileFieldPrefixes = ["elapsedMs=", "path=", "ts="];

  public static IReadOnlyList<string> Normalize(IEnumerable<string> lines)
  {
    ArgumentNullException.ThrowIfNull(lines);
    return lines
      .Select(line => string.Join(
        ' ',
        line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
          .Where(token => !VolatileFieldPrefixes.Any(prefix => token.StartsWith(prefix, StringComparison.Ordinal)))))
      .Where(line => !string.IsNullOrWhiteSpace(line))
      .OrderBy(line => line, StringComparer.Ordinal)
      .ToArray();
  }
}
