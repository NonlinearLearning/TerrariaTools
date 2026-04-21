using Domain.Analysis;
using Domain.Workspaces;

namespace Logic.Analysis;

/// <summary>
/// 定义分析输入描述构造能力。
/// </summary>
public interface IAnalysisInputDescriptorBuilder
{
    /// <summary>
    /// 构造分析输入描述。
    /// </summary>
    AnalysisInputDescriptor Build(
        WorkspaceContext workspaceContext,
        string? requestedSourcePath,
        AnalysisSourceKind sourceKind);
}
