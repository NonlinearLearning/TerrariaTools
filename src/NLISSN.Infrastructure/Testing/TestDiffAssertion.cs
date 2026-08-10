namespace NLISSN.Infrastructure.Testing;

public sealed record TestDiffAssertionResult(IReadOnlyList<string> Failures)
{
    public bool IsSuccess => Failures.Count == 0;
}

public sealed class TestDiffAssertion
{
    public TestDiffAssertionResult Assert(
      TestDiffSnapshot actual,
      TestDiffContract contract)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(contract);

        return contract.Mode == TestDiffAssertionMode.Golden
          ? AssertGolden(actual, contract)
          : AssertStructured(actual, contract);
    }

    private static TestDiffAssertionResult AssertGolden(
      TestDiffSnapshot actual,
      TestDiffContract contract)
    {
        var failures = new List<string>();
        var paths = actual.DiffByFile.Keys
          .Concat(contract.GoldenDiffByFile.Keys)
          .Distinct(StringComparer.OrdinalIgnoreCase)
          .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            actual.DiffByFile.TryGetValue(path, out var actualDiff);
            contract.GoldenDiffByFile.TryGetValue(path, out var expectedDiff);
            if (!string.Equals(actualDiff, expectedDiff, StringComparison.Ordinal))
            {
                failures.Add($"Golden diff mismatch for '{path}'.");
            }
        }

        return new TestDiffAssertionResult(failures);
    }

    private static TestDiffAssertionResult AssertStructured(
      TestDiffSnapshot actual,
      TestDiffContract contract)
    {
        var failures = new List<string>();
        var actualPaths = new HashSet<string>(actual.DiffByFile.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var path in contract.RequiredFiles)
        {
            if (!actualPaths.Contains(path))
            {
                failures.Add($"Required diff file '{path}' is missing.");
            }
        }

        foreach (var path in contract.ForbiddenFiles)
        {
            if (actualPaths.Contains(path))
            {
                failures.Add($"Forbidden diff file '{path}' was emitted.");
            }
        }

        foreach (var pair in contract.RequiredTextByFile)
        {
            AssertText(failures, actual, pair.Key, pair.Value, shouldExist: true);
        }

        foreach (var pair in contract.ForbiddenTextByFile)
        {
            AssertText(failures, actual, pair.Key, pair.Value, shouldExist: false);
        }

        foreach (var pair in contract.RequiredEditKindsByFile)
        {
            var kinds = actual.Edits
              .Where(edit => string.Equals(edit.RelativePath, pair.Key, StringComparison.OrdinalIgnoreCase))
              .Select(edit => edit.Kind)
              .ToHashSet(StringComparer.Ordinal);
            foreach (var kind in pair.Value)
            {
                if (!kinds.Contains(kind))
                {
                    failures.Add($"Required edit kind '{kind}' is missing from diff '{pair.Key}'.");
                }
            }
        }

    foreach (var pair in contract.ForbiddenEditKindsByFile)
        {
            if (actual.Edits.Any(edit =>
              string.Equals(edit.RelativePath, pair.Key, StringComparison.OrdinalIgnoreCase) &&
              pair.Value.Contains(edit.Kind, StringComparer.Ordinal)))
            {
                failures.Add($"Forbidden edit kind was emitted in diff '{pair.Key}'.");
      }
    }

    foreach (var constraint in contract.RequiredEditSpans)
    {
      var matched = actual.Edits.Any(edit =>
        string.Equals(edit.RelativePath, constraint.RelativePath, StringComparison.OrdinalIgnoreCase) &&
        (constraint.Kind is null || string.Equals(edit.Kind, constraint.Kind, StringComparison.Ordinal)) &&
        edit.Start >= constraint.Start &&
        edit.Start + edit.Length <= constraint.End);
      if (!matched)
      {
        failures.Add(
          $"Required edit span [{constraint.Start}, {constraint.End}) is missing from diff '{constraint.RelativePath}'.");
      }
    }

        var provenance = actual.ProvenanceByFile.Values
          .SelectMany(values => values)
          .ToHashSet(StringComparer.Ordinal);
        foreach (var componentId in contract.RequiredProvenance)
        {
            if (!provenance.Contains(componentId))
            {
                failures.Add($"Required diff provenance '{componentId}' is missing.");
            }
        }

        return new TestDiffAssertionResult(failures);
    }

    private static void AssertText(
      ICollection<string> failures,
      TestDiffSnapshot actual,
      string path,
      IEnumerable<string> snippets,
      bool shouldExist)
    {
        actual.DiffByFile.TryGetValue(path, out var diff);
        diff ??= string.Empty;
        foreach (var snippet in snippets)
        {
            var found = diff.Contains(snippet, StringComparison.Ordinal);
            if (found != shouldExist)
            {
                var action = shouldExist ? "missing from" : "forbidden in";
                failures.Add($"Snippet '{snippet}' is {action} diff '{path}'.");
            }
        }
    }
}
