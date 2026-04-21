namespace Logic.Analysis.Engine.Passes.DataFlow;

/// <summary>
/// 使用工作队列算法求解通用数据流问题。
///
/// 对应 Joern `DataFlowSolver.scala`。这里保留同样的两条主路径：
/// 前向求解从前驱合并 IN，再用传递函数产生 OUT；后向求解反过来处理。
/// </summary>
public sealed class DataFlowSolver
{
    /// <summary>
    /// 计算前向最大定点解。
    /// </summary>
    public DataFlowSolution<TNode, TValue> CalculateForward<TNode, TValue>(
        DataFlowProblem<TNode, TValue> problem)
        where TNode : notnull
    {
        ArgumentNullException.ThrowIfNull(problem);

        Dictionary<TNode, TValue> input = Copy(problem.InOutInit.InitIn);
        Dictionary<TNode, TValue> output = Copy(problem.InOutInit.InitOut);
        Queue<TNode> workQueue = new(problem.FlowGraph.AllNodesReversePostOrder);
        HashSet<TNode> queued = problem.FlowGraph.AllNodesReversePostOrder.ToHashSet();

        while (workQueue.Count > 0)
        {
            TNode node = workQueue.Dequeue();
            queued.Remove(node);

            TValue inValue = MeetValues(problem, problem.FlowGraph.Predecessors(node).Select(pred => output[pred]));
            input[node] = inValue;
            TValue oldValue = output[node];
            TValue newValue = problem.TransferFunction.Apply(node, inValue);
            output[node] = newValue;

            if (EqualityComparer<TValue>.Default.Equals(oldValue, newValue))
            {
                continue;
            }

            EnqueueMissing(workQueue, queued, problem.FlowGraph.Successors(node));
        }

        return new DataFlowSolution<TNode, TValue>(input, output, problem);
    }

    /// <summary>
    /// 计算后向最大定点解。
    /// </summary>
    public DataFlowSolution<TNode, TValue> CalculateBackward<TNode, TValue>(
        DataFlowProblem<TNode, TValue> problem)
        where TNode : notnull
    {
        ArgumentNullException.ThrowIfNull(problem);

        Dictionary<TNode, TValue> input = Copy(problem.InOutInit.InitIn);
        Dictionary<TNode, TValue> output = Copy(problem.InOutInit.InitOut);
        Queue<TNode> workQueue = new(problem.FlowGraph.AllNodesPostOrder);
        HashSet<TNode> queued = problem.FlowGraph.AllNodesPostOrder.ToHashSet();

        while (workQueue.Count > 0)
        {
            TNode node = workQueue.Dequeue();
            queued.Remove(node);

            TValue outValue = MeetValues(problem, problem.FlowGraph.Successors(node).Select(succ => input[succ]));
            output[node] = outValue;
            TValue oldValue = input[node];
            TValue newValue = problem.TransferFunction.Apply(node, outValue);
            input[node] = newValue;

            if (EqualityComparer<TValue>.Default.Equals(oldValue, newValue))
            {
                continue;
            }

            EnqueueMissing(workQueue, queued, problem.FlowGraph.Predecessors(node));
        }

        return new DataFlowSolution<TNode, TValue>(input, output, problem);
    }

    private static Dictionary<TNode, TValue> Copy<TNode, TValue>(IReadOnlyDictionary<TNode, TValue> values)
        where TNode : notnull
    {
        return values.ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static TValue MeetValues<TNode, TValue>(
        DataFlowProblem<TNode, TValue> problem,
        IEnumerable<TValue> values)
        where TNode : notnull
    {
        using IEnumerator<TValue> enumerator = values.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return problem.Empty;
        }

        TValue result = enumerator.Current;
        while (enumerator.MoveNext())
        {
            result = problem.Meet(result, enumerator.Current);
        }

        return result;
    }

    private static void EnqueueMissing<TNode>(
        Queue<TNode> workQueue,
        HashSet<TNode> queued,
        IEnumerable<TNode> nodes)
        where TNode : notnull
    {
        foreach (TNode node in nodes)
        {
            if (queued.Add(node))
            {
                workQueue.Enqueue(node);
            }
        }
    }
}
