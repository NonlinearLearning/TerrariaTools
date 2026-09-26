using System.Text.Json;
using NLCPG.Builder;
using RoslynPrototype.Tests.TestCodeSet.Cpg;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class DataFlowDiagnosticsTests {
  [Theory]
  [InlineData(0)]
  [InlineData(1)]
  [InlineData(2)]
  public void BuildFromSource_AllModes_PreserveFullGraphAndPublicationOrder(int mode) {
    var baselineBuilder = CreateBuilder(DataFlowDiagnosticMode.Off);
    var source = DataFlowMeasurementSources.Collision();
    var baseline = baselineBuilder.BuildFromSource(source, "equivalence.cs");
    var builder = CreateBuilder((DataFlowDiagnosticMode)mode);
    var actual = builder.BuildFromSource(source, "equivalence.cs");

    Assert.Equal(DataFlowGraphSnapshot.Capture(baseline), DataFlowGraphSnapshot.Capture(actual));
    Assert.Equal(JsonSerializer.Serialize(baselineBuilder.LastDataFlowDiagnostics[0].PublicationOrder),
        JsonSerializer.Serialize(builder.LastDataFlowDiagnostics[0].PublicationOrder));
    if (mode != (int)DataFlowDiagnosticMode.Detailed) {
      Assert.All(builder.LastDataFlowDiagnostics[0].Counters.Values, value => Assert.Equal(0, value));
      Assert.All(builder.LastDataFlowDiagnostics[0].DetailTicks.Values, value => Assert.Equal(0, value));
    }
  }

  [Fact]
  public void BuildFromSource_DefaultMode_DoesNotCreateDiagnostics() {
    var builder = new NLCPGBuilder();
    builder.BuildFromSource(DataFlowMeasurementSources.Sparse(), "default.cs");
    Assert.Empty(builder.LastDataFlowDiagnostics);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(1)]
  [InlineData(2)]
  public void BuildFromSource_CandidateOverflow_PreservesSkipAndRawBudget(int mode) {
    var options = NLCPGBuilderOptions.CreateDefault() with {
      MaxDegreeOfParallelism = 1,
      DataFlowOptions = new NLCPGDataFlowOptions(int.MaxValue, int.MaxValue, 1),
    };
    var baseline = new NLCPGBuilder(options).BuildFromSource(
        DataFlowMeasurementSources.Collision(), "budget.cs");
    var builder = new NLCPGBuilder(options) {
      DataFlowDiagnostics = new((DataFlowDiagnosticMode)mode, "budget", "fixture"),
    };
    var actual = builder.BuildFromSource(DataFlowMeasurementSources.Collision(), "budget.cs");
    var result = Assert.Single(builder.LastDataFlowDiagnostics);

    Assert.Equal("CandidateEdgeLimitExceeded", result.ExitReason);
    Assert.Equal(2, result.Metrics.RawCandidateCount);
    Assert.Empty(result.PublicationOrder);
    Assert.Equal(DataFlowGraphSnapshot.Capture(baseline), DataFlowGraphSnapshot.Capture(actual));
  }

  [Fact]
  public void BuildFromSource_MultipleMethods_KeepsIdentityAndCountersLocal() {
    var builder = CreateBuilder(DataFlowDiagnosticMode.Detailed);
    var source = DataFlowMeasurementSources.Sparse().Replace("static int Run", "static int First") +
        DataFlowMeasurementSources.Sparse().Replace("class Sample", "class Other");
    builder.BuildFromSource(source, "two-methods.cs");
    Assert.Equal(2, builder.LastDataFlowDiagnostics.Count);
    var first = builder.LastDataFlowDiagnostics[0];
    var second = builder.LastDataFlowDiagnostics[1];
    Assert.NotEqual(first.MethodSignature, second.MethodSignature);
    Assert.NotEqual(first.SpanStart, second.SpanStart);
    Assert.NotEqual(first.StableOrder, second.StableOrder);
    Assert.Equal(first.Counters, second.Counters);
  }

  [Fact]
  public void BuildFromSource_DetailedMode_ReportsSameMethodPhasesAndActualScans() {
    var builder = CreateBuilder(DataFlowDiagnosticMode.Detailed);
    builder.BuildFromSource(DataFlowMeasurementSources.Collision(), "collision.cs");

    var result = Assert.Single(builder.LastDataFlowDiagnostics);
    Assert.Equal("Complete", result.ExitReason);
    Assert.Contains("Sample.Run(Box, bool, int)", result.MethodSignature);
    Assert.Equal(9, result.PhaseTicks.Count);
    Assert.True(result.PhaseTicks.Values.Sum() <= result.MethodTotalTicks);
    Assert.True(result.DetailTicks.Values.Sum() <= result.PhaseTicks["CandidateLoop"]);
    Assert.True(result.Counters["UsedOperationVisits"] > 0);
    Assert.True(result.Counters["PlanOperationVisits"] > 0);
    Assert.True(result.Counters["LocationHits"] > 0);
    Assert.True(result.Counters["RootHits"] > 0);
    Assert.True(result.Counters["SeenRejects"] > 0);
    Assert.True(result.Counters["FactsMatchCalls"] > 0);
    // Each of the four indexes implements a sufficient FactsMatch condition.
    Assert.Equal(result.Counters["FactsMatchCalls"], result.Counters["FactsMatchTrue"]);
    Assert.Equal(result.Metrics.RawCandidateCount, result.Counters["RawCandidates"]);
  }

  [Fact]
  public void BuildFromSource_ReusedBuilder_DoesNotAccumulateMethodCounters() {
    var builder = CreateBuilder(DataFlowDiagnosticMode.Detailed);
    builder.BuildFromSource(DataFlowMeasurementSources.Sparse(), "sparse.cs");
    var first = Assert.Single(builder.LastDataFlowDiagnostics);
    builder.BuildFromSource(DataFlowMeasurementSources.Sparse(), "sparse.cs");
    var second = Assert.Single(builder.LastDataFlowDiagnostics);
    Assert.Equal(first.Counters, second.Counters);
  }

  [Fact]
  public void BuildFromSource_JoinLoop_RecordsRevisitsAndIncomingRebuilds() {
    var builder = CreateBuilder(DataFlowDiagnosticMode.Detailed);
    builder.BuildFromSource(DataFlowMeasurementSources.JoinLoop(), "joinloop.cs");
    var result = Assert.Single(builder.LastDataFlowDiagnostics);
    Assert.True(result.Counters["Dequeues"] > result.Metrics.FlowNodeCount);
    Assert.True(result.Counters["CandidatePredecessorVisits"] > 0);
    Assert.NotEmpty(result.PublicationOrder);
  }

  private static NLCPGBuilder CreateBuilder(DataFlowDiagnosticMode mode) {
    return new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with {
      MaxDegreeOfParallelism = 1, LargeFileLineThreshold = 1,
      LargeFileMethodThreshold = 1, LargeMethodLineSpanThreshold = 1,
      SyntaxLargeFileLineThreshold = 1,
    }) { DataFlowDiagnostics = new(mode, "contract", "fixture") };
  }
}
