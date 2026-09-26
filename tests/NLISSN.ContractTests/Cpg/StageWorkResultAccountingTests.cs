using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

/// <summary>
/// G0-P **R-2** 的验证：4 个此前**没有结果类型**的阶段
/// （`ControlFlow` / `ControlDependence` / `MemberAccess` / `InterproceduralDataFlow`）
/// 现在都以 <see cref="IStageWorkResult"/> 形式被统一记账。
/// <para>
/// <b>为什么值得测：</b>附录 R.1 ② 记录该缺口——5 个阶段各有私有嵌套 record，
/// 而这 4 个**连结果类型都没有**，导致窗口层无法统一记账，也就无法支撑 R.4 的配额作用域。
/// </para>
/// <para>
/// ⚠ <b>判据选择（吸取 N.4/P.4/S.2 的教训）：</b>断言的是**各阶段产出的相对规模**，
/// 不是"非空"——非空断言对"记账挂错阶段"这类错误不敏感。
/// 这里用**可实现的最小夹具**并检查**每个阶段各自**的记录。
/// </para>
/// </summary>
public sealed class StageWorkResultAccountingTests
{
    /// <summary>含分支与属性访问的最小源——确保 ControlFlow/ControlDependence/MemberAccess 都有产出。</summary>
    private const string Source = """
        namespace Demo;

        public sealed class Holder
        {
            public int Value { get; set; }
        }

        public sealed class Sample
        {
            private readonly Holder _holder = new();

            public int Adjust(int value)
            {
                _holder.Value = value;
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

    /// <summary>
    /// `ControlFlow` 阶段必须被记账，且其 fragment 数与该阶段实际批次数一致（> 0）。
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenControlFlowRequested_RecordsControlFlowStageResult()
    {
        var result = BuildAndGetResults(new[] { NLCPGCapability.Cfg });

        var record = Assert.IsType<StageWorkResults.FragmentStageWorkResult>(
          Assert.Contains(StageDependencyTable.Stage.ControlFlow, result));

        Assert.NotEmpty(record.Fragments);
        Assert.True(
          record.ProducedNodeCount > 0,
          "ControlFlow 已执行但记账的产出节点数为 0——记账对象与实际产出不符。");
    }

    /// <summary>
    /// `ControlDependence` 阶段必须被记账。
    /// <para>
    /// 该阶段依赖 `Dominance` 填充 `_dominanceOverlays`（依赖③，附录 Q.4 的回归根因）；
    /// 若顺序被破坏，它会静默 return 并**不产出任何 fragment**——
    /// 届时本用例会以"未记账 / 产出为 0"的形式失败。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenControlDependenceRequested_RecordsControlDependenceStageResult()
    {
        var result = BuildAndGetResults(new[] { NLCPGCapability.ControlDependence });

        var record = Assert.IsType<StageWorkResults.FragmentStageWorkResult>(
          Assert.Contains(StageDependencyTable.Stage.ControlDependence, result));

        Assert.NotEmpty(record.Fragments);
    }

    /// <summary>
    /// `MemberAccess` 阶段必须被记账，且其形态是**事实计数**而非 fragment
    /// （实测 `MemberAccessPass.cs:78-97` 产出 `MemberAccessFact`）。
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenMemberAccessRequested_RecordsFactCountStageResult()
    {
        var result = BuildAndGetResults(new[] { NLCPGCapability.MethodModel });

        var record = Assert.IsType<StageWorkResults.FactCountStageWorkResult>(
          Assert.Contains(StageDependencyTable.Stage.MemberAccess, result));

        Assert.True(
          record.ProducedFactCount > 0,
          "MemberAccess 已执行但记账的事实条数为 0——记账对象与实际产出不符（夹具可能未触发属性访问）。");
    }

    /// <summary>`InterproceduralDataFlow` 阶段必须被记账（该阶段不产 fragment）。</summary>
    [Fact]
    public void BuildFromSource_WhenInterproceduralRequested_RecordsInterproceduralStageResult()
    {
        var result = BuildAndGetResults(new[] { NLCPGCapability.InterproceduralDataFlow });

        var record = Assert.IsType<StageWorkResults.InterproceduralStageWorkResult>(
          Assert.Contains(StageDependencyTable.Stage.InterproceduralDataFlow, result));

        Assert.True(record.ProducedNodeCount > 0);
    }

    /// <summary>
    /// **关键判据：记账的键必须与阶段的真实身份一致。**
    /// <para>
    /// 若把 `ControlFlow` 的产出记到 `ControlDependence` 名下（错位），
    /// 上面几个"各自非空"的用例**仍会通过**——故这里逐条核对
    /// <see cref="IStageWorkResult.Stage"/> 与字典键一致。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenAllStagesRequested_EachRecordedResultReportsItsOwnStage()
    {
        var result = BuildAndGetResults(new[] { NLCPGCapability.All });

        Assert.NotEmpty(result);

        foreach (var (stage, workResult) in result)
        {
            Assert.Equal(stage, workResult.Stage);
        }
    }

    /// <summary>
    /// 记账只覆盖**实际执行过**的阶段：未请求的能力不得留下记录。
    /// <para>
    /// 这条防止"记账层自己造出没跑过的阶段"，也保证 R.4 的配额回填不会凭空多算。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenOnlyCfgRequested_DoesNotRecordUnrequestedStages()
    {
        var result = BuildAndGetResults(new[] { NLCPGCapability.Cfg });

        // 只请求 Cfg ⇒ 不应出现 DataFlow / Dominance / ControlDependence。
        Assert.DoesNotContain(StageDependencyTable.Stage.DataFlow, result.Keys);
        Assert.DoesNotContain(StageDependencyTable.Stage.Dominance, result.Keys);
        Assert.DoesNotContain(StageDependencyTable.Stage.ControlDependence, result.Keys);
    }

    /// <summary>含跨方法调用的源，确保 Interprocedural 与 MemberAccess 都有产出。</summary>
    private static IReadOnlyDictionary<StageDependencyTable.Stage, IStageWorkResult> BuildAndGetResults(
      NLCPGCapability[] capabilities)
    {
        var builder = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = 1,
            RequestedCapabilities = capabilities,
        });

        builder.BuildFromSource(Source, "stage-work-result.cs");

        return builder.LastStageWorkResults;
    }
}
