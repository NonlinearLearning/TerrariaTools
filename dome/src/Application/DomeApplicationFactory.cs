namespace TerrariaTools.Dome.Application;

using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Reporting;
using TerrariaTools.Dome.Rewrite.Roslyn;
using TerrariaTools.Dome.Rules;

/// <summary>
/// DomeApplication 工厂类，负责创建 DomeApplication 实例。
/// </summary>
public static class DomeApplicationFactory
{
    /// <summary>
    /// 创建默认的 DomeApplication 实例。
    /// </summary>
    /// <returns>配置了默认组件的 DomeApplication 实例。</returns>
    public static DomeApplication CreateDefault()
    {
        return new DomeApplication(
            new WorkspaceLoadCoordinator(
                new CodeAnalysisWorkspaceLoader(),
                new SourceWorkspaceLoader()),
            new RoslynAnalysisEngine(),
            new FunctionImpactAnalyzer(),
            new ReferenceZeroPredictionAnalyzer(),
            new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()),
            new RoslynRewriteExecutor(),
            new JsonArtifactWriter());
    }
}
