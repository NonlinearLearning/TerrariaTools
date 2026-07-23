namespace RoslynPrototype.Tests.TestCodeSet.Rewrite;

public static class RewriteSources
{
    public const string SimpleValueReturnSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Run(int value)
        {
          return value + 1;
        }
      }
      """;

    public const string ReplaceAndDeleteSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(int value)
        {
          var temp = value + 1;
          if (value > 0)
          {
            return temp;
          }

          return temp;
        }
      }
      """;

    public const string NoDecisionSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(int value)
        {
          return value + 1;
        }
      }
      """;
}
