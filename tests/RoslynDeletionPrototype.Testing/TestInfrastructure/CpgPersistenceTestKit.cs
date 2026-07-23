using MinimalRoslynCpg.Persistence;

namespace RoslynPrototype.Testing.TestInfrastructure;

public static class CpgPersistenceTestKit
{
  public static IDisposable ObserveShardWrites(Action<CpgShardWriteCheckpoint> observer)
  {
    ArgumentNullException.ThrowIfNull(observer);
    var sessionType = typeof(CpgShardStoreLock).Assembly.GetType(
      "MinimalRoslynCpg.Builder.CpgShardBuildSession")
      ?? throw new InvalidOperationException("CPG shard build session type was not found.");
    var property = sessionType.GetProperty(
      "CheckpointObserver",
      System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
      ?? throw new InvalidOperationException("CPG shard build checkpoint observer was not found.");
    Action<object> adapter = checkpoint =>
    {
      var type = checkpoint.GetType();
      observer(new CpgShardWriteCheckpoint(
        (string)(type.GetProperty("BuildId")?.GetValue(checkpoint) ?? string.Empty),
        (string)(type.GetProperty("StagingRoot")?.GetValue(checkpoint) ?? string.Empty),
        (string)(type.GetProperty("FragmentKind")?.GetValue(checkpoint) ?? string.Empty),
        (int)(type.GetProperty("FragmentSpanStart")?.GetValue(checkpoint) ?? 0),
        (int)(type.GetProperty("ActiveFileWriteCount")?.GetValue(checkpoint) ?? 0)));
    };
    property.SetValue(null, adapter);
    return new CallbackReset(property);
  }

  public static Task<CpgShardStoreLock> AcquireStoreLockAsync(
    string storeRoot,
    TimeSpan timeout,
    CancellationToken cancellationToken)
  {
    return CpgShardStoreLock.AcquireAsync(storeRoot, timeout, cancellationToken);
  }
}

public sealed record CpgShardWriteCheckpoint(
  string BuildId,
  string StagingRoot,
  string FragmentKind,
  int FragmentSpanStart,
  int ActiveFileWriteCount);

internal sealed class CallbackReset(System.Reflection.PropertyInfo property) : IDisposable
{
  public void Dispose() => property.SetValue(null, null);
}
