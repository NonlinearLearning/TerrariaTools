using RoslynPrototype.Testing.TestInfrastructure;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

public sealed class CpgExecutionSnapshotComparerTests
{
  [Fact]
  public void AssertEquivalent_WhenGraphEdgesDiffer_ReportsSortedGraphEdgeDifference()
  {
    var expected = CreateSnapshot(graphEdges: ["2>3:DataFlow", "1>2:Contains"]);
    var actual = CreateSnapshot(graphEdges: ["1>2:Contains", "2>4:DataFlow"]);

    var exception = Assert.Throws<InvalidOperationException>(() =>
      CpgExecutionSnapshotComparer.AssertEquivalent(expected, actual));

    Assert.Equal(
      "graphEdges differs: expected=[1>2:Contains, 2>3:DataFlow]; actual=[1>2:Contains, 2>4:DataFlow]",
      exception.Message);
  }

  [Theory]
  [InlineData("directMarks")]
  [InlineData("propagatedMarks")]
  [InlineData("decisions")]
  [InlineData("diagnostics")]
  [InlineData("rewrittenSource")]
  [InlineData("diffText")]
  public void AssertEquivalent_WhenObservableContractDiffers_ReportsContractName(string contractName)
  {
    var expected = CreateSnapshot();
    var actual = contractName switch
    {
      "directMarks" => expected with { DirectMarks = ["mark-b"] },
      "propagatedMarks" => expected with { PropagatedMarks = ["propagated-b"] },
      "decisions" => expected with { Decisions = ["decision-b"] },
      "diagnostics" => expected with { Diagnostics = ["diagnostic-b"] },
      "rewrittenSource" => expected with { RewrittenSource = "rewritten-b" },
      "diffText" => expected with { DiffText = "diff-b" },
      _ => throw new ArgumentOutOfRangeException(nameof(contractName)),
    };

    var exception = Assert.Throws<InvalidOperationException>(() =>
      CpgExecutionSnapshotComparer.AssertEquivalent(expected, actual));

    Assert.StartsWith($"{contractName} differs:", exception.Message, StringComparison.Ordinal);
  }

  private static CpgExecutionSnapshot CreateSnapshot(IReadOnlyList<string>? graphEdges = null)
  {
    return new CpgExecutionSnapshot(
      "snapshot-v1",
      ["node-a"],
      graphEdges ?? ["1>2:Contains"],
      ["mark-a"],
      ["propagated-a"],
      ["decision-a"],
      ["diagnostic-a"],
      "rewritten-a",
      "diff-a");
  }
}
