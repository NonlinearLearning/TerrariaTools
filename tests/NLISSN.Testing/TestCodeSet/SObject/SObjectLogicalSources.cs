namespace RoslynPrototype.Tests.TestCodeSet.SObject;

public static class SObjectLogicalSources
{
    public const string LogicalMixedPrecedenceLargeCase1Source = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(bool a, bool b, bool c, bool d, bool e, bool f, bool g, bool h, bool i, bool j, bool k, bool l)
        {
          if (((a && b) || (c && d) || !b || e || (f && g) || h || i || j || k || l))
          {
            return 1;
          }

          return 0;
        }
      }
      """;

    public const string LogicalMixedPrecedenceLargeCase2Source = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(bool a, bool b, bool c, bool d, bool e, bool f, bool g, bool h, bool i, bool j, bool k, bool l, bool m)
        {
          if ((((a || b) && (c || !b)) || d || e || (f && g) || h || i || j || k || l || m))
          {
            return 1;
          }

          return 0;
        }
      }
      """;

    public const string LogicalMixedPrecedenceLargeCase3Source = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(bool a, bool b, bool c, bool d, bool e, bool f, bool g, bool h, bool i, bool j, bool k, bool l)
        {
          if (((a && b) || c || d || (!b && e) || f || g || h || (i && j) || k || l))
          {
            return 1;
          }

          return 0;
        }
      }
      """;

    public const string LogicalMixedPrecedenceLargeCase4Source = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(bool a, bool b, bool c, bool d, bool e, bool f, bool g, bool h, bool i, bool j, bool k, bool l)
        {
          if ((((a && b) || c) || d || e || ((f || !b) && g) || h || i || j || k || l))
          {
            return 1;
          }

          return 0;
        }
      }
      """;

    public const string LogicalMixedPrecedenceLargeCase5Source = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(bool a, bool b, bool c, bool d, bool e, bool f, bool g, bool h, bool i, bool j, bool k, bool l)
        {
          if (((a && (b || c)) || d || e || !b || (f && g) || h || i || j || k || l))
          {
            return 1;
          }

          return 0;
        }
      }
      """;

    public const string LogicalMixedPrecedenceWithParenthesesSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(bool a, bool b, bool c)
        {
          if ((a && b) || c || !b)
          {
            return 1;
          }

          return 0;
        }
      }
      """;

    public const string LogicalMixedPrecedenceSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(bool a, bool b, bool c)
        {
          if (a && b || c || !b)
          {
            return 1;
          }

          return 0;
        }
      }
      """;

    public const string LogicalMultiTargetGroupFiveHitsSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(bool a, bool b, bool c, bool d, bool e, bool f, bool g, bool h)
        {
          if (a || b || c || d || e || f || g || h)
          {
            return 1;
          }

          return 0;
        }
      }
      """;

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
