using System.Diagnostics;
using System.Globalization;
using System.Text;
using NLCPG.Analysis;
using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Model;
using NLCPG.Persistence;
using Xunit;
using Xunit.Abstractions;

namespace RoslynPrototype.Tests;

/// <summary>
/// 小型、固定输入的 graph storage measurement。该测试记录数据，不把机器相关的耗时阈值写成契约。
/// </summary>
public sealed class NLCPGNodeStoragePerformanceTests
{
    private const int WarmupCount = 2;
    private const int MeasurementCount = 5;
    private const int CarrierCount = 50_000;
    private const string FilePath = "nlcpg-storage-benchmark.cs";

    private readonly ITestOutputHelper _output;

    public NLCPGNodeStoragePerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void FixedSource_ReportsGraphBuildQueryAndExportRestoreMeasurements()
    {
        var source = CreateBenchmarkSource();
        var warmups = Enumerable.Range(0, WarmupCount)
          .Select(sample => MeasureGraph(source, sample, isWarmup: true))
          .ToArray();
        var measurements = Enumerable.Range(0, MeasurementCount)
          .Select(sample => MeasureGraph(source, sample, isWarmup: false))
          .ToArray();

        var baseline = measurements[0];
        Assert.All(measurements, measurement =>
        {
            Assert.Equal(baseline.NodeCount, measurement.NodeCount);
            Assert.Equal(baseline.EdgeCount, measurement.EdgeCount);
            Assert.Equal(baseline.OperationCount, measurement.OperationCount);
            Assert.Equal(baseline.PathQueryCount, measurement.PathQueryCount);
            Assert.Equal(baseline.GraphSnapshotVersion, measurement.GraphSnapshotVersion);
            Assert.Equal(measurement.GraphSnapshotVersion, measurement.RestoredSnapshotVersion);
            Assert.Equal(measurement.NodeCount, measurement.RestoredNodeCount);
            Assert.Equal(measurement.EdgeCount, measurement.RestoredEdgeCount);
            Assert.True(measurement.TextProjectionChecksum != 0);
            Assert.True(measurement.LocalNodeCount > 0);
            Assert.True(measurement.SerializedBytes > 0);
        });

        _output.WriteLine(
          $"runtime={Environment.Version}; sdk=10.0.400; inputChars={source.Length}; " +
          $"dop=1; warmups={warmups.Length}; measurements={measurements.Length}; " +
          $"nodes={baseline.NodeCount}; edges={baseline.EdgeCount}; " +
          $"build-us={FormatValues(measurements.Select(item => item.BuildMicroseconds))}; " +
          $"query-us={FormatValues(measurements.Select(item => item.QueryMicroseconds))}; " +
          $"export-restore-us={FormatValues(measurements.Select(item => item.ExportRestoreMicroseconds))}; " +
          $"allocated-bytes={FormatValues(measurements.Select(item => item.AllocatedBytes))}; " +
          $"heap-bytes={FormatValues(measurements.Select(item => item.HeapBytes))}; " +
          $"peak-working-set-delta={FormatValues(measurements.Select(item => item.PeakWorkingSetDeltaBytes))}; " +
          $"gc-gen0={FormatValues(measurements.Select(item => item.Gen0Collections))}; " +
          $"gc-gen1={FormatValues(measurements.Select(item => item.Gen1Collections))}; " +
          $"gc-gen2={FormatValues(measurements.Select(item => item.Gen2Collections))}; " +
          $"serialized-bytes={baseline.SerializedBytes}; local-nodes={baseline.LocalNodeCount}; " +
          $"path-nodes={baseline.PathQueryCount}; text-checksum={baseline.TextProjectionChecksum}");
    }

    [Fact]
    public void CarrierVariants_ReportAllocationAndScanMeasurements()
    {
        var warmups = Enumerable.Range(0, WarmupCount)
          .Select(sample => MeasureCarrier(CarrierVariant.Class, sample, isWarmup: true))
          .Concat(Enumerable.Range(0, WarmupCount)
            .Select(sample => MeasureCarrier(CarrierVariant.StructString, sample, isWarmup: true)))
          .Concat(Enumerable.Range(0, WarmupCount)
            .Select(sample => MeasureCarrier(CarrierVariant.StructId, sample, isWarmup: true)))
          .ToArray();
        _ = warmups;

        foreach (var variant in Enum.GetValues<CarrierVariant>())
        {
            var measurements = Enumerable.Range(0, MeasurementCount)
              .Select(sample => MeasureCarrier(variant, sample, isWarmup: false))
              .ToArray();
            Assert.All(measurements, measurement => Assert.Equal(CarrierCount, measurement.Count));

            _output.WriteLine(
              $"carrier={variant}; count={CarrierCount}; samples={measurements.Length}; " +
              $"time-us={FormatValues(measurements.Select(item => item.ElapsedMicroseconds))}; " +
              $"allocated-bytes={FormatValues(measurements.Select(item => item.AllocatedBytes))}; " +
              $"gc-gen0={FormatValues(measurements.Select(item => item.Gen0Collections))}; " +
              $"gc-gen1={FormatValues(measurements.Select(item => item.Gen1Collections))}; " +
              $"gc-gen2={FormatValues(measurements.Select(item => item.Gen2Collections))}; " +
              $"checksum={measurements[0].Checksum}");
        }
    }

    private static GraphMeasurement MeasureGraph(string source, int sample, bool isWarmup)
    {
        ForceFullCollection();
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var workingSetBefore = process.WorkingSet64;
        var peakWorkingSet = workingSetBefore;
        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var options = NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = 1,
            RequestedCapabilities = new[] { NLCPGCapability.All },
        };

        var buildTimer = Stopwatch.StartNew();
        var graph = new NLCPGBuilder(options).BuildFromSource(source, FilePath);
        buildTimer.Stop();
        process.Refresh();
        peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);

        var operationNodes = graph.GetNodes(NLCPGNodeKind.Operation);
        var anchor = graph.Nodes
          .Where(node => node.NodeId.HasValue)
          .OrderBy(node => node.NodeId)
          .First();
        var queryTimer = Stopwatch.StartNew();
        var pathNodes = graph.GetNodesInFileSpan(FilePath, 0, source.Length);
        var localView = graph.ExtractLocalView(anchor.NodeId!.Value, hops: 2);
        var textProjectionChecksum = 17;
        foreach (var node in graph.Nodes.OrderBy(node => node.NodeId))
        {
            textProjectionChecksum = unchecked(
              (textProjectionChecksum * 31) + StringComparer.Ordinal.GetHashCode(graph.GetDisplayText(node)));
            textProjectionChecksum = unchecked(
              (textProjectionChecksum * 31) + StringComparer.Ordinal.GetHashCode(graph.ResolveFullName(node) ?? string.Empty));
        }

        _ = graph.GetNodes(NLCPGNodeKind.Operation).Count;
        queryTimer.Stop();
        process.Refresh();
        peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);

        var lookup = new CpgShardLookup(
          new CpgFileKey("storage-benchmark", FilePath, "fixed-source-hash"),
          new CpgFragmentKey("whole-file", 0, source.Length, "fixed-fragment-hash"),
          SchemaVersion: 1,
          ProfileHash: "storage-benchmark-profile");
        var exportRestoreTimer = Stopwatch.StartNew();
        var shard = CpgFrozenShardExporter.Export(graph, lookup);
        var storeRoot = Path.Combine(
          Path.GetTempPath(),
          $"nlcpg-storage-benchmark-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storeRoot);
        try
        {
            var store = new CpgShardStore(storeRoot);
            var location = store.WriteAsync(shard, CancellationToken.None).GetAwaiter().GetResult();
            var restoredShard = store.ReadAsync(location, CancellationToken.None).GetAwaiter().GetResult();
            var restoredGraph = CpgFrozenShardGraphReader.ReadGraph(restoredShard);
            exportRestoreTimer.Stop();

            process.Refresh();
            peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
            var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            var heapBytes = GC.GetGCMemoryInfo().HeapSizeBytes;
            return new GraphMeasurement(
              sample,
              isWarmup,
              graph.Nodes.Count,
              graph.Edges.Count,
              operationNodes.Count,
              pathNodes.Count,
              localView.Nodes.Count,
              graph.GraphSnapshotVersion,
              restoredGraph.GraphSnapshotVersion,
              restoredGraph.Nodes.Count,
              restoredGraph.Edges.Count,
              textProjectionChecksum,
              ToMicroseconds(buildTimer.Elapsed),
              ToMicroseconds(queryTimer.Elapsed),
              ToMicroseconds(exportRestoreTimer.Elapsed),
              allocatedBytes,
              heapBytes,
              peakWorkingSet - workingSetBefore,
              GC.CollectionCount(0) - gen0Before,
              GC.CollectionCount(1) - gen1Before,
              GC.CollectionCount(2) - gen2Before,
              location.ByteLength);
        }
        finally
        {
            if (Directory.Exists(storeRoot))
            {
                Directory.Delete(storeRoot, recursive: true);
            }
        }
    }

    private static CarrierMeasurement MeasureCarrier(CarrierVariant variant, int sample, bool isWarmup)
    {
        ForceFullCollection();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);
        var timer = Stopwatch.StartNew();
        var checksum = 17;
        switch (variant)
        {
            case CarrierVariant.Class:
            {
                var values = new ClassCarrier[CarrierCount];
                for (var index = 0; index < values.Length; index += 1)
                {
                    values[index] = new ClassCarrier("Add", "Demo.Sample.Add", "Demo.Sample.Add:int(int,int)", "Demo.Sample", "Fixture.cs", index);
                    checksum = unchecked((checksum * 31) + values[index].SpanStart);
                }

                GC.KeepAlive(values);
                break;
            }
            case CarrierVariant.StructString:
            {
                var values = new StructStringCarrier[CarrierCount];
                for (var index = 0; index < values.Length; index += 1)
                {
                    values[index] = new StructStringCarrier("Add", "Demo.Sample.Add", "Demo.Sample.Add:int(int,int)", "Demo.Sample", "Fixture.cs", index);
                    checksum = unchecked((checksum * 31) + values[index].SpanStart);
                }

                GC.KeepAlive(values);
                break;
            }
            case CarrierVariant.StructId:
            {
                var values = new StructIdCarrier[CarrierCount];
                var strings = new[]
                {
                    "Add",
                    "Demo.Sample.Add",
                    "Demo.Sample.Add:int(int,int)",
                    "Demo.Sample",
                    "Fixture.cs",
                };
                for (var index = 0; index < values.Length; index += 1)
                {
                    values[index] = new StructIdCarrier(1, 2, 3, 4, 5, index);
                    checksum = unchecked((checksum * 31) + strings[values[index].NameId - 1].Length);
                    checksum = unchecked((checksum * 31) + values[index].SpanStart);
                }

                GC.KeepAlive(values);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(variant), variant, null);
        }

        timer.Stop();
        return new CarrierMeasurement(
          variant,
          sample,
          isWarmup,
          CarrierCount,
          checksum,
          ToMicroseconds(timer.Elapsed),
          GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore,
          GC.CollectionCount(0) - gen0Before,
          GC.CollectionCount(1) - gen1Before,
          GC.CollectionCount(2) - gen2Before);
    }

    private static string CreateBenchmarkSource()
    {
        var source = new StringBuilder("public sealed class StorageSample {\n");
        for (var index = 0; index < 16; index += 1)
        {
            source.Append("public int Method")
              .Append(index.ToString(CultureInfo.InvariantCulture))
              .Append("(int value) { if (value > ")
              .Append(index.ToString(CultureInfo.InvariantCulture))
              .Append(") { value += 1; } else { value -= 1; } return value; }\n");
        }

        return source.Append("}\n").ToString();
    }

    private static void ForceFullCollection()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private static long ToMicroseconds(TimeSpan elapsed)
    {
        return (long)(elapsed.TotalMilliseconds * 1_000d);
    }

    private static string FormatValues(IEnumerable<long> values)
    {
        return string.Join(",", values.Select(value => value.ToString(CultureInfo.InvariantCulture)));
    }

    private static string FormatValues(IEnumerable<int> values)
    {
        return string.Join(",", values.Select(value => value.ToString(CultureInfo.InvariantCulture)));
    }

    private enum CarrierVariant
    {
        Class,
        StructString,
        StructId,
    }

    private sealed class ClassCarrier
    {
        public ClassCarrier(string name, string fullName, string signature, string typeFullName, string filePath, int spanStart)
        {
            Name = name;
            FullName = fullName;
            Signature = signature;
            TypeFullName = typeFullName;
            FilePath = filePath;
            SpanStart = spanStart;
        }

        public string Name { get; }
        public string FullName { get; }
        public string Signature { get; }
        public string TypeFullName { get; }
        public string FilePath { get; }
        public int SpanStart { get; }
    }

    private readonly struct StructStringCarrier
    {
        public StructStringCarrier(string name, string fullName, string signature, string typeFullName, string filePath, int spanStart)
        {
            Name = name;
            FullName = fullName;
            Signature = signature;
            TypeFullName = typeFullName;
            FilePath = filePath;
            SpanStart = spanStart;
        }

        public string Name { get; }
        public string FullName { get; }
        public string Signature { get; }
        public string TypeFullName { get; }
        public string FilePath { get; }
        public int SpanStart { get; }
    }

    private readonly struct StructIdCarrier
    {
        public StructIdCarrier(uint nameId, uint fullNameId, uint signatureId, uint typeFullNameId, uint filePathId, int spanStart)
        {
            NameId = nameId;
            FullNameId = fullNameId;
            SignatureId = signatureId;
            TypeFullNameId = typeFullNameId;
            FilePathId = filePathId;
            SpanStart = spanStart;
        }

        public uint NameId { get; }
        public uint FullNameId { get; }
        public uint SignatureId { get; }
        public uint TypeFullNameId { get; }
        public uint FilePathId { get; }
        public int SpanStart { get; }
    }

    private sealed record GraphMeasurement(
      int Sample,
      bool IsWarmup,
      int NodeCount,
      int EdgeCount,
      int OperationCount,
      int PathQueryCount,
      int LocalNodeCount,
      string GraphSnapshotVersion,
      string RestoredSnapshotVersion,
      int RestoredNodeCount,
      int RestoredEdgeCount,
      int TextProjectionChecksum,
      long BuildMicroseconds,
      long QueryMicroseconds,
      long ExportRestoreMicroseconds,
      long AllocatedBytes,
      long HeapBytes,
      long PeakWorkingSetDeltaBytes,
      int Gen0Collections,
      int Gen1Collections,
      int Gen2Collections,
      long SerializedBytes);

    private sealed record CarrierMeasurement(
      CarrierVariant Variant,
      int Sample,
      bool IsWarmup,
      int Count,
      int Checksum,
      long ElapsedMicroseconds,
      long AllocatedBytes,
      int Gen0Collections,
      int Gen1Collections,
      int Gen2Collections);
}
