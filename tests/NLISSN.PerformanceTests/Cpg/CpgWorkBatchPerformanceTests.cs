using System.Collections.Concurrent;
using System.Diagnostics;
using NLCPG.Builder;
using NLCPG.Contracts;
using Xunit;
using Xunit.Abstractions;

namespace RoslynPrototype.Tests.Cpg;

public sealed class CpgWorkBatchPerformanceTests
{
    private static readonly IReadOnlySet<string> KnownStageIds = new HashSet<string>(StringComparer.Ordinal)
    {
        CpgWorkBatchPerformanceStageId.Syntax,
        CpgWorkBatchPerformanceStageId.Operation,
        CpgWorkBatchPerformanceStageId.CallGraph,
        CpgWorkBatchPerformanceStageId.MemberAccess,
        CpgWorkBatchPerformanceStageId.ControlFlow,
        CpgWorkBatchPerformanceStageId.DataFlow,
        CpgWorkBatchPerformanceStageId.Dominance,
        CpgWorkBatchPerformanceStageId.ControlDependence,
    };

    private readonly ITestOutputHelper _output;

    public CpgWorkBatchPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void BuildFromSource_EmitsBoundedWorkBatchTelemetryAcrossDopSettings()
    {
        foreach (var sample in CreateSamples())
        {
            foreach (var degreeOfParallelism in new[] { 1, 2, 16 })
            {
                var sink = new RecordingWorkBatchPerformanceEventSink();
                var builder = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
                {
                    MaxDegreeOfParallelism = degreeOfParallelism,
                    RequestedCapabilities = new[] { NLCPGCapability.All },
                    LargeFileLineThreshold = 1,
                    LargeFileMethodThreshold = 1,
                    LargeMethodLineSpanThreshold = 1,
                    SyntaxLargeFileLineThreshold = 1,
                WorkBatchMaxMethodsPerBatch = 2,
                WorkBatchMaxEstimatedBytesPerBatch = 16 * 1024,
                PerformanceRunId = $"workbatch-{sample.Name}-{degreeOfParallelism}",
                WorkBatchPerformanceEventSink = sink,
                });

                var stopwatch = Stopwatch.StartNew();
                var graph = builder.BuildFromSource(sample.Source, $"workbatch-performance-{sample.Name}-{degreeOfParallelism}.cs");
                stopwatch.Stop();
                var metrics = builder.LastBuildMetrics;
                var events = metrics.WorkBatchPerformanceEvents ?? Array.Empty<CpgWorkBatchPerformanceEvent>();

                Assert.True(graph.HasQueryIndex);
                Assert.True(metrics.DataFlowBatchCount > 0);
                Assert.Equal(degreeOfParallelism, metrics.DataFlowWorkerCount);
                Assert.NotEmpty(events);
                Assert.Equal(events.Count, sink.Events.Count);
                Assert.Contains(events, item => item.StageId == CpgWorkBatchPerformanceStageId.DataFlow);
                Assert.Contains(events, item => item.FragmentBytes > 0);

                Assert.All(events, performanceEvent =>
                {
                    Assert.Contains(performanceEvent.StageId, KnownStageIds);
                    Assert.True(performanceEvent.BatchId >= 0);
                    Assert.True(performanceEvent.StableOrder >= 0);
                    Assert.True(performanceEvent.InputCount > 0);
                    Assert.True(performanceEvent.EstimatedCost > 0);
                    Assert.True(performanceEvent.EstimatedBytes > 0);
                    Assert.True(performanceEvent.OutputNodeCount >= 0);
                    Assert.True(performanceEvent.OutputEdgeCount >= 0);
                    Assert.True(performanceEvent.FragmentBytes >= 0);
                    Assert.True(performanceEvent.QueueWaitMilliseconds >= 0);
                    Assert.True(performanceEvent.ProcessingElapsedMilliseconds >= 0);
                    Assert.True(performanceEvent.ReducerWaitElapsedMilliseconds >= 0);
                    Assert.Equal($"workbatch-{sample.Name}-{degreeOfParallelism}", performanceEvent.RunId);
                    Assert.InRange(performanceEvent.WorkerIndex, 0, degreeOfParallelism - 1);
                    Assert.InRange(performanceEvent.PeakActiveWorkerCount, 1, degreeOfParallelism);
                    Assert.True(performanceEvent.QueueHighWaterMark > 0);
                    Assert.True(performanceEvent.CompletedNotReducedHighWaterMark > 0);
                });

                Assert.Equal(
                    events.Select(item => (item.StageId, item.StableOrder, item.BatchId)),
                    sink.Events.Select(item => (item.StageId, item.StableOrder, item.BatchId)));

                _output.WriteLine(
                    $"sample={sample.Name}; dop={degreeOfParallelism}; elapsedMs={stopwatch.ElapsedMilliseconds}; " +
                    $"events={events.Count}; dataFlowBatches={metrics.DataFlowBatchCount}; " +
                    $"peakActive={events.Max(item => item.PeakActiveWorkerCount)}; " +
                    $"queueHighWater={events.Max(item => item.QueueHighWaterMark)}; " +
                    $"completedNotReducedHighWater={events.Max(item => item.CompletedNotReducedHighWaterMark)}; " +
                    $"fragmentBytes={events.Sum(item => item.FragmentBytes)}");
            }
        }
    }

    private static IReadOnlyList<PerformanceSample> CreateSamples()
    {
        return new[]
        {
            new PerformanceSample("small", """
              public sealed class SmallSample
              {
                public int Run(int value)
                {
                  var result = value + 1;
                  return result;
                }

                public int Call(int value)
                {
                  return Run(value);
                }
              }
              """),
            new PerformanceSample("mixed", """
              public sealed class MixedSample
              {
                public int First(int value)
                {
                  var result = value + 1;
                  if (result > 10)
                  {
                    result -= 2;
                  }
                  return result;
                }

                public int Second(int value) => First(value) + 1;
                public int Third(int value) => Second(value) + First(value);
                public int Fourth(int value) => Third(value) + 1;
                public int Fifth(int value) => Fourth(value) + 1;
                public int Sixth(int value) => Fifth(value) + 1;
                public int Seventh(int value) => Sixth(value) + 1;
                public int Eighth(int value) => Seventh(value) + 1;
              }
              """),
            new PerformanceSample("large", CreateLargeSource()),
        };
    }

    private static string CreateLargeSource()
    {
        var methods = Enumerable.Range(0, 96)
            .Select(index =>
                $"    public int Method{index}(int value)\n" +
                "    {\n" +
                $"      var result = value + {index};\n" +
                "      return result;\n" +
                "    }");
        return "public sealed class LargeSample\n{\n" +
            string.Join("\n", methods) +
            "\n}";
    }

    private sealed record PerformanceSample(string Name, string Source);

    private sealed class RecordingWorkBatchPerformanceEventSink : ICpgWorkBatchPerformanceEventSink
    {
        private readonly ConcurrentQueue<CpgWorkBatchPerformanceEvent> _events = new();

        public IReadOnlyList<CpgWorkBatchPerformanceEvent> Events => _events.ToArray();

        public void Record(CpgWorkBatchPerformanceEvent performanceEvent)
        {
            _events.Enqueue(performanceEvent);
        }
    }
}
