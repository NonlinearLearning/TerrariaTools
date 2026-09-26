using System.Runtime.CompilerServices;
using NLCPG.Builder;
using NLCPG.Builder.Concurrency;
using NLCPG.Builder.Streaming;
using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;
using Xunit.Abstractions;

namespace RoslynPrototype.Tests;

/// <summary>
/// G0-M「估算校准」：把 CPG 侧那些**硬编码的字节常量**与运行时的**实测结构宽度**对齐。
/// <para>
/// 计划 :114 明确要求：「估算须来自相同夹具的实测校准；源码字符数、行数乘常量**不是**可靠
/// bytes 上界」。设计 R6 同样要求先校准再把它当硬额度用。
/// </para>
/// <para>
/// 本文件只做**测量与关系断言**，不改变任何生产估算公式：先证明现状到底偏多少，
/// 再决定是否需要改常量。测量手法沿用本仓库既有先例
/// （<c>LocalCpgFragmentContractTests</c> 的 <c>Unsafe.SizeOf&lt;T&gt;()</c> +
/// <c>GC.GetAllocatedBytesForCurrentThread</c>），而非按字段数推导。
/// </para>
/// <para>
/// ⚠️ <b>不得外推</b>：<c>Unsafe.SizeOf&lt;T&gt;()</c> 是**载荷结构宽度**，
/// 既不是 GC 实际占用（含对象头/对齐/装箱），也不包含数组头、<c>List</c> 保留容量、
/// 字符串驻留与图结构开销。故本文件**不**声称"这就是峰值内存上界"。
/// </para>
/// </summary>
public sealed class CpgFragmentByteCalibrationTests
{
    /// <summary>生产代码里 <c>FragmentBytes</c> 使用的每节点常量（ControlFlowPass/DominancePass/ControlDependencePass）。</summary>
    private const int HardcodedBytesPerNodeDescriptor = 64;

    /// <summary>生产代码里 <c>FragmentBytes</c> 使用的每边常量。</summary>
    private const int HardcodedBytesPerEdgeCandidate = 48;

    /// <summary>生产代码里 <c>EstimatedBytesPerCostUnit</c>（CpgWorkBatchBuilder.cs:7）。</summary>
    private const int HardcodedBytesPerCostUnit = 64;

    private readonly ITestOutputHelper _output;

    public CpgFragmentByteCalibrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void NodeDescriptor_MeasuredWidth_IsAtLeastWhatTheFragmentEstimateAssumes()
    {
        var anchorWidth = Unsafe.SizeOf<StableNodeAnchor>();
        var descriptorWidth = Unsafe.SizeOf<CpgNodeDescriptor>();

        _output.WriteLine(
          $"StableNodeAnchor={anchorWidth}; CpgNodeDescriptor={descriptorWidth}; " +
          $"hardcoded-per-node={HardcodedBytesPerNodeDescriptor}");

        // 生产公式把每个节点记作 64 字节。若实测宽度大于该常量，
        // 则 FragmentBytes 是一个**低估**，不能当作任何形式的上界。
        Assert.True(
          descriptorWidth >= HardcodedBytesPerNodeDescriptor,
          $"CpgNodeDescriptor is {descriptorWidth} bytes but the estimate assumes " +
          $"{HardcodedBytesPerNodeDescriptor}; the estimate is an UNDER-estimate and cannot be an upper bound.");
    }

    [Fact]
    public void EdgeCandidate_MeasuredWidth_IsAtLeastWhatTheFragmentEstimateAssumes()
    {
        var candidateWidth = Unsafe.SizeOf<CpgEdgeCandidate>();

        _output.WriteLine(
          $"CpgEdgeCandidate={candidateWidth}; hardcoded-per-edge={HardcodedBytesPerEdgeCandidate}");

        Assert.True(
          candidateWidth >= HardcodedBytesPerEdgeCandidate,
          $"CpgEdgeCandidate is {candidateWidth} bytes but the estimate assumes " +
          $"{HardcodedBytesPerEdgeCandidate}; the estimate is an UNDER-estimate and cannot be an upper bound.");
    }

    [Fact]
    public void FragmentBytesFormula_WhenAppliedToRealWidths_UnderestimatesTheMeasuredPayload()
    {
        // 在同一夹具上同时算「生产公式」与「实测宽度公式」，给出**偏了多少**这个可复核的数字。
        const int nodeCount = 1_000;
        const int edgeCount = 4_000;

        var descriptorWidth = Unsafe.SizeOf<CpgNodeDescriptor>();
        var candidateWidth = Unsafe.SizeOf<CpgEdgeCandidate>();

        var productionEstimate =
          (long)nodeCount * HardcodedBytesPerNodeDescriptor + (long)edgeCount * HardcodedBytesPerEdgeCandidate;
        var measuredWidthPayload =
          (long)nodeCount * descriptorWidth + (long)edgeCount * candidateWidth;

        var ratio = measuredWidthPayload / (double)productionEstimate;

        _output.WriteLine(
          $"nodes={nodeCount}; edges={edgeCount}; " +
          $"production-estimate={productionEstimate}; measured-width-payload={measuredWidthPayload}; " +
          $"ratio={ratio:F3}");

        // 记录现状：生产公式在该夹具上是低估。此处断言"低估"这一事实本身，
        // 若未来有人把常量调到 >= 实测宽度，本断言会失败并提示重新校准——这是有意为之。
        Assert.True(
          measuredWidthPayload > productionEstimate,
          $"expected the hardcoded constants to under-estimate the measured payload, " +
          $"but estimate={productionEstimate} and measured={measuredWidthPayload}.");
    }

    [Fact]
    public void EstimatedBytesAndFragmentBytes_MeasureDifferentQuantities_SoTelemetryIsNotACalibration()
    {
        // 遥测同时输出 EstimatedBytes 与 FragmentBytes，看起来像"估算 vs 实测"。
        // 但两者都是**公式**，且自变量不同：
        //   EstimatedBytes  = 行数 × 64          （CpgWorkBatchBuilder.EstimateBytes）
        //   FragmentBytes   = 节点数×64 + 边数×48 （各 pass 的 CpgFragmentMetrics）
        // 行数与节点/边数之间没有固定关系，故这条比值**不能**当作估算误差。
        var costOptions = CpgWorkBatchCostOptions.Default;
        var estimate = CpgWorkBatchCostModel.Estimate(startLine: 0, endLine: 999, costOptions);

        var batchEstimate = new CpgWorkBatchBuilder(costOptions).Build(
          "calibration.cs",
          new[]
          {
              new CpgWorkItem(
                stableOrder: 0,
                sourceFilePath: "calibration.cs",
                methodSymbolKey: "M:0",
                spanStart: 0,
                spanEnd: 999,
                estimatedCost: estimate.Cost,
                kind: CpgWorkItemKind.Method),
          });

        var estimatedBytes = batchEstimate[0].EstimatedBytes;

        // 同一个"1000 行"输入，两个公式给出两个不同的量：本用例固定它们的定义关系，
        // 使任何把二者直接相除当作"校准误差"的解读都能被识别为错误。
        Assert.Equal(estimate.Cost * HardcodedBytesPerCostUnit, estimatedBytes);

        _output.WriteLine(
          $"lines=1000; cost={estimate.Cost}; sizeClass={estimate.SizeClass}; " +
          $"EstimatedBytes={estimatedBytes}; " +
          $"FragmentBytes(for 1000 nodes/4000 edges)=" +
          $"{(1000L * HardcodedBytesPerNodeDescriptor) + (4000L * HardcodedBytesPerEdgeCandidate)}");
    }

    [Fact]
    public void Estimate_IsAFunctionOfLineSpanOnly_SoNoConstantCanMakeItAPayloadUpperBound()
    {
        // 这是"估算校准"无法靠**调常量**解决的根本原因：
        // CpgWorkBatchCostModel.Estimate(startLine, endLine) 的自变量**只有行号**。
        // 因此任何两个行范围相同的片段都会得到**完全相同**的 EstimatedBytes，
        // 而它们真实的节点/边数量可以相差一个数量级 —— 于是不存在任何常量 k，
        // 使 "行数 × k" 成为节点/边载荷的上界。
        //
        // 本用例固定行范围、让载荷在其上变化，直接证明该映射不是单射。
        var costOptions = CpgWorkBatchCostOptions.Default;
        var estimate = CpgWorkBatchCostModel.Estimate(startLine: 0, endLine: 999, costOptions);

        var descriptorWidth = Unsafe.SizeOf<CpgNodeDescriptor>();
        var candidateWidth = Unsafe.SizeOf<CpgEdgeCandidate>();

        // 同一个 1000 行范围下的三种极端形态。
        var sparsePayload = (long)10 * descriptorWidth + (long)10 * candidateWidth;
        var densePayload = (long)5_000 * descriptorWidth + (long)20_000 * candidateWidth;

        // 估算对三者完全相同（只依赖行号）。
        var estimateForSparse = CpgWorkBatchCostModel.Estimate(startLine: 0, endLine: 999, costOptions);
        var estimateForDense = CpgWorkBatchCostModel.Estimate(startLine: 0, endLine: 999, costOptions);

        Assert.Equal(estimate.Cost, estimateForSparse.Cost);
        Assert.Equal(estimate.Cost, estimateForDense.Cost);

        // 载荷却相差三个数量级。
        var spread = densePayload / (double)sparsePayload;
        _output.WriteLine(
          $"same line span 0..999 -> identical cost={estimate.Cost}; " +
          $"sparsePayload={sparsePayload}; densePayload={densePayload}; spread={spread:F1}x");

        Assert.True(
          spread >= 100,
          $"expected the payload spread for an identical line span to be large, but it was only {spread:F1}x.");

        // 结论（作为断言固化，防止后人把 EstimatedBytes 当上界用）：
        // 要让"行数 × k"覆盖 densePayload，k 必须 >= densePayload/1000；
        // 但同一个 k 会把 sparsePayload 高估到荒谬的程度。任何单一常量都无法同时成立。
        var requiredForDense = densePayload / 1000.0;
        var overstatementForSparse = requiredForDense * 1000 / (double)sparsePayload;
        _output.WriteLine(
          $"k to cover dense={requiredForDense:F1} B/line, which would overstate sparse by {overstatementForSparse:F1}x");

        Assert.True(
          overstatementForSparse >= 100,
          "the constant needed to cover the dense case must wildly overstate the sparse case.");
    }

    [Fact]
    public void ProductionConstants_StillMatchTheValuesThisCalibrationWasMeasuredAgainst()
    {
        // 本文件的测量与断言都对照上面两个常量。它们**不是**从产品代码导出的
        // （产品里是私有 const 与内联字面量），所以必须显式钉住来源：
        // 一旦产品改了这些常量，本校准立即失效，必须重新测量。
        // 这是"防止校准悄悄过期"的守卫，而不是重复产品逻辑。
        var repositoryRoot = FindRepositoryRoot();
        var costModelSource = File.ReadAllText(Path.Combine(
          repositoryRoot, "src", "NLCPG", "Builder", "Concurrency", "CpgWorkBatchBuilder.cs"));
        var controlFlowSource = File.ReadAllText(Path.Combine(
          repositoryRoot, "src", "NLCPG", "Builder", "Passes", "ControlFlowPass.cs"));
        var dominanceSource = File.ReadAllText(Path.Combine(
          repositoryRoot, "src", "NLCPG", "Builder", "Passes", "DominancePass.cs"));

        Assert.Contains(
          $"EstimatedBytesPerCostUnit = {HardcodedBytesPerCostUnit}",
          costModelSource,
          StringComparison.Ordinal);

        // 两个 pass 都用同一形状的公式，故逐一定位，避免其中一处被改而另一处漏检。
        foreach (var (name, source) in new[]
                 {
                     ("ControlFlowPass", controlFlowSource),
                     ("DominancePass", dominanceSource),
                 })
        {
            Assert.True(
              source.Contains(
                $".Length * {HardcodedBytesPerNodeDescriptor} + ",
                StringComparison.Ordinal),
              $"{name} no longer multiplies node descriptors by {HardcodedBytesPerNodeDescriptor}; " +
              "the fragment-byte calibration in this file is stale.");

            Assert.True(
              source.Contains(
                $".Length * {HardcodedBytesPerEdgeCandidate}",
                StringComparison.Ordinal),
              $"{name} no longer multiplies edge candidates by {HardcodedBytesPerEdgeCandidate}; " +
              "the fragment-byte calibration in this file is stale.");
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src", "NLCPG")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }

    [Fact]
    public void ByteBudget_AtDefaultOptions_CannotBindBecauseCostLimitsFlushFirst()
    {
        // G0-M 关心的「有界产物」在生产里由**两条**上限共同决定：
        //   1. 成本上限 TargetBatchCost = 400（CpgWorkBatchCostOptions.Default）
        //   2. 字节上限 WorkBatchMaxEstimatedBytesPerBatch = 1 MiB（NLCPGBuilderOptions 默认）
        // 而每单位成本被记为 64 字节（CpgWorkBatchBuilder.EstimateBytes）。
        // 于是成本上限触发时，字节最多只有 400 × 64 = 25,600 B，
        // 仅占 1 MiB 的约 2.4% —— 也就是说**默认配置下字节上限永远不会先触发**，
        // 真正限制批次大小的是成本上限。
        //
        // 本用例用真实打包器实测该关系，而不是复述常量：
        // 这也是"bytes 估算"目前不能作为独立内存门槛的证据。
        var costOptions = CpgWorkBatchCostOptions.Default;
        var options = NLCPGBuilderOptions.CreateDefault();
        var builder = new CpgWorkBatchBuilder(
          costOptions,
          options.EffectiveWorkBatchMaxMethodsPerBatch,
          options.EffectiveWorkBatchMaxEstimatedBytesPerBatch);

        // 铺满一整屏中等规模方法（cost 200 = Medium 上界），逼成本上限先触发。
        var items = Enumerable.Range(0, 500)
          .Select(index => new CpgWorkItem(
            stableOrder: index,
            sourceFilePath: "bounded.cs",
            methodSymbolKey: $"M:{index}",
            spanStart: index * 1_000,
            spanEnd: index * 1_000 + 200,
            estimatedCost: 200,
            kind: CpgWorkItemKind.Method))
          .ToArray();

        var batches = builder.Build("bounded.cs", items);

        var byteLimit = options.EffectiveWorkBatchMaxEstimatedBytesPerBatch;
        var maxObservedBytes = batches.Max(batch => (long)batch.EstimatedBytes);
        var maxObservedCost = batches.Max(batch => batch.EstimatedCost);
        var costBoundBytes = (long)costOptions.TargetBatchCost * HardcodedBytesPerCostUnit;

        _output.WriteLine(
          $"byteLimit={byteLimit}; costLimit={costOptions.TargetBatchCost}; " +
          $"maxObservedCost={maxObservedCost}; maxObservedBytes={maxObservedBytes}; " +
          $"costBoundBytes={costBoundBytes}; batches={batches.Count}");

        // 实测：没有一批接近 1 MiB 字节上限。
        Assert.True(
          maxObservedBytes < byteLimit,
          $"expected the byte budget ({byteLimit}) to be slack, but a batch reached {maxObservedBytes}.");

        // 实测：批次被**成本**上限约束在 400 附近（不是被字节上限约束）。
        Assert.True(
          maxObservedCost <= costOptions.TargetBatchCost,
          $"expected cost-limited batches <= {costOptions.TargetBatchCost}, but saw {maxObservedCost}.");

        // 关系成立：成本上限 × 每单位字节 << 字节上限。若未来有人调低字节上限或调高
        // TargetBatchCost 使二者可比，本断言会失败并提示重新评估"哪条上限在起作用"。
        Assert.True(
          costBoundBytes < byteLimit,
          $"cost-bound bytes ({costBoundBytes}) should be below the byte budget ({byteLimit}); " +
          "if this changed, the byte budget is now the binding constraint and must be re-evaluated.");
    }
}
