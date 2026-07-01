namespace RoslynPrototype.Tests.TestCodeSet.SObject;

public static class SObjectExpressionSources
{
    public const string TargetNameSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, int offset)
        {
          var value = s.Seed + offset;
          if (s.IsReady)
          {
            return value;
          }

          return offset;
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }

        public bool IsReady { get; set; }
      }
      """;

    public const string ReturnExpressionSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s)
        {
          return s.Seed;
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;

    public const string MarkingDedupSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s)
        {
          return s.Seed;
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;
}
