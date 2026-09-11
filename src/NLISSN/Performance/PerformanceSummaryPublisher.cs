using NLISSN.Core.Performance;

namespace NLISSN.Performance;

public enum PerformancePublicationStatus
{
  Published,
  AlreadyPublished,
  Failed
}

public sealed record PerformancePublicationResult(
  PerformancePublicationStatus Status,
  string? ErrorKind = null);

public sealed class PerformanceSummaryPublisher
{
  private readonly IPerformanceSummaryWriter _writer;
  private int _publicationState;

  public PerformanceSummaryPublisher(IPerformanceSummaryWriter? writer = null)
  {
    _writer = writer ?? new PerformanceSummaryWriter();
  }

  public PerformancePublicationResult Publish(string path, RunPerformanceReport report)
  {
    if (Interlocked.CompareExchange(ref _publicationState, 1, 0) != 0)
    {
      return new PerformancePublicationResult(PerformancePublicationStatus.AlreadyPublished);
    }

    try
    {
      _writer.WriteAtomic(path, report);
      Volatile.Write(ref _publicationState, 2);
      return new PerformancePublicationResult(PerformancePublicationStatus.Published);
    }
    catch (Exception exception)
    {
      Volatile.Write(ref _publicationState, 2);
      return new PerformancePublicationResult(
        PerformancePublicationStatus.Failed,
        exception.GetType().FullName ?? exception.GetType().Name);
    }
  }
}
