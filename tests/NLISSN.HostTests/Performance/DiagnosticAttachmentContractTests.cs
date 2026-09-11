using System.Text.Json;
using NLISSN.Core.Performance;
using NLISSN.Performance;
using Xunit;

namespace RoslynPrototype.Tests.Performance;

public sealed class DiagnosticAttachmentContractTests : IDisposable
{
  private readonly string _runRoot = Path.Combine(
    Path.GetTempPath(),
    $"nlissn-diagnostic-attachment-{Guid.NewGuid():N}");

  public DiagnosticAttachmentContractTests()
  {
    Directory.CreateDirectory(_runRoot);
  }

  [Fact]
  public void AvailableAttachmentUsesRunRelativePathWithoutEmbeddingPayload()
  {
    var tracePath = Path.Combine(_runRoot, "Performance", "trace.nettrace");
    Directory.CreateDirectory(Path.GetDirectoryName(tracePath)!);
    File.WriteAllText(tracePath, "trace payload must stay outside summary");

    var attachment = PerformanceDiagnosticAttachment.Create(
      "trace",
      "run-1",
      PerformanceStageId.Run,
      PerformanceMode.Diagnostic,
      _runRoot,
      tracePath);
    var report = CreateReport(attachment);
    var json = JsonSerializer.Serialize(
      PerformanceSummaryDocument.FromReport(report),
      PerformanceSummaryDocument.JsonOptions);

    Assert.False(Path.IsPathRooted(attachment.RelativePath!));
    Assert.Equal("Performance/trace.nettrace", attachment.RelativePath);
    Assert.True(attachment.IsAvailable);
    Assert.True(attachment.IsComplete);
    Assert.DoesNotContain("trace payload must stay outside summary", json, StringComparison.Ordinal);
    Assert.Contains("Performance/trace.nettrace", json, StringComparison.Ordinal);
  }

  [Fact]
  public void MissingAttachmentIsExplicitlyUnavailableAndIncomplete()
  {
    var missingPath = Path.Combine(_runRoot, "Performance", "missing.nettrace");

    var attachment = PerformanceDiagnosticAttachment.Create(
      "trace",
      "run-1",
      PerformanceStageId.Run,
      PerformanceMode.Profile,
      _runRoot,
      missingPath);
    var document = PerformanceSummaryDocument.FromReport(CreateReport(attachment));
    var jsonAttachment = Assert.Single(document.Attachments);

    Assert.False(attachment.IsAvailable);
    Assert.False(attachment.IsComplete);
    Assert.Equal("attachment-missing", attachment.ErrorKind);
    Assert.Equal("unavailable", jsonAttachment.Status);
    Assert.False(jsonAttachment.IsComplete);
  }

  [Fact]
  public void AttachmentOutsideRunRootIsRejected()
  {
    var outsidePath = Path.Combine(
      Path.GetDirectoryName(_runRoot)!,
      $"outside-{Guid.NewGuid():N}.nettrace");

    Assert.Throws<ArgumentException>(() => PerformanceDiagnosticAttachment.Create(
      "trace",
      "run-1",
      PerformanceStageId.Run,
      PerformanceMode.Diagnostic,
      _runRoot,
      outsidePath));
  }

  [Fact]
  public void SummaryOrdersAttachmentsByKindThenPath()
  {
    var firstPath = Path.Combine(_runRoot, "Performance", "a.nettrace");
    var secondPath = Path.Combine(_runRoot, "Performance", "b.nettrace");
    Directory.CreateDirectory(Path.GetDirectoryName(firstPath)!);
    File.WriteAllText(firstPath, "a");
    File.WriteAllText(secondPath, "b");

    var first = PerformanceDiagnosticAttachment.Create(
      "trace",
      "run-1",
      PerformanceStageId.Run,
      PerformanceMode.Diagnostic,
      _runRoot,
      firstPath);
    var second = PerformanceDiagnosticAttachment.Create(
      "counters",
      "run-1",
      PerformanceStageId.Run,
      PerformanceMode.Diagnostic,
      _runRoot,
      secondPath);

    var document = PerformanceSummaryDocument.FromReport(CreateReport(second, first));

    Assert.Equal(
      new[] { "counters", "trace" },
      document.Attachments.Select(attachment => attachment.Kind));
  }

  private static RunPerformanceReport CreateReport(
    params PerformanceAttachmentReference[] attachments)
  {
    return new RunPerformanceReport(
      "run-1",
      "file",
      "file.cs",
      Array.Empty<ApplicationPerformanceFacts>(),
      null,
      new PerformanceTerminalSummary(1, 1, PerformanceStatus.Completed, true),
      PerformanceStatus.Completed,
      PerformanceMode.Diagnostic,
      attachments: attachments);
  }

  public void Dispose()
  {
    if (Directory.Exists(_runRoot))
    {
      Directory.Delete(_runRoot, recursive: true);
    }
  }
}
