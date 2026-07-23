namespace RoslynPrototype.Tests.TestCodeSet.SObject;

public static class SObjectExpressionSources
{
    public const string DefinitionAssignmentSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, int offset)
        {
          var value = s.Seed + offset;
          return offset;
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;

    public const string AssignmentStatementSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, int offset)
        {
          offset += s.Seed;
          return offset;
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;

    public const string ComplexDefinitionAssignmentSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, int offset, int[] values)
        {
          var value = (s.Seed + offset) * values[offset];
          return value;
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;

    public const string ChainedAssignmentStatementSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, int left, int right)
        {
          left = right = s.Seed;
          return left + right;
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;

    public const string DeconstructionAssignmentStatementSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, int offset)
        {
          var left = 0;
          var right = 0;
          (left, right) = (s.Seed, offset);
          return left + right;
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;

    public const string ObjectInitializerDefinitionAssignmentSource = """
      namespace Demo;

      public sealed class Sample
      {
        public Holder Create(Box s, int offset)
        {
          var holder = new Holder
          {
            Value = s.Seed,
            Next = offset
          };
          return holder;
        }
      }

      public sealed class Holder
      {
        public int Value { get; set; }

        public int Next { get; set; }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;

    public const string ComplexCompoundAssignmentStatementSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, int offset)
        {
          offset += s.Seed + offset * 2;
          return offset;
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;

    public const string AssignmentLeftOperandSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, int[] values, int offset)
        {
          values[s.Seed] = offset;
          return values[offset];
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;

    public const string DefinitionLeftOperandSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(int offset)
        {
          var s = offset + 1;
          return offset;
        }
      }
      """;

    public const string CallArgumentStatementSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, int offset)
        {
          Fun(s.Seed, 3);
          return offset;
        }

        private static void Fun(int value, int other)
        {
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;

    public const string PropertyAccessDefinitionSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box holder)
        {
          var value = holder.Seed;
          return 0;
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;

    public const string IndexAccessDefinitionSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, int[] values, int offset)
        {
          var value = values[s.Seed];
          return offset;
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;

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

    public const string ConditionalAccessPropertySource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box? s)
        {
          var value = s?.Seed ?? 0;
          return value;
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;

    public const string ConditionalAccessInvokeSource = """
      namespace Demo;

      using System;

      public sealed class Sample
      {
        public void Raise(Action? handler)
        {
          handler?.Invoke();
        }
      }
      """;

    public const string ConditionalAccessChainSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box? s)
        {
          var value = s?.Inner?.Seed ?? 0;
          return value;
        }
      }

      public sealed class Box
      {
        public InnerBox? Inner { get; set; }
      }

      public sealed class InnerBox
      {
        public int Seed { get; set; }
      }
      """;
}
