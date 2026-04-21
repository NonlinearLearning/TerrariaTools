namespace Logic.Analysis.Engine.Passes.DataFlow;

/// <summary>
/// 表示一个通用数据流问题。
///
/// 对应 Joern `DataFlowProblem.scala`。它不关心节点来自哪种图，只要求调用方提供：
/// 控制流图、传递函数、合并函数、初始 IN/OUT 和空值。
/// </summary>
public sealed class DataFlowProblem<TNode, TValue>
    where TNode : notnull
{
    private DataFlowProblem(
        IFlowGraph<TNode> flowGraph,
        ITransferFunction<TNode, TValue> transferFunction,
        Func<TValue, TValue, TValue> meet,
        IInOutInit<TNode, TValue> inOutInit,
        bool forward,
        TValue empty)
    {
        FlowGraph = flowGraph ?? throw new ArgumentNullException(nameof(flowGraph));
        TransferFunction = transferFunction ?? throw new ArgumentNullException(nameof(transferFunction));
        Meet = meet ?? throw new ArgumentNullException(nameof(meet));
        InOutInit = inOutInit ?? throw new ArgumentNullException(nameof(inOutInit));
        IsForward = forward;
        Empty = empty;
    }

    /// <summary>
    /// 获取参与求解的流图。
    /// </summary>
    public IFlowGraph<TNode> FlowGraph { get; }

    /// <summary>
    /// 获取每个节点上的传递函数。
    /// </summary>
    public ITransferFunction<TNode, TValue> TransferFunction { get; }

    /// <summary>
    /// 获取前驱或后继结果的合并函数。
    /// </summary>
    public Func<TValue, TValue, TValue> Meet { get; }

    /// <summary>
    /// 获取 IN/OUT 的初始值。
    /// </summary>
    public IInOutInit<TNode, TValue> InOutInit { get; }

    /// <summary>
    /// 获取问题是否为前向数据流问题。
    /// </summary>
    public bool IsForward { get; }

    /// <summary>
    /// 获取没有前驱或后继时使用的空值。
    /// </summary>
    public TValue Empty { get; }

    /// <summary>
    /// 创建一个前向数据流问题。
    /// </summary>
    public static DataFlowProblem<TNode, TValue> Forward(
        IFlowGraph<TNode> flowGraph,
        ITransferFunction<TNode, TValue> transferFunction,
        Func<TValue, TValue, TValue> meet,
        IInOutInit<TNode, TValue> inOutInit,
        TValue empty)
    {
        return new DataFlowProblem<TNode, TValue>(
            flowGraph,
            transferFunction,
            meet,
            inOutInit,
            forward: true,
            empty);
    }

    /// <summary>
    /// 创建一个后向数据流问题。
    /// </summary>
    public static DataFlowProblem<TNode, TValue> Backward(
        IFlowGraph<TNode> flowGraph,
        ITransferFunction<TNode, TValue> transferFunction,
        Func<TValue, TValue, TValue> meet,
        IInOutInit<TNode, TValue> inOutInit,
        TValue empty)
    {
        return new DataFlowProblem<TNode, TValue>(
            flowGraph,
            transferFunction,
            meet,
            inOutInit,
            forward: false,
            empty);
    }
}

/// <summary>
/// 给数据流求解器提供统一的前驱、后继和遍历顺序。
/// </summary>
public interface IFlowGraph<TNode>
    where TNode : notnull
{
    /// <summary>
    /// 获取反向后序，用于前向问题的工作队列初始顺序。
    /// </summary>
    IReadOnlyList<TNode> AllNodesReversePostOrder { get; }

    /// <summary>
    /// 获取后序，用于后向问题的工作队列初始顺序。
    /// </summary>
    IReadOnlyList<TNode> AllNodesPostOrder { get; }

    /// <summary>
    /// 获取节点后继。
    /// </summary>
    IEnumerable<TNode> Successors(TNode node);

    /// <summary>
    /// 获取节点前驱。
    /// </summary>
    IEnumerable<TNode> Predecessors(TNode node);
}

/// <summary>
/// 表示节点上的数据流传递函数。
/// </summary>
public interface ITransferFunction<TNode, TValue>
{
    /// <summary>
    /// 将输入事实转换成节点之后的输出事实。
    /// </summary>
    TValue Apply(TNode node, TValue value);
}

/// <summary>
/// 表示求解开始前的 IN/OUT 初始表。
/// </summary>
public interface IInOutInit<TNode, TValue>
    where TNode : notnull
{
    /// <summary>
    /// 获取每个节点的 IN 初始值。
    /// </summary>
    IReadOnlyDictionary<TNode, TValue> InitIn { get; }

    /// <summary>
    /// 获取每个节点的 OUT 初始值。
    /// </summary>
    IReadOnlyDictionary<TNode, TValue> InitOut { get; }
}

/// <summary>
/// 保存一次数据流问题的 IN/OUT 求解结果。
/// </summary>
public sealed record DataFlowSolution<TNode, TValue>(
    IReadOnlyDictionary<TNode, TValue> In,
    IReadOnlyDictionary<TNode, TValue> Out,
    DataFlowProblem<TNode, TValue> Problem)
    where TNode : notnull;
