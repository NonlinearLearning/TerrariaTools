namespace RoslynPrototype.Tests.TestCodeSet.Propagation;

public static class PropagationSources
{
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

    public const string ObjectCreationWithInitializerSource = """
      namespace Demo;

      public sealed class Sample
      {
        public Holder Create(Box s)
        {
          var holder = new Holder
          {
            Value = s.Seed
          };
          return holder;
        }
      }

      public sealed class Holder
      {
        public int Value { get; set; }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;

    public const string ConditionalExpressionSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, bool ready)
        {
          return ready ? s.Seed : 0;
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;

    public const string TransparentWrappersSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s)
        {
          return checked((int)(s.Seed));
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;

    public const string InterpolatedStringSource = """
      namespace Demo;

      public sealed class Sample
      {
        public string Format(Box s)
        {
          return $"value {s.Seed}";
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;

    public const string YieldThrowAndArrowSource = """
      namespace Demo;

      using System;
      using System.Collections.Generic;

      public sealed class Sample
      {
        public IEnumerable<int> Values(Box s)
        {
          yield return s.Seed;
        }

        public int ThrowIt(Box s)
        {
          throw new InvalidOperationException(s.Text);
        }

        public int Arrow(Box s) => s.Seed;
      }

      public sealed class Box
      {
        public int Seed { get; set; }

        public string Text { get; set; } = "";
      }
      """;

    public const string ResourceAndLoopHeadersSource = """
      namespace Demo;

      public sealed class Sample
      {
        public void Run(Box s, int[] values)
        {
          lock (s.Sync)
          {
          }

          using (s.Open())
          {
          }

          fixed (int* value = &s.Seed)
          {
          }

          foreach (var value in s.Values)
          {
          }
        }
      }

      public sealed class Box
      {
        public object Sync { get; } = new object();

        public int Seed;

        public int[] Values { get; } = new int[0];

        public System.IDisposable Open() => null!;
      }
      """;

    public const string SwitchExpressionSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, int value)
        {
          return value switch
          {
            0 => s.Seed,
            _ => value
          };
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;

    public const string ArgumentShellSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s)
        {
          return Combine(s.Seed, 1);
        }

        private static int Combine(int value, int other)
        {
          return value + other;
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;

    public const string ChainedMemberAccessSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s)
        {
          return s.Inner.Next.Value();
        }
      }

      public sealed class Box
      {
        public InnerBox Inner { get; } = new InnerBox();
      }

      public sealed class InnerBox
      {
        public NextBox Next { get; } = new NextBox();
      }

      public sealed class NextBox
      {
        public int Value()
        {
          return 1;
        }
      }
      """;

    public const string SymbolReferenceSource = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s)
        {
          var value = s.Seed;
          return value + 1;
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }
      }
      """;
}
