using NLCPG.ProjectJson;

namespace RoslynPrototype.Tests;

/// <summary>
/// 项目导出的调度契约：文档之间**串行**，并行度落在单文档内部的 builder DOP。
/// 这些断言锁定的是"projectWorkerCount 经 EffectiveMaxDegreeOfParallelism
/// 作用于单文档 builder"的设计，防止再次被误读成跨文档并行度。
/// </summary>
public sealed class CpgProjectExportScheduleTests
{
    [Fact]
    public void ExportOptions_ProjectWorkerCountDrivesLocalBuilderDop()
    {
        var options = new ProjectExportOptions("sample.csproj", MaxDegreeOfParallelism: 8);

        // worker 数是单文档 builder 的 DOP；LocalBuilderDegreeOfParallelism 保留为
        // 显式表达"单文档串行"这一档位的只读入口，当前不接入导出路径。
        Assert.Equal(8, options.ProjectWorkerCount);
        Assert.Equal(8, options.EffectiveMaxDegreeOfParallelism);
        Assert.Equal(1, options.LocalBuilderDegreeOfParallelism);
    }

    [Fact]
    public void ExportOptions_DefaultMemoryBudgetIsAutomatic()
    {
        // 0 表示"导出时按物理内存决定"，因此不再被当作非法值拒绝。
        Assert.Equal(0, new ProjectExportOptions("sample.csproj").ProjectMemoryBudgetBytes);
    }

    [Fact]
    public void ExportOptions_RejectNegativeMemoryBudget()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
          () => new ProjectExportOptions("sample.csproj", ProjectMemoryBudgetBytes: -1));
    }

    // 说明：文档间串行的**确定性**不在这里用夹具验证，
    // 因为本环境的夹具项目无法解析元数据引用（NLISSNWS012，与本次改动无关）。
    // 该性质由真实导出的多次运行 payload 比对来证明，见 Build/worker8-utilization/。
}
