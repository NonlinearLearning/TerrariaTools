namespace RoslynPrototype.Tests.TestCodeSet.SObject;

public static class SObjectControlFlowSources
{
    public const string IfHostConflictSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, int offset)
        {
          if (s.IsReady)
          {
            return offset;
          }

          return 0;
        }
      }

      public sealed class Box
      {
        public bool IsReady { get; set; }
      }
      """;

    public const string WhileConditionSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, int offset)
        {
          while (s.IsReady)
          {
            offset++;
          }

          return offset;
        }
      }

      public sealed class Box
      {
        public bool IsReady { get; set; }
      }
      """;

    public const string DoConditionSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, int offset)
        {
          do
          {
            offset++;
          } while (s.IsReady);

          return offset;
        }
      }

      public sealed class Box
      {
        public bool IsReady { get; set; }
      }
      """;

    public const string ForConditionSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, int offset)
        {
          for (; s.IsReady; offset++)
          {
          }

          return offset;
        }
      }

      public sealed class Box
      {
        public bool IsReady { get; set; }
      }
      """;

    public const string PropagationDedupSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s)
        {
          if (s.IsReady)
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

    public const string MultipleDomainsSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s)
        {
          if (s.IsReady)
          {
            return 1;
          }

          while (s.IsReady)
          {
            return 2;
          }

          return 0;
        }
      }

      public sealed class Box
      {
        public bool IsReady { get; set; }
      }
      """;
}
