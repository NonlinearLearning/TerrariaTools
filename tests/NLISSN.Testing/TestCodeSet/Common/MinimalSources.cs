namespace RoslynPrototype.Tests.TestCodeSet.Common;

public static class MinimalSources
{
    public const string EmptyMainSource = """
      namespace Demo;

      public static class Sample
      {
        public static void Main()
        {
        }
      }
      """;

    public const string EmptyMainWithDeadMethodSource = """
      namespace Demo;

      public static class Sample
      {
        public static void Main()
        {
        }

        public static void DeadA()
        {
        }
      }
      """;
}
