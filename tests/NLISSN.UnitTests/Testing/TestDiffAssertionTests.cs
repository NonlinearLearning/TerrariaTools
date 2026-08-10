using NLISSN.Infrastructure.Testing;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class TestDiffAssertionTests
{
  [Fact]
  public void Assert_GoldenDiff_RequiresExactFileBytes()
  {
    var contract = TestDiffContract.Golden(
      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        ["Demo.cs"] = "-old\n+new\n"
      });
    var snapshot = new TestDiffSnapshot(
      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        ["Demo.cs"] = "-old\n+new\n"
      });

    var result = new TestDiffAssertion().Assert(snapshot, contract);

    Assert.True(result.IsSuccess);
  }

  [Fact]
  public void Assert_GoldenDiff_ReportsUnexpectedFile()
  {
    var contract = TestDiffContract.Golden(
      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        ["Demo.cs"] = "-old\n+new\n"
      });
    var snapshot = new TestDiffSnapshot(
      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        ["Demo.cs"] = "-old\n+new\n",
        ["Unexpected.cs"] = "-x\n+y\n"
      });

    var result = new TestDiffAssertion().Assert(snapshot, contract);

    Assert.False(result.IsSuccess);
    Assert.Contains(result.Failures, failure => failure.Contains("Unexpected.cs", StringComparison.Ordinal));
  }

  [Fact]
  public void Assert_StructuredContract_EnforcesTextAndProvenance()
  {
    var contract = TestDiffContract.Structured(
      requiredFiles: new[] { "Demo.cs" },
      requiredTextByFile: new Dictionary<string, IReadOnlyCollection<string>>(
        StringComparer.OrdinalIgnoreCase)
      {
        ["Demo.cs"] = new[] { "+new" }
      },
      forbiddenTextByFile: new Dictionary<string, IReadOnlyCollection<string>>(
        StringComparer.OrdinalIgnoreCase)
      {
        ["Demo.cs"] = new[] { "PlayerInput" }
      },
      requiredProvenance: new[] { "expression-component" },
      requiredEditKindsByFile: new Dictionary<string, IReadOnlyCollection<string>>(
        StringComparer.OrdinalIgnoreCase)
      {
        ["Demo.cs"] = new[] { "Replace" }
      },
      requiredEditSpans: new[] { new TestDiffSpanConstraint("Demo.cs", 0, 4, "Replace") });
    var snapshot = new TestDiffSnapshot(
      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        ["Demo.cs"] = "-old\n+new\n"
      },
      new Dictionary<string, IReadOnlyCollection<string>>(
        StringComparer.OrdinalIgnoreCase)
      {
        ["Demo.cs"] = new[] { "expression-component" }
      },
      new[] { new TestDiffEdit("Demo.cs", "Replace", 0, 3, new[] { "expression-component" }) });

    var result = new TestDiffAssertion().Assert(snapshot, contract);

    Assert.True(result.IsSuccess);
  }

  [Fact]
  public void Assert_StructuredContract_ReportsForbiddenTextAndMissingProvenance()
  {
    var contract = TestDiffContract.Structured(
      requiredTextByFile: new Dictionary<string, IReadOnlyCollection<string>>(
        StringComparer.OrdinalIgnoreCase)
      {
        ["Demo.cs"] = new[] { "+new" }
      },
      forbiddenTextByFile: new Dictionary<string, IReadOnlyCollection<string>>(
        StringComparer.OrdinalIgnoreCase)
      {
        ["Demo.cs"] = new[] { "PlayerInput" }
      },
      requiredProvenance: new[] { "expression-component" });
    var snapshot = new TestDiffSnapshot(
      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        ["Demo.cs"] = "-PlayerInput\n+new\n"
      });

    var result = new TestDiffAssertion().Assert(snapshot, contract);

    Assert.False(result.IsSuccess);
    Assert.Contains(result.Failures, failure => failure.Contains("forbidden", StringComparison.Ordinal));
    Assert.Contains(result.Failures, failure => failure.Contains("provenance", StringComparison.Ordinal));
  }
}
