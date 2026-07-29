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

    public const string WhileBodySource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, int offset)
        {
          while (offset > 0)
          {
            offset += s.Seed;
          }

          return offset;
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
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

    public const string DoBodySource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, int offset)
        {
          do
          {
            offset += s.Seed;
          } while (offset > 0);

          return offset;
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
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

    public const string ForInitializerDeclarationSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, bool ready)
        {
          for (var value = s.Seed; ready; value++)
          {
          }

          return 0;
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;

    public const string ForIncrementorSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, bool ready)
        {
          for (var value = 0; ready; value += s.Seed)
          {
          }

          return 0;
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
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

    public const string SwitchConditionSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s)
        {
          switch (s.Seed)
          {
            case 1:
              return 1;
            default:
              return 0;
          }
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;

    public const string SwitchCaseSingleStatementSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, int offset)
        {
          switch (offset)
          {
            case 1:
              offset += s.Seed;
              break;
            default:
              return offset;
          }

          return offset;
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;

    public const string SwitchCaseBlockStatementSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, int offset)
        {
          switch (offset)
          {
            case 1:
            {
              offset += s.Seed;
              break;
            }
            default:
              return offset;
          }

          return offset;
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;

    public const string SwitchCaseMultiStatementSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, int offset)
        {
          switch (offset)
          {
            case 1:
              offset += s.Seed;
              offset += s.Seed;
              break;
            default:
              return offset;
          }

          return offset;
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;

    public const string SwitchCaseWithoutBreakSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, int offset)
        {
          switch (offset)
          {
            case 1:
              offset += s.Seed;
              return offset;
            default:
              return 0;
          }
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;

    public const string SwitchCaseWithoutBreakFullyMarkedSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, int offset)
        {
          switch (offset)
          {
            case 1:
              offset += s.Seed;
              return s.Seed;
            default:
              return 0;
          }
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;

    public const string SwitchAllNonDefaultCasesMarkedSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, int offset)
        {
          switch (offset)
          {
            case 1:
              offset += s.Seed;
              break;
            case 2:
              offset += s.Seed;
              break;
            default:
              return offset;
          }

          return offset;
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;

    public const string IfElseOnlySource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, int value)
        {
          if (s.IsReady)
          {
            return value + 1;
          }
          else
          {
            return value + 2;
          }
        }
      }

      public sealed class Box
      {
        public bool IsReady { get; set; }
      }
      """;

    public const string IfElseIfElseSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, bool ready, bool fallback)
        {
          if (s.IsReady)
          {
            return 1;
          }
          else if (fallback)
          {
            return 2;
          }
          else
          {
            return 3;
          }
        }
      }

      public sealed class Box
      {
        public bool IsReady { get; set; }
      }
      """;

    public const string ElseIfElseSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, bool ready)
        {
          if (ready)
          {
            return 1;
          }
          else if (s.IsReady)
          {
            return 2;
          }
          else
          {
            return 3;
          }
        }
      }

      public sealed class Box
      {
        public bool IsReady { get; set; }
      }
      """;

    public const string ElseIfWithoutTailSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, bool ready)
        {
          if (ready)
          {
            return 1;
          }
          else if (s.IsReady)
          {
            return 2;
          }

          return 3;
        }
      }

      public sealed class Box
      {
        public bool IsReady { get; set; }
      }
      """;
}
