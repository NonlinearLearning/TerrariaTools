namespace Analysis.Passes.ControlFlow.Dominance;

/// <summary>
/// 计算 CFG 直接支配关系。
///
/// 这里直接按 Joern 使用的 Cooper 算法改写为 C#，
/// 保持“从入口做后序编号，再迭代求交”这条主路径。
/// </summary>
/// <typeparam name="TNode">CFG 节点类型。</typeparam>
public sealed class CfgDominator<TNode> where TNode : notnull
{
    private readonly ICfgAdapter<TNode> adapter;

    /// <summary>
    /// 初始化支配器。
    /// </summary>
    public CfgDominator(ICfgAdapter<TNode> adapter)
    {
        this.adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    /// <summary>
    /// 计算从入口可达节点的直接支配节点。
    /// </summary>
    public IReadOnlyDictionary<TNode, TNode> Calculate(TNode entryNode)
    {
        ArgumentNullException.ThrowIfNull(entryNode);

        const int Undefined = -1;
        Dictionary<TNode, int> postOrderNumbers = PostOrderNumbering(entryNode);
        List<TNode> reversePostOrder = postOrderNumbers
            .OrderByDescending(pair => pair.Value)
            .Select(pair => pair.Key)
            .Where(node => !EqualityComparer<TNode>.Default.Equals(node, entryNode))
            .ToList();

        int[] dominators = Enumerable.Repeat(Undefined, postOrderNumbers.Count).ToArray();
        dominators[postOrderNumbers[entryNode]] = postOrderNumbers[entryNode];

        int SafeDominators(int index) => index == Undefined ? Undefined : dominators[index];

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (TNode node in reversePostOrder)
            {
                TNode firstDefinedPredecessor = adapter
                    .GetPredecessors(node)
                    .First(predecessor =>
                        postOrderNumbers.TryGetValue(predecessor, out int predecessorIndex) &&
                        SafeDominators(predecessorIndex) != Undefined);

                int newImmediateDominator = postOrderNumbers[firstDefinedPredecessor];
                foreach (TNode predecessor in adapter.GetPredecessors(node))
                {
                    if (!postOrderNumbers.TryGetValue(predecessor, out int predecessorIndex))
                    {
                        continue;
                    }

                    if (SafeDominators(predecessorIndex) != Undefined)
                    {
                        newImmediateDominator = Intersect(dominators, predecessorIndex, newImmediateDominator);
                    }
                }

                int nodeIndex = postOrderNumbers[node];
                if (dominators[nodeIndex] != newImmediateDominator)
                {
                    dominators[nodeIndex] = newImmediateDominator;
                    changed = true;
                }
            }
        }

        Dictionary<int, TNode> indexToNode = postOrderNumbers.ToDictionary(pair => pair.Value, pair => pair.Key);
        Dictionary<TNode, TNode> result = new();
        foreach ((TNode node, int index) in postOrderNumbers)
        {
            if (EqualityComparer<TNode>.Default.Equals(node, entryNode))
            {
                continue;
            }

            result[node] = indexToNode[dominators[index]];
        }

        return result;
    }

    private static int Intersect(int[] dominators, int immediateDomIndex1, int immediateDomIndex2)
    {
        int finger1 = immediateDomIndex1;
        int finger2 = immediateDomIndex2;

        while (finger1 != finger2)
        {
            while (finger1 < finger2)
            {
                finger1 = dominators[finger1];
            }

            while (finger2 < finger1)
            {
                finger2 = dominators[finger2];
            }
        }

        return finger1;
    }

    private Dictionary<TNode, int> PostOrderNumbering(TNode entryNode)
    {
        Dictionary<TNode, int> numbering = new();
        HashSet<TNode> visited = new();
        int nextIndex = 0;

        void Visit(TNode node)
        {
            if (!visited.Add(node))
            {
                return;
            }

            foreach (TNode successor in adapter.GetSuccessors(node))
            {
                Visit(successor);
            }

            numbering[node] = nextIndex++;
        }

        Visit(entryNode);
        return numbering;
    }
}
