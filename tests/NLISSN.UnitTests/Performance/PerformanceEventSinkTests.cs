using NLISSN.Core.Performance;
using Xunit;

namespace RoslynPrototype.Tests.Performance;

public sealed class PerformanceEventSinkTests
{
  [Fact]
  public void EventSnapshotsCountersAndSinkFailuresAreFailOpen()
  {
    var sink = new RecordingSink();
    var performanceEvent = new PerformanceEvent(
      "run-1",
      "Rule.Mark",
      "a.cs",
      3,
      null,
      PerformanceStatus.Completed,
      new Dictionary<string, long> { ["inputCount"] = 2 });

    Assert.True(sink.TryRecord(performanceEvent));
    Assert.Equal(2, sink.Events.Single().Counters["inputCount"]);
    Assert.False(new ThrowingSink().TryRecord(performanceEvent));
  }

  [Fact]
  public void NullSinkDoesNotCreateAnEventPath()
  {
    var performanceEvent = new PerformanceEvent(
      "run-1",
      "Rule.Propagate",
      null,
      null,
      null,
      PerformanceStatus.Unavailable);

    Assert.False(((IPerformanceEventSink?)null).TryRecord(performanceEvent));
  }

  private sealed class RecordingSink : IPerformanceEventSink
  {
    public List<PerformanceEvent> Events { get; } = [];

    public void Record(PerformanceEvent performanceEvent)
    {
      Events.Add(performanceEvent);
    }
  }

  private sealed class ThrowingSink : IPerformanceEventSink
  {
    public void Record(PerformanceEvent performanceEvent)
    {
      throw new InvalidOperationException("diagnostic sink failed");
    }
  }
}
