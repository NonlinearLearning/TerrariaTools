using System.Reflection;
using System.Runtime.CompilerServices;
using NLCPG.Builder;
using NLCPG.Builder.Passes;
using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

// 护住【边的产出顺序】：现有跨 DOP oracle（CpgWorkBatchInterproceduralTests.DescribeGraph）
// 在比较前对边做了 OrderBy(SourceNodeId).ThenBy(Kind).ThenBy(TargetNodeId)，因此对
// “顺序变化”是【盲】的。本测试不做任何排序，直接冻结 graph.Edges 的原始插入序。
//
// 为什么顺序真的会变：AddEdge 是 append-only；orderedPlans 的排序层级为
//   StableCallSiteOrder -> BridgeKind -> NodeSortKey(CallSite) -> NodeSortKey(TargetMethod)
//   -> ArgumentOrdinal -> BridgeKind -> NodeSortKey(Source) -> NodeSortKey(Target)
// 同一个 call site、同一种桥之内，次序由 NodeSortKey(Source)/NodeSortKey(Target) 决定。
// 实测基线中 target=149 的 5 条 ArgumentToParameter 边次序为
//   273, 277, 288, 284, 286  —— 不是数值序，正是 NodeSortKey 的字典序。
// 因此若把 NodeSortKey 换成结构体键而语义有偏差，这里会立刻失败。
public sealed class CpgInterproceduralEdgeOrderTests
{
    // 输入需满足：多个 call site、可解析目标、且桥种类不止一种。
    private const string Source = """
      using System;

      public sealed class OrderSample
      {
        public int Leaf(int value)
        {
          return value + 1;
        }

        public int Middle(int value)
        {
          var a = Leaf(value);
          var b = Leaf(a);
          return a + b;
        }

        public int Top(int value)
        {
          var a = Middle(value);
          var b = Middle(a);
          var c = Leaf(b);
          return a + b + c;
        }

        public int Wide(int value, int other)
        {
          return Leaf(value) + Middle(other) + Leaf(other) + Middle(value);
        }

        public int ReturnHeavy(int value)
        {
          var r = Middle(value);
          return r + Middle(r);
        }
      }
      """;

    // 基线冻结（2026-09-23，NodeSortKey 替换【前】采集）。
    // 格式：SourceNodeId>TargetNodeId|BridgeKind|CallSiteSpanStart:SpanEnd
    private static readonly string[] ExpectedInterproceduralOrder =
    {
        "273>149|ArgumentToParameter|156:167",
        "277>149|ArgumentToParameter|156:167",
        "288>149|ArgumentToParameter|156:167",
        "284>149|ArgumentToParameter|156:167",
        "286>149|ArgumentToParameter|156:167",
        "265>155|ReturnToMethodReturn|156:167",
        "155>171|MethodReturnToCallResult|156:167",
        "273>149|ArgumentToParameter|181:188",
        "277>149|ArgumentToParameter|181:188",
        "288>149|ArgumentToParameter|181:188",
        "284>149|ArgumentToParameter|181:188",
        "286>149|ArgumentToParameter|181:188",
        "155>172|MethodReturnToCallResult|181:188",
        "273>149|ArgumentToParameter|307:314",
        "277>149|ArgumentToParameter|307:314",
        "288>149|ArgumentToParameter|307:314",
        "284>149|ArgumentToParameter|307:314",
        "286>149|ArgumentToParameter|307:314",
        "155>175|MethodReturnToCallResult|307:314",
        "273>149|ArgumentToParameter|398:409",
        "277>149|ArgumentToParameter|398:409",
        "288>149|ArgumentToParameter|398:409",
        "284>149|ArgumentToParameter|398:409",
        "286>149|ArgumentToParameter|398:409",
        "155>176|MethodReturnToCallResult|398:409",
        "273>149|ArgumentToParameter|428:439",
        "277>149|ArgumentToParameter|428:439",
        "288>149|ArgumentToParameter|428:439",
        "284>149|ArgumentToParameter|428:439",
        "286>149|ArgumentToParameter|428:439",
        "155>178|MethodReturnToCallResult|428:439",
        "276>150|ArgumentToParameter|257:270",
        "282>150|ArgumentToParameter|257:270",
        "287>150|ArgumentToParameter|257:270",
        "285>150|ArgumentToParameter|257:270",
        "289>150|ArgumentToParameter|257:270",
        "290>150|ArgumentToParameter|257:270",
        "266>156|ReturnToMethodReturn|257:270",
        "156>173|MethodReturnToCallResult|257:270",
        "276>150|ArgumentToParameter|284:293",
        "282>150|ArgumentToParameter|284:293",
        "287>150|ArgumentToParameter|284:293",
        "285>150|ArgumentToParameter|284:293",
        "289>150|ArgumentToParameter|284:293",
        "290>150|ArgumentToParameter|284:293",
        "156>174|MethodReturnToCallResult|284:293",
        "276>150|ArgumentToParameter|412:425",
        "282>150|ArgumentToParameter|412:425",
        "287>150|ArgumentToParameter|412:425",
        "285>150|ArgumentToParameter|412:425",
        "289>150|ArgumentToParameter|412:425",
        "290>150|ArgumentToParameter|412:425",
        "156>177|MethodReturnToCallResult|412:425",
        "276>150|ArgumentToParameter|442:455",
        "282>150|ArgumentToParameter|442:455",
        "287>150|ArgumentToParameter|442:455",
        "285>150|ArgumentToParameter|442:455",
        "289>150|ArgumentToParameter|442:455",
        "290>150|ArgumentToParameter|442:455",
        "156>179|MethodReturnToCallResult|442:455",
        "276>150|ArgumentToParameter|514:527",
        "282>150|ArgumentToParameter|514:527",
        "287>150|ArgumentToParameter|514:527",
        "285>150|ArgumentToParameter|514:527",
        "289>150|ArgumentToParameter|514:527",
        "290>150|ArgumentToParameter|514:527",
        "156>180|MethodReturnToCallResult|514:527",
        "276>150|ArgumentToParameter|544:553",
        "282>150|ArgumentToParameter|544:553",
        "287>150|ArgumentToParameter|544:553",
        "285>150|ArgumentToParameter|544:553",
        "289>150|ArgumentToParameter|544:553",
        "290>150|ArgumentToParameter|544:553",
        "156>181|MethodReturnToCallResult|544:553",
    };

    [Fact]
    public void BuildFromSource_InterproceduralEdges_PreservesBridgeEmissionOrder()
    {
        var graph = Build(Source, "interprocedural-edge-order.cs");

        var actual = DescribeInterproceduralBridgeOrder(graph);

        // 输入必须真的产生跨过程边，否则本测试会退化成恒真的空断言。
        Assert.NotEmpty(actual);
        Assert.Equal(ExpectedInterproceduralOrder, actual);
    }

    [Fact]
    public void BuildFromSource_InterproceduralEdges_AreStableAcrossRepeatedBuilds()
    {
        // 同一进程内重复构建必须完全一致（排除哈希序/字典序抖动）。
        var first = DescribeInterproceduralBridgeOrder(Build(Source, "interprocedural-edge-order.cs"));
        var second = DescribeInterproceduralBridgeOrder(Build(Source, "interprocedural-edge-order.cs"));

        Assert.Equal(first, second);
    }

    [Fact]
    public void BuildFromSource_InterproceduralEdges_HaveStableTotalCount()
    {
        var graph = Build(Source, "interprocedural-edge-order.cs");

        // 总数单独断言：顺序断言若因输入变化而长度改变，这里能给出更清晰的原因。
        Assert.Equal(
          ExpectedInterproceduralOrder.Length,
          graph.Edges.Count(edge => edge.Kind == NLCPGEdgeKind.InterproceduralDataFlow));
    }

    // 护住【方案 A：排序行不内嵌整条计划】这一载体形状。
    // 背景：PlanSortRow 原先按值内嵌计划（历史布局约 428 B/行），
    // 排序时每次比较/搬动都在搬运整份计划。现改为只存 PlanIndex + 4 个排序键，
    // 发布阶段用 plans[row.PlanIndex] 回读【惰性载体】。
    // 本测试直接对私有嵌套类型的【字段类型与实测托管宽度】断言，因此若有人把整条计划
    // 塞回行里（哪怕行为暂时还是对的），这里立即失败。
    [Fact]
    public void PlanSortRow_CarriesPlanIndexInsteadOfFullPlan()
    {
        var rowType = typeof(NLCPGBuilder).GetNestedType(
          "PlanSortRow",
          BindingFlags.NonPublic);
        Assert.NotNull(rowType);
        Assert.True(rowType!.IsValueType, "PlanSortRow 必须保持值类型，避免逐行对象分配。");

        var fieldTypes = rowType
          .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
          .ToDictionary(field => NormalizeFieldName(field.Name), field => field.FieldType, StringComparer.Ordinal);

        // 行的全部载荷字段：没有一个是计划载体或端点节点。
        Assert.Equal(
          new[] { "ArgumentOrdinal", "BridgeKind", "PlanIndex", "SourceKey", "TargetKey" },
          fieldTypes.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(typeof(InterproceduralPlanRef), fieldTypes.Values);
        Assert.DoesNotContain(typeof(NLCPGNode), fieldTypes.Values);

        // 宽度用 Unsafe.SizeOf 实测（不是 Marshal.SizeOf，也不经装箱对象大小换算）。
        var sizeOf = typeof(Unsafe).GetMethods()
          .Single(method => method.Name == "SizeOf" && method.IsGenericMethodDefinition
            && method.GetParameters().Length == 0);
        var rowWidth = (int)sizeOf.MakeGenericMethod(rowType).Invoke(null, null)!;
        var planWidth = Unsafe.SizeOf<InterproceduralPlanRef>();

        // ⚠ ⑥（延迟物化）【反转】了本测试原先的前提。
        // 改前断言的是 `rowWidth < planWidth`（32 < 216）——"排序行必须窄于计划"。
        // 载体降到 8 B 后 32 < 8 为假，故该前提整体失效（载体比行还窄）。
        // 这不是回归：行要持两个字符串引用，载体只需两个 int，行比载体宽才是正确的。
        // 现在的判据是"行宽不超过其冻结布局 32 B"，即行布局未因本项而变宽。
        Assert.True(
          rowWidth <= 32,
          $"排序行宽度 {rowWidth} B 超过冻结布局 32 B，方案 A 的行布局已失效。");
        Assert.True(
          planWidth < rowWidth,
          $"载体 {planWidth} B 应窄于排序行 {rowWidth} B；若不然，延迟物化已退化为内嵌端点。");
        if (Environment.Is64BitProcess)
        {
            // int PlanIndex + int BridgeKind + int ArgumentOrdinal + 2 个引用，按 8 B 对齐 = 32 B。
            Assert.Equal(32, rowWidth);
            // 两个 int，按 4 B 对齐 = 8 B。
            Assert.Equal(8, planWidth);
        }
    }

    // 护住 ContextId 由 CallSiteContext 派生这一不变量。
    // 背景：NLCPGBuilder 的产出侧曾把 callSiteContext.ToContextId() 作为 contextId 实参传入，
    // 与 NLCPGEdge 构造器内部的同一次插值重复（实测该调用点分配 1,061.5 MiB）。
    // 现改为只在构造器内算一次（source 侧不传 contextId）。若有人误删构造器里的派生、
    // 或产出侧漏传 CallSiteContext，ContextId 会变成 null，本测试立即失败。
    [Fact]
    public void BuildFromSource_InterproceduralEdges_DeriveContextIdFromCallSiteContext()
    {
        var graph = Build(Source, "interprocedural-edge-order.cs");

        var bridges = graph.Edges
          .Where(edge => edge.Kind == NLCPGEdgeKind.InterproceduralDataFlow)
          .ToArray();

        Assert.NotEmpty(bridges);
        Assert.All(bridges, edge =>
        {
            // 本 pass 产出的跨过程边两者都必须有值。
            Assert.NotNull(edge.CallSiteContext);
            Assert.NotNull(edge.ContextId);
            // 且 ContextId 必须逐字节等于由 CallSiteContext 现算的值。
            Assert.Equal(
              edge.CallSiteContext!.Value.ToContextId(),
              edge.ContextId!.Value);
        });
    }

    // record struct 的字段带编译器生成的 <Property>k__BackingField 名，比较前归一化。
    private static string NormalizeFieldName(string fieldName)
    {
        const string prefix = "<";
        const string suffix = ">k__BackingField";
        return fieldName.StartsWith(prefix, StringComparison.Ordinal) &&
          fieldName.EndsWith(suffix, StringComparison.Ordinal)
          ? fieldName[prefix.Length..^suffix.Length]
          : fieldName;
    }

    private static string[] DescribeInterproceduralBridgeOrder(NLCPGGraph graph)
    {
        return graph.Edges
          .Where(edge => edge.Kind == NLCPGEdgeKind.InterproceduralDataFlow)
          .Select(edge =>
          {
              var label = edge.StructuredLabel?.StableKey ?? string.Empty;
              var bridge = label.StartsWith("interprocedural-bridge:", StringComparison.Ordinal)
                ? label["interprocedural-bridge:".Length..]
                : label;
              var context = edge.ContextId?.Value ?? string.Empty;
              // ContextId 形如 callsite:{FilePath}:{SpanStart}:{SpanEnd}:{DisplayName}，
              // DisplayName 自身含冒号，故只取前缀之后的【前两个】冒号字段。
              var trimmed = context.StartsWith("callsite:", StringComparison.Ordinal)
                ? context["callsite:".Length..]
                : context;
              const string filePrefix = "interprocedural-edge-order.cs:";
              if (trimmed.StartsWith(filePrefix, StringComparison.Ordinal))
              {
                  trimmed = trimmed[filePrefix.Length..];
              }

              var parts = trimmed.Split(':');
              var span = parts.Length >= 2 ? $"{parts[0]}:{parts[1]}" : trimmed;

              return $"{edge.SourceNodeId}>{edge.TargetNodeId}|{bridge}|{span}";
          })
          .ToArray();
    }

    private static NLCPGGraph Build(string source, string filePath)
    {
        return new NLCPGBuilder(CreateOptions())
          .BuildFromSource(source, filePath);
    }

    private static NLCPGBuilderOptions CreateOptions()
    {
        return NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = 1,
            RequestedCapabilities = new[] { NLCPGCapability.InterproceduralDataFlow },
            LargeFileLineThreshold = 1,
            LargeFileMethodThreshold = 1,
            LargeMethodLineSpanThreshold = 1,
            SyntaxLargeFileLineThreshold = 1,
        };
    }
}
