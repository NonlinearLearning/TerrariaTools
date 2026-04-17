using Domain.Analysis;

namespace Application.Contracts.Analysis;

/// <summary>
/// 最小节点 DTO。
/// </summary>
public sealed class MinimumNodeDto
{
    /// <summary>
    /// 获取或设置节点标识。
    /// </summary>
    public string NodeId { get; init; } = string.Empty;

    /// <summary>
    /// 获取或设置展示名称。
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// 获取或设置节点类型。
    /// </summary>
    public CpgType NodeType { get; init; }

    /// <summary>
    /// 获取或设置文档路径。
    /// </summary>
    public string DocumentPath { get; init; } = string.Empty;

    /// <summary>
    /// 获取或设置开始行。
    /// </summary>
    public int StartLine { get; init; }

    /// <summary>
    /// 获取或设置开始列。
    /// </summary>
    public int StartColumn { get; init; }

    /// <summary>
    /// 获取或设置结束行。
    /// </summary>
    public int EndLine { get; init; }

    /// <summary>
    /// 获取或设置结束列。
    /// </summary>
    public int EndColumn { get; init; }
}
