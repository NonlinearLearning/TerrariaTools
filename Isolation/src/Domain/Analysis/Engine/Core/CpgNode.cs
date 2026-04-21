using System.Collections.ObjectModel;

namespace Domain.Analysis.Engine.Core;

/// <summary>
/// 表示内存态 CPG 中的一个节点。
///
/// 当前实现刻意保持轻量：
/// - <see cref="Kind"/> 负责表达节点类别。
/// - <see cref="Properties"/> 负责承载具体 schema 属性。
///
/// 为什么暂时不做成 Joern 那种“每类节点一个专门类型”：
/// 1. Joern 依赖 schema 代码生成体系和专门图运行时。
/// 2. 当前仓库还处于最小核心实现阶段。
/// 3. 先统一成一个节点模型，更容易在阶段一、二快速收敛。
/// </summary>
public sealed class CpgNode
{
    private readonly Dictionary<string, object?> properties;

    /// <summary>
    /// 使用图内唯一标识和节点类型初始化一个节点。
    /// </summary>
    /// <param name="id">图内部使用的节点编号。</param>
    /// <param name="kind">节点对应的 schema 类型。</param>
    public CpgNode(long id, CpgNodeKind kind)
    {
        Id = id;
        Kind = kind;
        properties = new Dictionary<string, object?>(StringComparer.Ordinal);
    }

    /// <summary>
    /// 获取图内部节点编号。
    /// </summary>
    public long Id { get; }

    /// <summary>
    /// 获取节点的稳定 schema 类别。
    /// </summary>
    public CpgNodeKind Kind { get; }

    /// <summary>
    /// 获取节点属性的只读视图。
    ///
    /// 之所以不直接暴露可写字典，是为了后续如果要加 schema 校验、
    /// 属性标准化或变更钩子时，不需要改所有调用方。
    /// </summary>
    public IReadOnlyDictionary<string, object?> Properties =>
        new ReadOnlyDictionary<string, object?>(properties);

    /// <summary>
    /// 设置或替换一个属性值。
    ///
    /// 当前先使用字符串属性名，是为了贴近 Joern 的 property 模型。
    /// 等阶段一、二属性面稳定之后，再决定是否加强类型包装。
    /// </summary>
    /// <param name="name">schema 属性名。</param>
    /// <param name="value">要写入的属性值。</param>
    public void SetProperty(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        properties[name] = value;
    }

    /// <summary>
    /// 尝试按指定类型读取属性值。
    /// </summary>
    /// <typeparam name="T">期望的属性类型。</typeparam>
    /// <param name="name">schema 属性名。</param>
    /// <param name="value">如果读取成功，返回对应的强类型值。</param>
    /// <returns>当属性存在且类型匹配时返回真。</returns>
    public bool TryGetProperty<T>(string name, out T? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (properties.TryGetValue(name, out object? raw) && raw is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }
}
