using CoreAnalysis = TerrariaTools.Dome.Core.Analysis;

namespace TerrariaTools.Dome.Application.Ports;

/// <summary>
/// 描述 Dome 应用运行请求。
/// </summary>
/// <param name="InputPath">输入路径。</param>
/// <param name="OutputPath">输出路径。</param>
/// <param name="RuleSet">启用的规则集。</param>
/// <param name="Mode">运行模式。</param>
/// <param name="WorkspaceLoadOptions">工作区加载选项。</param>
public sealed record RunRequest(
    string InputPath,
    string OutputPath,
    IReadOnlyList<string> RuleSet,
    RunMode Mode,
    WorkspaceLoadOptions WorkspaceLoadOptions)
{
    /// <summary>
    /// 使用默认工作区加载选项创建运行请求。
    /// </summary>
    /// <param name="inputPath">输入路径。</param>
    /// <param name="outputPath">输出路径。</param>
    /// <param name="ruleSet">启用的规则集。</param>
    /// <param name="mode">运行模式。</param>
    public RunRequest(string inputPath, string outputPath, IReadOnlyList<string> ruleSet, RunMode mode)
        : this(inputPath, outputPath, ruleSet, mode, WorkspaceLoadOptions.Default)
    {
    }
}

/// <summary>
/// 描述 Terraria 运行时流水线的执行请求。
/// </summary>
/// <param name="SolutionPath">待处理的解决方案路径。</param>
/// <param name="OutputRootPath">输出根目录。</param>
public sealed record TerrariaRuntimeRunRequest(string SolutionPath, string OutputRootPath);

/// <summary>
/// 描述 Terraria 影子提取流水线的执行请求。
/// </summary>
/// <param name="SolutionPath">待处理的解决方案路径。</param>
/// <param name="OutputRootPath">输出根目录。</param>
/// <param name="SeedMemberName">作为提取起点的种子成员名。</param>
public sealed record TerrariaRuntimeShadowExtractionRequest(string SolutionPath, string OutputRootPath, string SeedMemberName);

/// <summary>
/// 描述标准运行时工作区的目录布局。
/// </summary>
/// <param name="SolutionPath">原始解决方案路径。</param>
/// <param name="SourceRootPath">源码根目录。</param>
/// <param name="OutputRootPath">输出根目录。</param>
/// <param name="DependencyEnvironmentPath">依赖环境目录。</param>
/// <param name="WorkspacePath">运行时工作区目录。</param>
/// <param name="ArtifactsPath">产物目录。</param>
/// <param name="WorkspaceSolutionPath">工作区中的解决方案路径。</param>
public sealed record TerrariaRuntimeLayout(
    string SolutionPath,
    string SourceRootPath,
    string OutputRootPath,
    string DependencyEnvironmentPath,
    string WorkspacePath,
    string ArtifactsPath,
    string WorkspaceSolutionPath);

/// <summary>
/// 描述影子提取工作区的目录布局。
/// </summary>
/// <param name="SolutionPath">原始解决方案路径。</param>
/// <param name="SourceRootPath">源码根目录。</param>
/// <param name="OutputRootPath">输出根目录。</param>
/// <param name="WorkspacePath">影子工作区目录。</param>
/// <param name="ArtifactsPath">产物目录。</param>
/// <param name="DependencyEnvironmentPath">依赖环境目录。</param>
/// <param name="WorkspaceSolutionPath">工作区中的解决方案路径。</param>
public sealed record TerrariaRuntimeShadowLayout(
    string SolutionPath,
    string SourceRootPath,
    string OutputRootPath,
    string WorkspacePath,
    string ArtifactsPath,
    string DependencyEnvironmentPath,
    string WorkspaceSolutionPath);

/// <summary>
/// 封装外部运行时进程的执行结果。
/// </summary>
/// <param name="ExitCode">进程退出码。</param>
/// <param name="StandardOutput">标准输出内容。</param>
/// <param name="StandardError">标准错误内容。</param>
public sealed record TerrariaRuntimeProcessResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// 汇总影子源码重写后的成员处理结果。
/// </summary>
/// <param name="PreservedMembers">保留原实现的成员数量。</param>
/// <param name="DefaultedMembers">改写为默认实现的成员数量。</param>
/// <param name="EmptiedMembers">清空实现体的成员数量。</param>
/// <param name="SamplePreservedMembers">示例保留成员。</param>
/// <param name="SampleDefaultedMembers">示例默认化成员。</param>
/// <param name="SampleEmptiedMembers">示例清空成员。</param>
public sealed record TerrariaRuntimeShadowRewriteSummary(
    int PreservedMembers,
    int DefaultedMembers,
    int EmptiedMembers,
    IReadOnlyList<string> SamplePreservedMembers,
    IReadOnlyList<string> SampleDefaultedMembers,
    IReadOnlyList<string> SampleEmptiedMembers);

/// <summary>
/// 描述运行时工作区构建结果。
/// </summary>
/// <param name="BuildSucceeded">指示构建是否成功。</param>
/// <param name="BuildExitCode">构建命令退出码。</param>
/// <param name="BuildCommand">执行的构建命令。</param>
/// <param name="RuntimeWorkspacePath">运行时工作区路径。</param>
/// <param name="DependencyEnvironmentPath">依赖环境路径。</param>
/// <param name="SolutionPath">参与构建的解决方案路径。</param>
/// <param name="StandardOutput">标准输出内容。</param>
/// <param name="StandardError">标准错误内容。</param>
public sealed record TerrariaRuntimeBuildSummary(
    bool BuildSucceeded,
    int BuildExitCode,
    string BuildCommand,
    string RuntimeWorkspacePath,
    string DependencyEnvironmentPath,
    string SolutionPath,
    string StandardOutput,
    string StandardError);

/// <summary>
/// 汇总影子提取流水线的最终报告。
/// </summary>
/// <param name="SeedMemberName">输入的种子成员名。</param>
/// <param name="SeedMemberId">解析出的种子成员标识。</param>
/// <param name="IncludedDocuments">纳入影子工作区的文档路径。</param>
/// <param name="ReachableMethods">从种子可达的方法标识。</param>
/// <param name="AdvancedAnalysisSummary">高级分析摘要。</param>
/// <param name="RewrittenDocuments">重写文档数量。</param>
/// <param name="RewriteSummary">成员重写摘要。</param>
public sealed record TerrariaRuntimeShadowExtractionReport(
    string SeedMemberName,
    string SeedMemberId,
    IReadOnlyList<string> IncludedDocuments,
    IReadOnlyList<string> ReachableMethods,
    CoreAnalysis.AdvancedAnalysisSummary AdvancedAnalysisSummary,
    int RewrittenDocuments,
    TerrariaRuntimeShadowRewriteSummary RewriteSummary)
{
    /// <summary>
    /// 获取影子工作区的构建摘要。
    /// </summary>
    public TerrariaRuntimeBuildSummary? TrBuildSummary { get; init; }
}
