using Analysis.Core;

namespace Analysis.Semantic;

/// <summary>
/// 提供节点级语义辅助方法。
///
/// Joern 的 `NodeExtension.scala` 是一个节点扩展标记 trait。
/// C# 版没有 Scala trait 机制，因此这里落成节点扩展方法集合。
/// </summary>
public static class NodeExtension
{
    /// <summary>
    /// 读取节点的字符串属性，缺失时返回空字符串。
    /// </summary>
    /// <param name="node">目标节点。</param>
    /// <param name="propertyName">属性名。</param>
    /// <returns>属性值或空字符串。</returns>
    public static string PropertyAsString(this CpgNode node, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        return node.TryGetProperty<string>(propertyName, out string? value)
            ? value ?? string.Empty
            : string.Empty;
    }

    /// <summary>
    /// 按强类型读取节点属性，缺失或类型不匹配时抛错。
    /// </summary>
    public static T Property<T>(this CpgNode node, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        if (node.TryGetProperty<T>(propertyName, out T? value) && value is not null)
        {
            return value;
        }

        throw new InvalidOperationException($"节点 '{node.Id}' 缺少属性 '{propertyName}'。");
    }

    /// <summary>
    /// 判断节点是否拥有指定属性值。
    /// </summary>
    /// <param name="node">目标节点。</param>
    /// <param name="propertyName">属性名。</param>
    /// <param name="expectedValue">期望值。</param>
    /// <returns>属性值匹配时返回真。</returns>
    public static bool HasPropertyValue(this CpgNode node, string propertyName, string expectedValue)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        return node.TryGetProperty<string>(propertyName, out string? actualValue) &&
               string.Equals(actualValue, expectedValue, StringComparison.Ordinal);
    }
}
