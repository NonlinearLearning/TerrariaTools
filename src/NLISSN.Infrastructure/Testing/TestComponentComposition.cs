namespace NLISSN.Infrastructure.Testing;

public sealed record TestComponentComposition
{
    internal TestComponentComposition(
      IReadOnlyList<TestComponent> components,
      IReadOnlyList<TestComponentSource> sources,
      IReadOnlyDictionary<string, IReadOnlyList<string>> provenanceByFile,
      IReadOnlyList<TestComponentSyntaxDiagnostic> syntaxDiagnostics,
      IReadOnlyList<TestComponentCompositionDiagnostic> compositionDiagnostics)
    {
        Components = components;
        Sources = sources;
        ProvenanceByFile = provenanceByFile;
        SyntaxDiagnostics = syntaxDiagnostics;
        CompositionDiagnostics = compositionDiagnostics;
    }

    public IReadOnlyList<TestComponent> Components { get; }

    public IReadOnlyList<TestComponentSource> Sources { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> ProvenanceByFile { get; }

    public IReadOnlyList<TestComponentSyntaxDiagnostic> SyntaxDiagnostics { get; }

    public IReadOnlyList<TestComponentCompositionDiagnostic> CompositionDiagnostics { get; }

    public bool IsSyntaxValid => SyntaxDiagnostics.Count == 0;

    public bool IsValid => IsSyntaxValid && CompositionDiagnostics.Count == 0;
}

public sealed record TestComponentSyntaxDiagnostic(
  string ComponentId,
  string RelativePath,
  string Message,
  int Start,
  int Length);

public sealed record TestComponentCompositionDiagnostic(
  string Code,
  string ComponentId,
  string Message);
