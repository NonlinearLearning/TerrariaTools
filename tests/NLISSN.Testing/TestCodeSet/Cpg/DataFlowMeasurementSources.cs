using System.Text;

namespace RoslynPrototype.Tests.TestCodeSet.Cpg;

public static class DataFlowMeasurementSources {
  public static string Sparse() {
    var source = new StringBuilder("public static class Sample { public static int Run(int seed) {\n");
    for (int index = 0; index < 64; index++) {
      source.AppendLine($"int v{index} = seed + {index};");
    }
    source.Append("return " + string.Join(" + ", Enumerable.Range(0, 64).Select(i => $"v{i}")));
    return source.AppendLine("; } }").ToString();
  }

  public static string Collision() {
    var source = new StringBuilder("public sealed class Box { public bool A; public bool B; }\n" +
        "public static class Sample { public static int Run(Box box, bool choose, int seed) {\n");
    for (int index = 0; index < 32; index++) {
      source.AppendLine($"if (box.A = choose) {{ seed = box.A ? seed + {index} : seed - {index}; }} " +
          "else { box.B = choose; }");
    }
    return source.AppendLine("return (box.A || box.B) ? seed : -seed; } }").ToString();
  }

  public static string JoinLoop() {
    var source = new StringBuilder("public static class Sample { " +
        "public static int Run(int seed, int limit, bool choose) { int total = seed;\n");
    for (int loop = 0; loop < 4; loop++) {
      // The CFG cycle needs a definition on its condition node to exercise re-enqueue.
      source.AppendLine($"for (int i{loop} = 0; (choose = i{loop} < limit); i{loop}++) {{");
      for (int branch = 0; branch < 4; branch++) {
        source.AppendLine($"if (choose) {{ total = total + i{loop}; }} " +
            $"else {{ total = total - {branch}; }}");
      }
      source.AppendLine("}");
    }
    return source.AppendLine("return total; } }").ToString();
  }

  // P02 T2 input: operations whose whole subtree carries no used fact (the `true` literal of
  // each `if`) while still owning CFG predecessors, interleaved with real data flow so the same
  // method also contains operations that do have facts. This separates "no used fact at all"
  // from "has facts but an empty predecessor set".
  public static string ZeroFactOps() {
    var source = new StringBuilder("public static class Sample { public static int Run(int seed) {\n");
    source.AppendLine("int total = seed;");
    for (int index = 0; index < 16; index++) {
      source.AppendLine(index % 2 == 0
          ? $"if (true) {{ total = total + {index}; }}"
          : $"total = total + {index};");
    }
    return source.AppendLine("return total; } }").ToString();
  }

  // P02 T2 boundary C: mostly fact-free with a few fact-bearing operations. Local declaration
  // targets, assignment targets and literals yield no used fact, so 9 of the 16 ordered
  // operations are skipped while the remaining ones still run the candidate stage. Measured:
  // CandidateOperationVisits == 16, CandidateZeroFactSkips == 9, CandidatePredecessorVisits
  // drops 3 -> 2.
  public static string AllZeroFactMethod() {
    return "public static class Sample { public static int Run() { " +
        "int x = 1; x = 2; x = 3; return 4; } }\n";
  }

  // P02 T2 boundary A: a fact-bearing operation whose candidate in-set stays empty. The `Field`
  // reference is a real direct used fact, so the FactCount == 0 short-circuit must NOT fire
  // (CandidateZeroFactSkips stays 0); the operation still executes sets.Clear and then hits the
  // empty-in-set check, which is the pre-existing "cleared then found empty" path that must be
  // preserved. Measured: CandidatePredecessorVisits == CandidateUnions == 0,
  // UsedFactVisits == TryGetCandidatesCalls == 0, DefinitionOperationVisits == 3.
  public static string FactWithEmptyIncomingSet() {
    return "public static class Sample { static int Field; " +
        "public static int Run() { return Field; } }\n";
  }

  // P02 T2 boundary B: the contrast case - a fact-bearing operation whose in-set is NON-empty.
  // The parameter is a direct used fact and a flow node, so the candidate stage does reach
  // TryGetCandidates. Measured: CandidatePredecessorVisits == CandidateUnions == 1,
  // UsedFactVisits == TryGetCandidatesCalls == ReturnedCandidates == 1. Together with boundary A
  // this pins that the two classes stay distinct.
  public static string FactWithIncomingSet() {
    return "public static class Sample { public static int Run(int seed) { return seed; } }\n";
  }

  // P02 T2 strongest input: every operation is fact-free (block-scoped local declarations whose
  // targets and initializers are constants), each with a real CFG predecessor chain. Measured
  // before the short-circuit: CandidatePredecessorVisits == CandidateUnions == 24; after it both
  // are 0, while the fixpoint still enqueues/dequeues every flow node. Nested blocks are what
  // give the fact-free operations predecessors at all - a flat statement list has none.
  public static string ZeroFactChain() {
    var source = new StringBuilder("public static class Sample { public static int Run() {\n");
    for (int index = 0; index < 24; index++) {
      source.AppendLine($"{{ int y{index} = {index}; }}");
    }
    return source.AppendLine("return 0; } }").ToString();
  }
}
