namespace RoslynPrototype.Testing.TestInfrastructure;

public static class CpgExecutionSnapshotComparer
{
  public static void AssertEquivalent(
    CpgExecutionSnapshot expected,
    CpgExecutionSnapshot actual)
  {
    ArgumentNullException.ThrowIfNull(expected);
    ArgumentNullException.ThrowIfNull(actual);

    AssertEqual("graphSnapshotVersion", expected.GraphSnapshotVersion, actual.GraphSnapshotVersion);
    AssertEqual("nodes", expected.Nodes, actual.Nodes);
    AssertEqual("graphEdges", expected.GraphEdges, actual.GraphEdges);
    AssertEqual("directMarks", expected.DirectMarks, actual.DirectMarks);
    AssertEqual("propagatedMarks", expected.PropagatedMarks, actual.PropagatedMarks);
    AssertEqual("decisions", expected.Decisions, actual.Decisions);
    AssertEqual("diagnostics", expected.Diagnostics, actual.Diagnostics);
    AssertEqual("rewrittenSource", expected.RewrittenSource, actual.RewrittenSource);
    AssertEqual("diffText", expected.DiffText, actual.DiffText);
  }

  private static void AssertEqual(string contractName, string expected, string actual)
  {
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
    {
      throw new InvalidOperationException(
        $"{contractName} differs: expected=[{expected}]; actual=[{actual}]");
    }
  }

  private static void AssertEqual(
    string contractName,
    IReadOnlyList<string> expected,
    IReadOnlyList<string> actual)
  {
    var expectedValues = expected.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    var actualValues = actual.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    if (expectedValues.SequenceEqual(actualValues, StringComparer.Ordinal))
    {
      return;
    }

    throw new InvalidOperationException(
      $"{contractName} differs: expected=[{string.Join(", ", expectedValues)}]; " +
      $"actual=[{string.Join(", ", actualValues)}]");
  }
}
