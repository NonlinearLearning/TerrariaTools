namespace RoslynPrototype.Tests.TestCodeSet.Cpg;

public static class CpgDisplaySources
{
    public const string ConditionalSeedSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Run(int seed)
        {
          if (seed > 0) return seed + 1;
          return 0;
        }
      }
      """;
}
