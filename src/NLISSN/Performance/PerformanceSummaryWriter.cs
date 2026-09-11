using System.Text.Json;
using NLISSN.Core.Performance;

namespace NLISSN.Performance;

public interface IPerformanceSummaryWriter
{
  void WriteAtomic(string path, RunPerformanceReport report);
}

public sealed class PerformanceSummaryWriter : IPerformanceSummaryWriter
{
  public void WriteAtomic(string path, RunPerformanceReport report)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);
    ArgumentNullException.ThrowIfNull(report);

    var fullPath = Path.GetFullPath(path);
    var parentDirectory = Path.GetDirectoryName(fullPath)
      ?? throw new ArgumentException("Performance summary path must have a parent directory.", nameof(path));
    var runArtifactRoot = PerformanceDiagnosticAttachment.ResolveRunArtifactRoot(fullPath);
    foreach (var attachment in report.Attachments)
    {
      PerformanceDiagnosticAttachment.ValidateForSummary(
        attachment,
        report.RunId,
        runArtifactRoot);
    }

    Directory.CreateDirectory(parentDirectory);
    var temporaryPath = fullPath + ".tmp";
    try
    {
      var document = PerformanceSummaryDocument.FromReport(report);
      using (var stream = new FileStream(
        temporaryPath,
        FileMode.Create,
        FileAccess.Write,
        FileShare.None,
        bufferSize: 4096,
        options: FileOptions.WriteThrough))
      {
        JsonSerializer.Serialize(stream, document, PerformanceSummaryDocument.JsonOptions);
        stream.Flush(flushToDisk: true);
      }

      if (File.Exists(fullPath))
      {
        File.Replace(temporaryPath, fullPath, null, ignoreMetadataErrors: true);
      }
      else
      {
        File.Move(temporaryPath, fullPath);
      }
    }
    finally
    {
      if (File.Exists(temporaryPath))
      {
        File.Delete(temporaryPath);
      }
    }
  }
}
