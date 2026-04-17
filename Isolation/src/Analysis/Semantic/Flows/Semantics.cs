using Analysis.Core;

namespace Analysis.Semantic.Flows;

/// <summary>
/// 定义外部方法语义查询接口。
///
/// 这个接口对应 Joern `Semantics.scala` 的核心职责：
/// 给定一个方法节点，返回该方法的数据流传播语义。
/// </summary>
public interface ISemantics
{
    /// <summary>
    /// 查询指定方法节点的流语义。
    /// </summary>
    /// <param name="methodNode">方法节点。</param>
    /// <returns>命中的语义规则；没有命中时返回空集合。</returns>
    IReadOnlyList<MethodFlowRule> ForMethod(CpgNode methodNode);
}

/// <summary>
/// 提供空语义实现。
/// </summary>
public sealed class NoSemantics : ISemantics
{
    /// <inheritdoc />
    public IReadOnlyList<MethodFlowRule> ForMethod(CpgNode methodNode)
    {
        ArgumentNullException.ThrowIfNull(methodNode);
        return Array.Empty<MethodFlowRule>();
    }
}

/// <summary>
/// 提供语义组合能力。
/// </summary>
public sealed class CompositeSemantics : ISemantics
{
    private readonly ISemantics primary;
    private readonly ISemantics fallback;

    /// <summary>
    /// 使用主语义和回退语义初始化组合对象。
    /// </summary>
    /// <param name="primary">优先查询的语义。</param>
    /// <param name="fallback">主语义未命中时使用的语义。</param>
    public CompositeSemantics(ISemantics primary, ISemantics fallback)
    {
        this.primary = primary ?? throw new ArgumentNullException(nameof(primary));
        this.fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    /// <inheritdoc />
    public IReadOnlyList<MethodFlowRule> ForMethod(CpgNode methodNode)
    {
        IReadOnlyList<MethodFlowRule> rules = primary.ForMethod(methodNode);
        return rules.Count > 0 ? rules : fallback.ForMethod(methodNode);
    }
}
