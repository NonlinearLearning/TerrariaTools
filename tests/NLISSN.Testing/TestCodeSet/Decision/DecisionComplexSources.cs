namespace RoslynPrototype.Tests.TestCodeSet.Decision;

public static class DecisionComplexSources
{
    public const string NestedLogicalAndReductionSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, bool ready, bool enabled)
        {
          if (ready && s.IsReady && enabled)
          {
            return 1;
          }

          return 0;
        }
      }

      public sealed class Box
      {
        public bool IsReady { get; set; }
      }
      """;

    public const string NestedLogicalOrReductionSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, bool ready, bool fallback)
        {
          if (ready || s.IsReady || fallback)
          {
            return 1;
          }

          return 0;
        }
      }

      public sealed class Box
      {
        public bool IsReady { get; set; }
      }
      """;

    public const string ParentHostWinsOverNestedBodySource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s)
        {
          if (s.IsReady)
          {
            s.Touch();
          }

          return 0;
        }
      }

      public sealed class Box
      {
        public bool IsReady { get; set; }

        public void Touch()
        {
        }
      }
      """;
}
