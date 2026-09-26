using NL.Concurrency;
using NLISSN.Core.Pipeline;
using Xunit;

namespace RoslynPrototype.ContractTests.Concurrency;

/// <summary>
/// 守住「配置值 → 内核/准入配额」这一段的数值边界。
/// <para>
/// YAML 校验（<c>YamlConfigurationLoader</c>）与 JSON Schema 都只要求这些字段
/// <c>&gt;= 1</c>，**没有上界**；而构造 runtime 时 `maxConcurrentOperations` 会被
/// <c>checked(x * 2)</c> 放大为准入预留项上限。因此一个合法（通过校验）的极大值
/// 会在构造期抛裸 <c>OverflowException</c>，而不是可诊断的参数错误。
/// </para>
/// </summary>
public sealed class ExecutionQuotaBoundaryTests
{
    private const int HugeValue = int.MaxValue / 2 + 1;

    private static RoslynPrototypeExecutionOptions CreateOptions(int maxConcurrentOperations)
    {
        return new RoslynPrototypeExecutionOptions(
          DirectoryMaxDegreeOfParallelism: 2,
          CpgMaxDegreeOfParallelism: 2,
          GroupMaxDegreeOfParallelism: 2,
          HelperMaxDegreeOfParallelism: 2,
          ReplayMaxDegreeOfParallelism: 2,
          MaxConcurrentOperations: maxConcurrentOperations);
    }

    [Fact]
    public void AnalysisRuntime_WhenMaxConcurrentOperationsOverflowsTheAdmissionReservation_FailsDiagnosably()
    {
        // `checked(maximumParallelism * 2)` 在这里溢出。期望是可诊断的
        // ArgumentOutOfRangeException（与其它配额字段一致），而不是裸 OverflowException，
        // 因为该值通过了 YAML 的 `>= 1` 校验，用户看到的是「合法配置 + 随机溢出」。
        var options = CreateOptions(HugeValue);

        var exception = Record.Exception(
          () => new AnalysisRuntime(options, new AnalysisEpoch(0, 0, 0)));

        Assert.NotNull(exception);
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    [Fact]
    public void AnalysisRuntime_WhenMaxConcurrentOperationsIsAtTheSafeMaximum_StillConstructs()
    {
        // 边界内必须仍然可用：证明上面的拒绝不是「把大值一律拒掉」。
        //
        // ⚠️ 轮次 7 修正：本用例原用 int.MaxValue / 2，那只满足「*2 不溢出」这一条约束。
        // 轮次 7 给内核补上了 worker 数上界（WorkSchedulerOptions.MaximumWorkerCount），
        // 且 AnalysisRuntime 会在构造时立即建内核，故 int.MaxValue/2 已不再是可构造值——
        // 它现在被**更紧的**那条约束拒绝。这里改用同时满足两条约束的值。
        var runtime = new AnalysisRuntime(
          CreateOptions(512),
          new AnalysisEpoch(0, 0, 0));

        Assert.NotNull(runtime.ConcurrencyAdmissionController);
    }

    [Fact]
    public void AnalysisRuntime_WhenMaxConcurrentOperationsExceedsTheWorkerCeiling_FailsDiagnosably()
    {
        // 轮次 7 新增：MaxConcurrentOperations 同时是六个额度之一，
        // 因此它也必须受 worker 数上界约束（轮次 6 的 *2 守卫只看溢出，不看 worker 数量）。
        var options = CreateOptions(WorkSchedulerOptions.MaximumWorkerCount + 1);

        var exception = Record.Exception(
          () => new AnalysisRuntime(options, new AnalysisEpoch(0, 0, 0)));

        Assert.NotNull(exception);
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }
}
