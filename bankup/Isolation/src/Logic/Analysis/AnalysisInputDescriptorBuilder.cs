using Domain.Analysis;
using Domain.Workspaces;

namespace Logic.Analysis;

/// <summary>
/// 分析输入描述构造器。
/// </summary>
public sealed class AnalysisInputDescriptorBuilder : IAnalysisInputDescriptorBuilder
{

    public AnalysisInputDescriptor Build(
        WorkspaceContext workspaceContext,
        string? requestedSourcePath,
        AnalysisSourceKind sourceKind)
    {
        ArgumentNullException.ThrowIfNull(workspaceContext);

        return AnalysisInputDescriptor.Create(
            workspaceContext.Id,
            ResolveSourcePath(workspaceContext, requestedSourcePath),
            sourceKind);
    }

    private static string ResolveSourcePath(WorkspaceContext workspaceContext, string? requestedSourcePath)
    {
        return workspaceContext.ResolveAnalysisSourcePath(requestedSourcePath);
    }
}
