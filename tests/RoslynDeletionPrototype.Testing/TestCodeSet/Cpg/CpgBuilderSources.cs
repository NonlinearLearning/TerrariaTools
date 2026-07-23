namespace RoslynPrototype.Tests.TestCodeSet.Cpg;

public static class CpgBuilderSources
{
  public const string ControlDependenceOverlay = """
      public sealed class Sample
      {
        public int Adjust(int value)
        {
          if (value > 0)
          {
            value += 1;
          }
          else
          {
            value -= 1;
          }

          return value;
        }
      }
      """;

  public const string SmallPartitionedSource = """
      namespace Demo;

      public sealed class SmallSample
      {
        public int Run(int value)
        {
          return value + 1;
        }
      }
      """;

  public const string ComplexMethodLocalFlow = """
      namespace Demo;

      public sealed class FlowShapeSample
      {
        public int Run(int seed)
        {
          var current = seed;
          if (seed > 0)
          {
            current = seed + 1;
          }

          while (current < 3)
          {
            current = current + 1;
          }

          return Echo(current);
        }

        private static int Echo(int value)
        {
          return value;
        }
      }
      """;

  public const string LocalDataFlow = """
      namespace Demo;

      public sealed class GraphSample
      {
        public int Increment(int seed)
        {
          var value = seed + 1;
          return value;
        }
      }
      """;

  public const string RepeatedReferences = """
      namespace Demo;

      public sealed class DuplicateFlowSample
      {
        public int Run(int seed)
        {
          var value = seed + 1;
          return value + value + value;
        }
      }
      """;

  public const string DeclarationShapes = """
      namespace Demo;

      public delegate int Transformer<T>(T value);

      public enum State
      {
        Ready,
      }

      public sealed class DeclarationShapes<T>
      {
        private int _field;
        public event System.Action? Changed;
        public event System.Action? CustomChanged
        {
          add { }
          remove { }
        }
        public int Property { get; set; }
        public int this[int index] { get => index; set => _field = value; }

        public DeclarationShapes(int parameter)
        {
          _field = parameter;
        }

        public static DeclarationShapes<T> operator +(
          DeclarationShapes<T> left,
          DeclarationShapes<T> right) => left;

        public static implicit operator int(DeclarationShapes<T> value) => value._field;

        public int Run<TMethod>(int parameter)
        {
          var local = parameter;
          if (local is int pattern)
          {
          label:
            int LocalFunction(int localParameter) => localParameter + pattern;
            return LocalFunction(local);
          }

          return 0;
        }
      }
      """;

  public const string DeclaredSymbolQueryTelemetry = """
      namespace Demo;

      public sealed class QueryTelemetrySample
      {
        public int Run(int value)
        {
          var total = value + 1;
          total = total * 2;
          return total > 3 ? total : total + 4;
        }
      }
      """;

  public const string SeparatedTypeInfoTelemetry = """
      namespace Demo;

      public sealed class TypeInfoSample
      {
        private int _field;
        public int Property { get; set; }

        public int Run(int parameter)
        {
          var local = parameter + _field + Property;
          return new System.Collections.Generic.List<int> { local }.Count;
        }
      }
      """;

  public const string TypeInfoSourceAndDataFlowPreparationTelemetry = """
      namespace Demo;

      public sealed class TelemetrySample
      {
        private int _field;
        public int Property { get; set; }

        public int Run(int parameter)
        {
          var local = parameter + _field + Property;
          return local;
        }
      }
      """;

  public const string DataFlowFactCollection = """
      namespace Demo;

      public sealed class DataFlowDedupSample
      {
        public int Run(int input)
        {
          var first = input + 1;
          var second = Transform(first * (input + 2));
          return second + first;
        }

        private static int Transform(int value) => value;
      }
      """;

  public const string DataFlowDefinitionBudget = """
      namespace Demo;

      public sealed class DefinitionBudgetSample
      {
        public int Run(int input)
        {
          var first = input + 1;
          var second = first + 1;
          return second;
        }
      }
      """;

  public const string DataFlowNodeBudget = """
      namespace Demo;

      public sealed class FlowNodeBudgetSample
      {
        public int Run(int input)
        {
          return input + 1;
        }
      }
      """;

  public const string DataFlowCandidateBudget = "namespace Demo; public sealed class Sample { public int Run(int value) { return value + 1; } }";

  public const string DataFlowBudgetSkip = """
      namespace Demo;

      public sealed class BudgetSample
      {
        public int WithinBudget(int input)
        {
          var result = input + 1;
          return result;
        }

        public int OverBudget(int input)
        {
          var first = input + 1;
          var second = first + 1;
          return second;
        }
      }
      """;

  public const string OperationBackedTypeInfoFallback = """
      namespace Demo;

      public sealed class TypeInfoFallbackSample
      {
        private int _field = 1 + 2;

        public void Run(int parameter)
        {
          System.Action action = Log;
          action();
        }

        private void Log(int value)
        {
        }
      }
      """;

  public const string SymbolTypeReuse = """
      namespace Demo;

      public sealed class SymbolTypeSample
      {
        private int _field;
        public int Property { get; set; }
        public event System.Action? Changed;

        public int Run(int parameter)
        {
          var local = parameter + _field + Property;
          Changed?.Invoke();
          return local;
        }
      }
      """;

  public const string SymbolTypeReuseFallback = """
      namespace Demo;

      public sealed class FallbackSample
      {
        private static int Transform(int value) => value + 1;

        public int Run(dynamic dynamicValue)
        {
          System.Func<int, int> methodGroup = Transform;
          var conditional = dynamicValue?.ToString();
          return methodGroup(conditional is null ? 0 : 1);
        }
      }
      """;

  public const string OperationBackedSyntaxTypes = """
      namespace Demo;

      public sealed class OperationTypeSample
      {
        private int _value;

        public int Run(int parameter)
        {
          var local = parameter + _value;
          return local > 0 ? local : local + 1;
        }
      }
      """;

  public const string ControlFlowAndDataFlowHeavy = """
      namespace Demo;

      public sealed class ComplexSample
      {
        public int Run(int input)
        {
          var total = input;
          while (total < 5)
          {
            total += 1;
            if (total == 3)
            {
              continue;
            }
          }

          for (var index = 0; index < 2; index += 1)
          {
            total += index;
          }

          switch (total)
          {
            case 0:
              total += 10;
              break;
            case 1:
            case 2:
              total += 20;
              break;
            default:
              total += 30;
              break;
          }

          try
          {
            total += Helper(total);
          }
          catch (System.InvalidOperationException)
          {
            total -= 1;
          }
          finally
          {
            total += 100;
          }

          return total;
        }

        private static int Helper(int value)
        {
          return value > 10 ? value : value + 1;
        }
      }
      """;

  public const string SyntaxSemanticShapes = """
      namespace Demo;
      public sealed class Box<T> { public T Value { get; set; } = default!; }
      public sealed class Sample
      {
        public int Run(Box<int> box)
        {
          int Convert() => box.Value + 1;
          return Convert();
        }
      }
      """;

  public const string DataFlowCallReturnAndProperty = """
      namespace Demo;
      public sealed class Counter { public int Value { get; set; } }
      public sealed class Sample
      {
        public int Run(Counter counter, int seed)
        {
          counter.Value = seed;
          var next = Increment(counter.Value);
          return next;
        }
        private static int Increment(int value) => value + 1;
      }
      """;

  public const string DispatchKinds = """
      namespace Demo;

      public sealed class DispatchSample
      {
        private static int Helper(int value) => value + 1;

        public int Run(int value)
        {
          return Helper(value);
        }
      }
      """;

}
