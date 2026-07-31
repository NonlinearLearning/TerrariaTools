namespace NL.Concurrency;

public static class ConcurrencyExecutionPolicy
{
  public static int ResolveMaxDegreeOfParallelism(
    bool isParallelismEnabled,
    int requestedMaxDegreeOfParallelism)
  {
    return isParallelismEnabled
      ? Math.Max(1, requestedMaxDegreeOfParallelism)
      : 1;
  }
}
