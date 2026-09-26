using NLCPG.Builder;
using NLCPG.Contracts;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

/// <summary>
/// G0-P **R-1** 的补漏验证：<c>Syntax</c>/<c>Operation</c> 必须**真的进入**被校验的执行序列。
/// <para>
/// <b>为什么单列一个文件：</b>权威表 <see cref="StageDependencyTable"/> 里声明了
/// <c>Operation ← Syntax</c> 与 <c>CallGraph ← Operation</c> 两条边，
/// 但这两个阶段在本 builder 中**不**经 <c>RunOptionalPass</c> 执行（后者是
/// <c>_executedStageOrder.Add</c> 的**唯一**原有入口），而是由 <c>MeasureStage</c> 包裹。
/// 于是它们**从未**进入被校验的序列，而 <c>TryValidateOrder</c> 把"缺席的前置"
/// 当作"未请求 ⇒ 视为已满足"（这是"能力未开启"的正确语义）。
/// </para>
/// <para>
/// <b>后果：那两条边**永远不可能失败**——声明在，机制不在。</b>
/// 这与附录 N.4/P.4 的「声明代替机制」同型，也与附录 Y.4 同型：
/// <b>"从未记录"与"没有机制"在行为层完全等价</b>（去掉补记后，全部行为用例仍然全绿），
/// 故判据只能落在**结构/序列层**——即断言记录里**确实包含**这两个阶段。
/// </para>
/// <para>
/// ⚠ 本文件只做**只读观测**，不改变任何执行行为。
/// </para>
/// </summary>
public sealed class StageExecutionRecordingTests
{
    private const string Source = """
        namespace Demo;

        public sealed class Sample
        {
            public int Adjust(int value)
            {
                if (value > 0)
                {
                    value += 1;
                }

                return value;
            }
        }
        """;

    /// <summary>
    /// **核心用例：`Syntax`/`Operation` 必须出现在被校验的执行序列中。**
    /// <para>
    /// <b>为什么这条不可省：</b>补记这两行之前，本断言会失败（序列里没有它们），
    /// 而其余全部行为断言**依然全绿**——这正是"声明代替机制"的判别面。
    /// 若把 <c>MeasureStage</c> 的 <c>recordedStage</c> 实参去掉，
    /// 本用例立刻失败，而其他任何用例都不会。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_RecordsSyntaxAndOperationInTheValidatedOrder()
    {
        var builder = Build(new[] { NLCPGCapability.DataFlow });

        var order = builder.LastExecutedStageOrder;

        Assert.Contains(StageDependencyTable.Stage.Syntax, order);
        Assert.Contains(StageDependencyTable.Stage.Operation, order);
    }

    /// <summary>
    /// **顺序必须正确**：`Syntax` 先于 `Operation`，且二者都先于全部后置 pass 阶段。
    /// <para>
    /// 只断言"包含"还不够——把两者都追加到序列末尾同样满足"包含"，
    /// 却会让权威表里的 <c>CallGraph ← Operation</c> 反过来恒假（误报）。
    /// 故必须钉住**相对位置**。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_PlacesSyntaxBeforeOperationAndBothBeforePostPassStages()
    {
        var builder = Build(new[] { NLCPGCapability.DataFlow });

        var order = builder.LastExecutedStageOrder;
        var syntaxIndex = order.ToList().IndexOf(StageDependencyTable.Stage.Syntax);
        var operationIndex = order.ToList().IndexOf(StageDependencyTable.Stage.Operation);
        var callGraphIndex = order.ToList().IndexOf(StageDependencyTable.Stage.CallGraph);

        Assert.True(syntaxIndex >= 0, "Syntax 未被记录。");
        Assert.True(operationIndex >= 0, "Operation 未被记录。");
        Assert.True(syntaxIndex < operationIndex, "Syntax 必须早于 Operation。");

        // CallGraph 是第一个后置 pass 阶段；Operation 必须早于它。
        Assert.True(callGraphIndex >= 0, "CallGraph 未被记录。");
        Assert.True(operationIndex < callGraphIndex, "Operation 必须早于 CallGraph。");
    }

    /// <summary>
    /// **记录的序列必须仍然通过权威表校验**——即补记没有引入假阳性。
    /// <para>
    /// 与上面两条**成对**：只证明"记录了"无法排除"记录错了导致每次构建都抛"。
    /// 本用例断言真实构建的成功路径，故补记既**有效**又**不误报**。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_RecordedOrderSatisfiesTheAuthoritativeTable()
    {
        var builder = Build(new[] { NLCPGCapability.ControlDependence });

        Assert.True(
          StageDependencyTable.TryValidateOrder(builder.LastExecutedStageOrder, out var reason),
          $"补记后的执行序列未通过权威表校验：{reason}");
    }

    /// <summary>
    /// **能力未请求时，该阶段不得被记录**——记录必须如实反映"真的执行了"。
    /// <para>
    /// 若把 <c>Operation</c> 无条件记进序列（例如记在 <c>if</c> 之外），
    /// 会在未请求 MethodModel 的构建里**谎报**执行过，从而让"缺席=未请求"的
    /// 正确语义被破坏。本用例守住这条边界。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenMethodModelNotRequested_DoesNotRecordOperation()
    {
        var builder = Build(new[] { NLCPGCapability.SyntaxSemantic });

        var order = builder.LastExecutedStageOrder;

        // Syntax 总会跑（任何非空能力都蕴含 SyntaxSemantic）。
        Assert.Contains(StageDependencyTable.Stage.Syntax, order);
        // Operation 由 RequiresMethodModel 门控 ⇒ 未请求就不该出现。
        Assert.DoesNotContain(StageDependencyTable.Stage.Operation, order);
    }

    private static NLCPGBuilder Build(NLCPGCapability[] capabilities)
    {
        var builder = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = 1,
            RequestedCapabilities = capabilities,
        });

        builder.BuildFromSource(Source, "stage-recording.cs");

        return builder;
    }
}
