namespace RoslynPrototype.Tests.TestCodeSet.SObject;

public static class SObjectLogicalSources
{
    public const string LogicalAndConditionSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, bool ready, int offset)
        {
          if (ready && s.IsReady)
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

    public const string LogicalAndConflictSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, bool ready)
        {
          if (ready && s.IsReady)
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

    public const string LogicalOrConditionSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, bool ready, int offset)
        {
          if (ready || s.IsReady)
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
}
