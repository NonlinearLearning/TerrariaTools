using NLCPG.Builder;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class CpgPartitionDiagnosticTests
{
  [Fact]
  public void DiagnosticPartitionEvents_ExposeStableIdentityAndSchedulerFacts()
  {
    var sink = new RecordingPartitionPerformanceEventSink();
    var options = NLCPGBuilderOptions.CreateDefault() with
    {
      MaxDegreeOfParallelism = 16,
      PerformanceDiagnostics = NLCPGPerformanceDiagnosticsMode.Diagnostic,
      PartitionPerformanceEventSink = sink,
    };

    _ = new NLCPGBuilder(options).BuildFromSource(
      "public sealed class Sample { public int Run(int value) { var copy = value + 1; return copy; } }",
      "partition-performance.cs");

    Assert.NotEmpty(sink.Events);
    Assert.Equal(
      sink.Events.Select(item => (item.StageId, item.PartitionId)).Distinct().Count(),
      sink.Events.Count);
    Assert.All(sink.Events, item =>
    {
      Assert.InRange(item.WallElapsedMilliseconds, 0, long.MaxValue);
      Assert.InRange(item.AccumulatedElapsedMilliseconds, 0, long.MaxValue);
      Assert.Null(item.QueueWaitMilliseconds);
    });
  }

  private sealed class RecordingPartitionPerformanceEventSink : IPartitionPerformanceEventSink
  {
    public List<PartitionPerformanceEvent> Events { get; } = [];

    public void Record(PartitionPerformanceEvent performanceEvent)
    {
      Events.Add(performanceEvent);
    }
  }
}
