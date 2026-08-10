namespace NLISSN.Infrastructure.Testing;

public enum TestDiffAssertionMode
{
    Golden,
    Structured
}

public sealed record TestDiffSnapshot
{
    public TestDiffSnapshot(
      IReadOnlyDictionary<string, string> diffByFile,
      IReadOnlyDictionary<string, IReadOnlyCollection<string>>? provenanceByFile = null,
      IReadOnlyCollection<TestDiffEdit>? edits = null)
    {
        ArgumentNullException.ThrowIfNull(diffByFile);
        DiffByFile = new Dictionary<string, string>(diffByFile, StringComparer.OrdinalIgnoreCase);
        ProvenanceByFile = new Dictionary<string, IReadOnlyCollection<string>>(
          provenanceByFile ?? new Dictionary<string, IReadOnlyCollection<string>>(),
          StringComparer.OrdinalIgnoreCase);
        Edits = (edits ?? Array.Empty<TestDiffEdit>()).ToArray();
    }

    public IReadOnlyDictionary<string, string> DiffByFile { get; }

    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> ProvenanceByFile { get; }

    public IReadOnlyList<TestDiffEdit> Edits { get; }
}

public sealed record TestDiffEdit
{
    public TestDiffEdit(
      string relativePath,
      string kind,
      int start,
      int length,
      IReadOnlyCollection<string>? componentIds = null)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("An edit path is required.", nameof(relativePath));
        }

        if (string.IsNullOrWhiteSpace(kind))
        {
            throw new ArgumentException("An edit kind is required.", nameof(kind));
        }

        if (start < 0 || length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "Edit spans must be non-negative.");
        }

        RelativePath = relativePath.Replace('\\', '/');
        Kind = kind;
        Start = start;
        Length = length;
        ComponentIds = (componentIds ?? Array.Empty<string>()).ToArray();
    }

    public string RelativePath { get; }

    public string Kind { get; }

    public int Start { get; }

    public int Length { get; }

    public IReadOnlyCollection<string> ComponentIds { get; }
}

public sealed record TestDiffSpanConstraint
{
    public TestDiffSpanConstraint(string relativePath, int start, int end, string? kind = null)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("A constrained edit path is required.", nameof(relativePath));
        }

        if (start < 0 || end < start)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "A constrained span must be non-negative and ordered.");
        }

        RelativePath = relativePath.Replace('\\', '/');
        Start = start;
        End = end;
        Kind = kind;
    }

    public string RelativePath { get; }

    public int Start { get; }

    public int End { get; }

    public string? Kind { get; }
}

public sealed record TestDiffContract
{
    private TestDiffContract(
      TestDiffAssertionMode mode,
      IReadOnlyDictionary<string, string> goldenDiffByFile,
      IReadOnlyCollection<string> requiredFiles,
      IReadOnlyCollection<string> forbiddenFiles,
      IReadOnlyDictionary<string, IReadOnlyCollection<string>> requiredTextByFile,
      IReadOnlyDictionary<string, IReadOnlyCollection<string>> forbiddenTextByFile,
    IReadOnlyCollection<string> requiredProvenance,
    IReadOnlyDictionary<string, IReadOnlyCollection<string>> requiredEditKindsByFile,
    IReadOnlyDictionary<string, IReadOnlyCollection<string>> forbiddenEditKindsByFile,
    IReadOnlyCollection<TestDiffSpanConstraint> requiredEditSpans)
    {
        Mode = mode;
        GoldenDiffByFile = goldenDiffByFile;
        RequiredFiles = requiredFiles;
        ForbiddenFiles = forbiddenFiles;
        RequiredTextByFile = requiredTextByFile;
        ForbiddenTextByFile = forbiddenTextByFile;
        RequiredProvenance = requiredProvenance;
        RequiredEditKindsByFile = requiredEditKindsByFile;
        ForbiddenEditKindsByFile = forbiddenEditKindsByFile;
        RequiredEditSpans = requiredEditSpans;
    }

    public TestDiffAssertionMode Mode { get; }

    public IReadOnlyDictionary<string, string> GoldenDiffByFile { get; }

    public IReadOnlyCollection<string> RequiredFiles { get; }

    public IReadOnlyCollection<string> ForbiddenFiles { get; }

    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> RequiredTextByFile { get; }

    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> ForbiddenTextByFile { get; }

    public IReadOnlyCollection<string> RequiredProvenance { get; }

    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> RequiredEditKindsByFile { get; }

    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> ForbiddenEditKindsByFile { get; }

    public IReadOnlyCollection<TestDiffSpanConstraint> RequiredEditSpans { get; }

    public static TestDiffContract Golden(IReadOnlyDictionary<string, string> expectedDiffByFile)
    {
        ArgumentNullException.ThrowIfNull(expectedDiffByFile);
        return new TestDiffContract(
          TestDiffAssertionMode.Golden,
          new Dictionary<string, string>(expectedDiffByFile, StringComparer.OrdinalIgnoreCase),
          expectedDiffByFile.Keys.ToArray(),
          Array.Empty<string>(),
          new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase),
          new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase),
      Array.Empty<string>(),
      new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase),
      new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase),
      Array.Empty<TestDiffSpanConstraint>());
    }

    public static TestDiffContract Structured(
      IReadOnlyCollection<string>? requiredFiles = null,
      IReadOnlyCollection<string>? forbiddenFiles = null,
      IReadOnlyDictionary<string, IReadOnlyCollection<string>>? requiredTextByFile = null,
      IReadOnlyDictionary<string, IReadOnlyCollection<string>>? forbiddenTextByFile = null,
    IReadOnlyCollection<string>? requiredProvenance = null,
    IReadOnlyDictionary<string, IReadOnlyCollection<string>>? requiredEditKindsByFile = null,
    IReadOnlyDictionary<string, IReadOnlyCollection<string>>? forbiddenEditKindsByFile = null,
    IReadOnlyCollection<TestDiffSpanConstraint>? requiredEditSpans = null)
    {
        return new TestDiffContract(
          TestDiffAssertionMode.Structured,
          new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
          Normalize(requiredFiles),
          Normalize(forbiddenFiles),
          Copy(requiredTextByFile),
          Copy(forbiddenTextByFile),
      Normalize(requiredProvenance),
      Copy(requiredEditKindsByFile),
      Copy(forbiddenEditKindsByFile),
      (requiredEditSpans ?? Array.Empty<TestDiffSpanConstraint>()).ToArray());
    }

    private static IReadOnlyCollection<string> Normalize(IReadOnlyCollection<string>? values)
    {
        return (values ?? Array.Empty<string>())
          .Where(value => !string.IsNullOrWhiteSpace(value))
          .Distinct(StringComparer.Ordinal)
          .OrderBy(value => value, StringComparer.Ordinal)
          .ToArray();
    }

    private static IReadOnlyDictionary<string, IReadOnlyCollection<string>> Copy(
      IReadOnlyDictionary<string, IReadOnlyCollection<string>>? values)
    {
        return (values ?? new Dictionary<string, IReadOnlyCollection<string>>())
          .ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyCollection<string>)Normalize(pair.Value),
            StringComparer.OrdinalIgnoreCase);
    }
}
