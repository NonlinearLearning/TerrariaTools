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

  public const string CallTargetResolution = """
      namespace Demo;

      public interface IContract
      {
        int Apply(int value);
      }

      public class Base
      {
        public virtual int Virtual(int value) => value;
      }

      public sealed class Derived : Base, IContract
      {
        public override int Virtual(int value) => value + 1;
        public int Apply(int value) => value + 2;

        public int Run(Base baseValue, IContract contract, int value)
        {
          var first = baseValue.Virtual(value);
          var second = contract.Apply(value);
          var third = System.Math.Abs(value);
          return first + second + third + value.Extend();
        }
      }

      public static class CallTargetExtensions
      {
        public static int Extend(this int value) => value + 3;
      }
      """;

  /// <summary>
  /// 稀疏位集溢出临界构造：4 个参数定义（含 <c>ref</c>/<c>out</c>）在同一使用点同时活跃。
  ///
  /// **为什么要多参数**：实测（1692 个真实方法）确认 <c>cardMax ≤ 参数个数</c> 恒成立，
  /// 且 74.4% 的方法取等号——out-set 的基数由**同时活跃的参数定义**驱动。
  /// 纯局部变量构造（单参数 + 多个局部）实测 <c>cardMax == 1</c>，
  /// **无法触发** <c>k=1</c> 的溢出通道，会让"溢出不截断"测试变成空测试。
  /// 本 fixture 实测 <c>cardMax == 4</c>、9 个节点的基数 ≥ 2。
  /// </summary>
  public const string DataFlowSparseOverflowRefOut = """
      namespace Demo;

      public sealed class SparseOverflowFour
      {
        public int Run(int seed, ref int first, ref int second, out int third)
        {
          first = seed + 1;
          second = seed + 2;
          third = seed + 3;
          return first + second + third;
        }
      }
      """;

  /// <summary>
  /// 稀疏位集溢出临界构造（纯值参数版）：5 个参数定义同时活跃。
  /// 实测 <c>cardMax == 5</c>、10 个节点的基数 ≥ 2。
  /// 与 <see cref="DataFlowSparseOverflowRefOut"/> 互为佐证：
  /// 溢出由参数定义数驱动，与参数是否为 <c>ref</c>/<c>out</c> 无关。
  /// </summary>
  public const string DataFlowSparseOverflowValueParams = """
      namespace Demo;

      public sealed class SparseOverflowFive
      {
        public int Run(int a, int b, int c, int d, int e)
        {
          var total = a + b;
          total += c;
          total += d;
          return total + e;
        }
      }
      """;
  /// <summary>
  /// 跨过程计划容量压力构造：单个方法内 <paramref name="callCount"/> 个独立调用点，
  /// 每个调用点解析到同一个 <c>Leaf</c>，因此每个调用点各自形成一个计划组。
  /// </summary>
  /// <remarks>
  /// M1 发布窗口的双上界是 64 组 / 131072 行，故 <c>callCount &gt; 64</c> 才能真正跨越窗口边界
  /// （64 组窗口 + 余数），否则"窗口容量治理"这条路径从未被执行过，测试是空的。
  /// 实测（DOP=1）：calls=70 → 70/140/4971 条边（预算 1/2/10000），calls=90 → 90/180/8191。
  /// 调用点数量改变会显著改变边数，故断言不得写死边数，只能断言账本关系。
  /// </remarks>
  /// <summary>
  /// 行上界压力构造：被调方法带 <paramref name="parameterCount"/> 个形参，且被调用
  /// <paramref name="callCount"/> 次，使【每个计划组】的行数约为
  /// <c>parameterCount * callCount</c>。
  /// </summary>
  /// <remarks>
  /// 为什么需要它：<see cref="InterproceduralPlanCapacityPressure"/> 的每个组只有 O(1) 行，
  /// 只能把【组】上界（64）撑满，永远碰不到【行】上界（131072）。
  /// 实测确认的组大小定律是 <b>组行数 ≈ 形参个数 × 指向该方法的调用点数</b>
  /// （因为实参边桶按被调方法共享，每个调用点都拿到整桶）：
  ///
  /// - <c>params=40, calls=64</c> → maxCount=2562，峰值 <b>133173 行 &gt; 131072</b>，
  ///   且 <b>peakGroups=52 &lt; 64</b> —— 证明冲刷确实由【行】而非【组】触发，耗时约 9 s。
  /// - <c>params=20, calls=64</c> → maxCount=1282，峰值 81985 行，仍未越界（对照组）。
  ///
  /// 注意：单独靠"加大形参个数"构造该形态代价极高且超线性
  /// （params=5000 约 84 s、params=20000 约 512 s），是行不通的路子；
  /// 【形参 × 调用点】才是低成本构造方式。
  /// </remarks>
  public static string InterproceduralPlanRowThresholdPressure(
    int parameterCount,
    int callCount)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(parameterCount);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(callCount);

    var parameters = string.Join(
      ", ",
      Enumerable.Range(0, parameterCount).Select(index => $"int p{index}"));

    var body = new System.Text.StringBuilder();
    for (var index = 0; index < parameterCount; index += 1)
    {
      body.AppendLine($"      p{index} = p{index} + 1;");
    }

    var argumentList = string.Join(
      ", ",
      Enumerable.Range(0, parameterCount).Select(index => $"seed + {index}"));

    var calls = new System.Text.StringBuilder();
    for (var index = 0; index < callCount; index += 1)
    {
      calls.AppendLine($"    var r{index} = Leaf({argumentList});");
    }

    var sum = string.Join(
      " + ",
      Enumerable.Range(0, callCount).Select(index => $"r{index}"));

    return $$"""
        namespace Demo;

        public sealed class RowThresholdPressure
        {
          public int Leaf({{parameters}})
          {
        {{body}}    return p0;
          }

          public int Run(int seed)
          {
        {{calls}}    return {{sum}};
          }
        }
        """;
  }

  public static string InterproceduralPlanCapacityPressure(int callCount)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(callCount);

    var builder = new System.Text.StringBuilder();
    for (var index = 0; index < callCount; index += 1)
    {
      builder.AppendLine($"    var r{index} = Leaf(seed + {index});");
    }

    var sum = string.Join(
      " + ",
      Enumerable.Range(0, callCount).Select(index => $"r{index}"));

    return $$"""
        namespace Demo;

        public sealed class CapacityPressure
        {
          public int Leaf(int value)
          {
            return value + 1;
          }

          public int Run(int seed)
          {
        {{builder}}    return {{sum}};
          }
        }
        """;
  }

}
