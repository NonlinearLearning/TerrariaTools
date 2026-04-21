namespace Logic.Analysis.Engine.Passes.ControlFlow.Dominance;

/// <summary>
/// 定义 CFG 适配器。
///
/// 这里直接对应 Joern `cfgdominator/CfgAdapter.scala`，
/// 让支配算法只依赖“前驱/后继”接口，不直接绑定图实现。
/// </summary>
/// <typeparam name="TNode">CFG 节点类型。</typeparam>
public interface ICfgAdapter<TNode>
{
    /// <summary>
    /// 获取后继节点。
    /// </summary>
    IEnumerable<TNode> GetSuccessors(TNode node);

    /// <summary>
    /// 获取前驱节点。
    /// </summary>
    IEnumerable<TNode> GetPredecessors(TNode node);
}
