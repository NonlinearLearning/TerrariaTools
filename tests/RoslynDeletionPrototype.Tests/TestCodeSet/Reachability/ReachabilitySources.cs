namespace RoslynPrototype.Tests.TestCodeSet.Reachability;

public static class ReachabilitySources
{
    public const string UnreachableMethodsSource = """
      namespace Demo;

      public static class Sample
      {
        public static void Main()
        {
          Live();
        }

        public static void Live()
        {
        }

        public static void DeadA()
        {
          DeadB();
        }

        public static void DeadB()
        {
        }
      }
      """;

    public const string NoEntryPointSource = """
      namespace Demo;

      public static class Sample
      {
        public static void MainEntry()
        {
          Dead();
        }

        public static void Dead()
        {
        }
      }
      """;

    public const string ReachabilityIgnoresConfiguredMethodNamesSource = """
      namespace Demo;

      public static class Sample
      {
        public static void Main()
        {
          Live();
        }

        public static void Live()
        {
          Helper();
        }

        public static void Helper()
        {
        }

        public static void Dead()
        {
        }
      }
      """;

    public const string MixedRulePipelineSource = """
      namespace Demo;

      public static class Sample
      {
        public static void Main()
        {
          Live(new Box());
        }

        public static int Live(Box s)
        {
          var value = s.Seed + 1;
          return 42;
        }

        public static void Dead()
        {
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;
}
