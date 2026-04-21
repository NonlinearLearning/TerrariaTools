using Domain.Workspaces;

namespace Domain.Analysis;

/// <summary>
/// 表示分析输入描述。
/// </summary>
public sealed class AnalysisInputDescriptor
{
    private AnalysisInputDescriptor(Guid workspaceContextId, WorkspacePath sourcePath, AnalysisSourceKind sourceKind)
    {
        WorkspaceContextId = workspaceContextId;
        SourcePathValue = sourcePath;
        SourceKind = sourceKind;
    }

    /// <summary>
    /// 获取工作区上下文标识。
    /// </summary>
    public Guid WorkspaceContextId { get; }

    /// <summary>
    /// 获取输入路径。
    /// </summary>
    public string SourcePath => SourcePathValue.Value;

    /// <summary>
    /// 获取输入路径值对象。
    /// </summary>
    public WorkspacePath SourcePathValue { get; }

    /// <summary>
    /// 获取输入来源类型。
    /// </summary>
    public AnalysisSourceKind SourceKind { get; }

    /// <summary>
    /// 创建分析输入描述。
    /// </summary>
    public static AnalysisInputDescriptor Create(
        Guid workspaceContextId,
        string sourcePath,
        AnalysisSourceKind sourceKind)
    {
        return Create(workspaceContextId, WorkspacePath.Create(sourcePath), sourceKind);
    }

    public static AnalysisInputDescriptor Create(
        Guid workspaceContextId,
        WorkspacePath sourcePath,
        AnalysisSourceKind sourceKind)
    {
        return new AnalysisInputDescriptor(workspaceContextId, sourcePath, sourceKind);
    }
}
