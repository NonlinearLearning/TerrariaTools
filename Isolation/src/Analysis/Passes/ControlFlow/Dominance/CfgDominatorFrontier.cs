namespace Analysis.Passes.ControlFlow.Dominance;

/// <summary>
/// 计算支配边界。
/// </summary>
/// <typeparam name="TNode">节点类型。</typeparam>
public sealed class CfgDominatorFrontier<TNode> where TNode : notnull
{
    private readonly ICfgAdapter<TNode> cfgAdapter;
    private readonly IDomTreeAdapter<TNode> domTreeAdapter;

    /// <summary>
    /// 初始化支配边界计算器。
    /// </summary>
    public CfgDominatorFrontier(ICfgAdapter<TNode> cfgAdapter, IDomTreeAdapter<TNode> domTreeAdapter)
    {
        this.cfgAdapter = cfgAdapter ?? throw new ArgumentNullException(nameof(cfgAdapter));
        this.domTreeAdapter = domTreeAdapter ?? throw new ArgumentNullException(nameof(domTreeAdapter));
    }

    /// <summary>
    /// 计算支配边界。
    /// </summary>
    public IReadOnlyDictionary<TNode, IReadOnlyCollection<TNode>> Calculate(IEnumerable<TNode> cfgNodes)
    {
        ArgumentNullException.ThrowIfNull(cfgNodes);

        Dictionary<TNode, HashSet<TNode>> frontier = new();
        foreach (TNode node in cfgNodes)
        {
            TNode[] predecessors = cfgAdapter.GetPredecessors(node).ToArray();
            if (predecessors.Length <= 1)
            {
                continue;
            }

            TNode? immediateDominator = domTreeAdapter.GetImmediateDominator(node);
            if (immediateDominator is null)
            {
                continue;
            }

            foreach (TNode predecessor in predecessors)
            {
                TNode? current = predecessor;
                while (current is not null && !EqualityComparer<TNode>.Default.Equals(current, immediateDominator))
                {
                    if (!frontier.TryGetValue(current, out HashSet<TNode>? values))
                    {
                        values = new HashSet<TNode>();
                        frontier[current] = values;
                    }

                    values.Add(node);
                    current = domTreeAdapter.GetImmediateDominator(current);
                }
            }
        }

        return frontier.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyCollection<TNode>)pair.Value.ToArray());
    }
}
