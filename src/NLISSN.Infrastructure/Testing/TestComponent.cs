namespace NLISSN.Infrastructure.Testing;

public enum TestComponentKind
{
    Declaration,
    Expression,
    Statement,
    SemanticRelation,
    Context,
    SafetyBoundary
}

public sealed record TestComponent
{
    public TestComponent(
      string id,
      TestComponentKind kind,
      IReadOnlyList<TestComponentSource> sources,
      IReadOnlyCollection<string>? tags = null,
      IReadOnlyCollection<string>? prerequisites = null,
      TestDiffContract? diffContract = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Component id is required.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
        {
            throw new ArgumentException("At least one source fragment is required.", nameof(sources));
        }

        Id = id;
        Kind = kind;
        Sources = sources.ToArray();
        Tags = Normalize(tags);
        Prerequisites = Normalize(prerequisites);
        DiffContract = diffContract;
    }

    public string Id { get; }

    public TestComponentKind Kind { get; }

    public IReadOnlyList<TestComponentSource> Sources { get; }

    public IReadOnlyCollection<string> Tags { get; }

    public IReadOnlyCollection<string> Prerequisites { get; }

    public TestDiffContract? DiffContract { get; }

    private static IReadOnlyCollection<string> Normalize(IReadOnlyCollection<string>? values)
    {
        return (values ?? Array.Empty<string>())
          .Where(value => !string.IsNullOrWhiteSpace(value))
          .Distinct(StringComparer.Ordinal)
          .OrderBy(value => value, StringComparer.Ordinal)
          .ToArray();
    }
}
