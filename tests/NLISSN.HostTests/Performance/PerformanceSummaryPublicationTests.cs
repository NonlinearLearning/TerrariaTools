using NLISSN.Core.Performance;
using NLISSN.Performance;
using Xunit;

namespace RoslynPrototype.Tests.Performance;

public sealed class PerformanceSummaryPublicationTests
{
  [Fact]
  public void Publish_IsIdempotentForOneTerminalReport()
  {
    var writer = new RecordingWriter();
    var publisher = new PerformanceSummaryPublisher(writer);
    var report = CreateReport();

    var first = publisher.Publish("summary.json", report);
    var second = publisher.Publish("summary.json", report);

    Assert.Equal(PerformancePublicationStatus.Published, first.Status);
    Assert.Equal(PerformancePublicationStatus.AlreadyPublished, second.Status);
    Assert.Equal(1, writer.CallCount);
  }

  [Fact]
  public void Publish_ContainsWriterFailureWithoutThrowingToCaller()
  {
    var publisher = new PerformanceSummaryPublisher(new ThrowingWriter());

    var result = publisher.Publish("summary.json", CreateReport());

    Assert.Equal(PerformancePublicationStatus.Failed, result.Status);
    Assert.NotNull(result.ErrorKind);
  }

  private static RunPerformanceReport CreateReport()
  {
    return RunPerformanceReport.Create(
      "run-1",
      "file",
      new ApplicationPerformanceFacts(
        "file.cs",
        CpgPerformanceFacts.Unavailable("file.cs"),
        null,
        null),
      PerformanceStatus.Completed);
  }

  private sealed class RecordingWriter : IPerformanceSummaryWriter
  {
    public int CallCount { get; private set; }

    public void WriteAtomic(string path, RunPerformanceReport report)
    {
      CallCount++;
    }
  }

  private sealed class ThrowingWriter : IPerformanceSummaryWriter
  {
    public void WriteAtomic(string path, RunPerformanceReport report)
    {
      throw new IOException("test writer failure");
    }
  }
}
