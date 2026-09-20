using System.Collections.Concurrent;
using System.Text.Json;
using NLCPG.Builder;
using NLISSN.Core.Performance;

namespace NLISSN.Application.Performance;

/// Stores high-cardinality diagnostics for one run until the host materializes them.
public sealed class PerformanceDiagnosticsCollector : IPerformanceEventSink, IPartitionPerformanceEventSink
{
  private readonly ConcurrentQueue<PerformanceEvent> _events = new();
  private readonly ConcurrentQueue<PartitionPerformanceEvent> _partitions = new();

  public void Record(PerformanceEvent performanceEvent)
  {
    ArgumentNullException.ThrowIfNull(performanceEvent);
    _events.Enqueue(performanceEvent);
  }

  public void Record(PartitionPerformanceEvent performanceEvent)
  {
    ArgumentNullException.ThrowIfNull(performanceEvent);
    _partitions.Enqueue(performanceEvent);
  }

  public bool TryWriteJson(string path, out string? errorKind)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);
    var temporaryPath = path + ".tmp";
    try
    {
      var parentDirectory = Path.GetDirectoryName(Path.GetFullPath(path))
        ?? throw new ArgumentException("Diagnostic artifact path must have a parent directory.", nameof(path));
      Directory.CreateDirectory(parentDirectory);
      var document = new
      {
        schemaVersion = 1,
        events = _events
          .OrderBy(item => item.StageId, StringComparer.Ordinal)
          .ThenBy(item => item.ItemId, StringComparer.Ordinal)
          .ThenBy(item => item.WallElapsedMs)
          .Select(item => new
          {
            item.RunId,
            item.StageId,
            item.ItemId,
            item.WallElapsedMs,
            item.AccumulatedElapsedMs,
            status = item.Status.ToString().ToLowerInvariant(),
            item.Counters,
            item.ErrorKind
          })
          .ToArray(),
        partitions = _partitions
          .OrderBy(item => item.StageId, StringComparer.Ordinal)
          .ThenBy(item => item.PartitionIndex)
          .ThenBy(item => item.PartitionId, StringComparer.Ordinal)
          .ToArray()
      };
      using (var stream = new FileStream(
        temporaryPath,
        FileMode.Create,
        FileAccess.Write,
        FileShare.None,
        bufferSize: 4096,
        options: FileOptions.WriteThrough))
      {
        JsonSerializer.Serialize(stream, document, new JsonSerializerOptions
        {
          PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
          DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
          WriteIndented = true
        });
        stream.Flush(flushToDisk: true);
      }

      if (File.Exists(path))
      {
        File.Replace(temporaryPath, path, null, ignoreMetadataErrors: true);
      }
      else
      {
        File.Move(temporaryPath, path);
      }

      errorKind = null;
      return true;
    }
    catch (Exception exception)
    {
      errorKind = exception.GetType().FullName ?? exception.GetType().Name;
      return false;
    }
    finally
    {
      if (File.Exists(temporaryPath))
      {
        try
        {
          File.Delete(temporaryPath);
        }
        catch
        {
          // A diagnostic cleanup failure must not affect the business result.
        }
      }
    }
  }
}
