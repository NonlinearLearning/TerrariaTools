namespace RoslynPrototype.Tests.TestCodeSet.Cli;

public static class CliInputSources
{
    public const string DiffWriteSource = """
          namespace Demo;

          public sealed class Sample
          {
            public int Compute(Box s, int offset)
            {
              var value = s.Seed + offset;
              return value;
            }
          }

          public sealed class Box
          {
            public int Seed { get; set; }
          }
          """;

    public const string ExplicitDiffOutSource = """
          namespace Demo;

          public sealed class Sample
          {
            public int Compute(Box s, int offset)
            {
              var value = s.Seed + offset;
              return value;
            }
          }

          public sealed class Box
          {
            public int Seed { get; set; }
          }
          """;
}
