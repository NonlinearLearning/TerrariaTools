using Domain.Analysis.Engine.Core;
using Domain.Analysis.Engine.Semantic;

namespace Domain.Analysis.Engine.Slicing;

/// <summary>
/// 表示可序列化的切片节点。
///
/// 这个模型对应 Joern `SliceNode` 的核心字段，
/// 让切片结果不必直接暴露完整 CPG 节点对象。
/// </summary>
public sealed class SliceNode
{
    /// <summary>
    /// 从 CPG 节点创建切片节点。
    /// </summary>
    /// <param name="node">CPG 节点。</param>
    public SliceNode(CpgNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        Id = node.Id;
        Kind = node.Kind.ToString();
        Name = node.PropertyAsString("Name");
        Code = node.PropertyAsString("Code");
        TypeFullName = node.PropertyAsString("TypeFullName");
        ParentMethod = node.PropertyAsString("MethodFullName");
        FileName = node.PropertyAsString("FileName");
        Line = node.TryGetProperty<int>("Line", out int line) ? line : null;
        Column = node.TryGetProperty<int>("Column", out int column) ? column : null;
    }

    /// <summary>
    /// 获取节点编号。
    /// </summary>
    public long Id { get; }

    /// <summary>
    /// 获取节点类型。
    /// </summary>
    public string Kind { get; }

    /// <summary>
    /// 获取节点名称。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 获取源码文本。
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// 获取类型完整名。
    /// </summary>
    public string TypeFullName { get; }

    /// <summary>
    /// 获取所在方法完整名。
    /// </summary>
    public string ParentMethod { get; }

    /// <summary>
    /// 获取所在文件。
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// 获取行号。
    /// </summary>
    public int? Line { get; }

    /// <summary>
    /// 获取列号。
    /// </summary>
    public int? Column { get; }
}
