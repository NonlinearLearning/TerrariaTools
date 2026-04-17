namespace Analysis.Passes.ControlFlow.Dominance;

/// <summary>
/// 定义支配树访问接口。
/// </summary>
/// <typeparam name="TNode">节点类型。</typeparam>
public interface IDomTreeAdapter<TNode>
{
    /// <summary>
    /// 获取一个节点的直接支配节点。
    /// </summary>
    TNode? GetImmediateDominator(TNode node);
}
