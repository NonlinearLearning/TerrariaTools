namespace RoslynPrototype.Tests.TestCodeSet.Logging;

public static class LoggingSources
{
  public const string SingleFileAnalysisSource =
    """
    namespace Demo;

    public sealed class Sample
    {
      public int Run(Box s)
      {
        return s.Seed + 1;
      }
    }

    public sealed class Box
    {
      public int Seed { get; set; }
    }
    """;

  public const string DirectoryAnalysisSource =
    """
    namespace Demo;

    public sealed class DirectorySample
    {
      public int Run(Box s)
      {
        return s.Seed + 1;
      }
    }

    public sealed class Box
    {
      public int Seed { get; set; }
    }
    """;
}
