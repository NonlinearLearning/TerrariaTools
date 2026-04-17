using Analysis.Core;

namespace Analysis.Frontend.AstModel;

/// <summary>
/// 表示前端构建阶段的临时 AST。
///
/// 对应 Joern `Ast.scala`。本地版本直接使用已经创建的 `CpgNode`，
/// 但保留 Joern 的核心组合能力：合并子树、连接 AST 边、写入图时补 `Order`。
/// </summary>
public sealed class Ast
{
    private Ast(IEnumerable<CpgNode> nodes, IEnumerable<AstEdge> edges)
    {
        Nodes = nodes.DistinctBy(node => node.Id).ToArray();
        Edges = edges.ToArray();
    }

    /// <summary>
    /// 获取当前 AST 包含的节点。
    /// </summary>
    public IReadOnlyList<CpgNode> Nodes { get; }

    /// <summary>
    /// 获取当前 AST 包含的边。
    /// </summary>
    public IReadOnlyList<AstEdge> Edges { get; }

    /// <summary>
    /// 获取 AST 根节点。
    /// </summary>
    public CpgNode? Root => Nodes.FirstOrDefault();

    /// <summary>
    /// 获取最右叶子节点。
    /// </summary>
    public CpgNode? RightMostLeaf => Nodes.LastOrDefault();

    /// <summary>
    /// 创建空 AST。
    /// </summary>
    public static Ast Empty() => new(Array.Empty<CpgNode>(), Array.Empty<AstEdge>());

    /// <summary>
    /// 从根节点创建 AST。
    /// </summary>
    public static Ast FromRoot(CpgNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new Ast(new[] { node }, Array.Empty<AstEdge>());
    }

    /// <summary>
    /// 将另一棵 AST 作为当前根节点的子树。
    /// </summary>
    public Ast WithChild(Ast other)
    {
        ArgumentNullException.ThrowIfNull(other);

        IEnumerable<AstEdge> bridge = Root is null || other.Root is null
            ? Array.Empty<AstEdge>()
            : new[] { new AstEdge(Root, other.Root, CpgEdgeKind.Ast) };
        return new Ast(Nodes.Concat(other.Nodes), Edges.Concat(other.Edges).Concat(bridge));
    }

    /// <summary>
    /// 将多棵 AST 依次作为当前根节点的子树。
    /// </summary>
    public Ast WithChildren(IEnumerable<Ast> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        Ast current = this;
        foreach (Ast child in children)
        {
            current = current.WithChild(child);
        }

        return current;
    }

    /// <summary>
    /// 合并两棵 AST，不额外创建父子边。
    /// </summary>
    public Ast Merge(Ast other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new Ast(Nodes.Concat(other.Nodes), Edges.Concat(other.Edges));
    }

    /// <summary>
    /// 添加一条特殊语义边。
    /// </summary>
    public Ast WithEdge(CpgNode source, CpgNode target, CpgEdgeKind kind, string label = "")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        return new Ast(Nodes, Edges.Concat(new[] { new AstEdge(source, target, kind, label) }));
    }

    /// <summary>
    /// 添加条件边。
    /// </summary>
    public Ast WithConditionEdge(CpgNode source, CpgNode target) => WithEdge(source, target, CpgEdgeKind.Condition);

    /// <summary>
    /// 添加 true 分支边。
    /// </summary>
    public Ast WithTrueBodyEdge(CpgNode source, CpgNode target) => WithEdge(source, target, CpgEdgeKind.TrueBody);

    /// <summary>
    /// 添加 false 分支边。
    /// </summary>
    public Ast WithFalseBodyEdge(CpgNode source, CpgNode target) => WithEdge(source, target, CpgEdgeKind.FalseBody);

    /// <summary>
    /// 添加 receiver 边。
    /// </summary>
    public Ast WithReceiverEdge(CpgNode source, CpgNode target) => WithEdge(source, target, CpgEdgeKind.Receiver);

    /// <summary>
    /// 添加参数边。
    /// </summary>
    public Ast WithArgEdge(CpgNode source, CpgNode target, int? argumentIndex = null)
    {
        if (argumentIndex.HasValue)
        {
            target.SetProperty("ArgumentIndex", argumentIndex.Value);
        }

        return WithEdge(source, target, CpgEdgeKind.Argument);
    }

    /// <summary>
    /// 添加多条参数边，并从指定序号开始设置 `ArgumentIndex`。
    /// </summary>
    public Ast WithArgEdges(CpgNode source, IEnumerable<CpgNode> targets, int argumentIndexStart = 1)
    {
        ArgumentNullException.ThrowIfNull(targets);
        Ast current = this;
        int argumentIndex = argumentIndexStart;
        foreach (CpgNode target in targets)
        {
            current = current.WithArgEdge(source, target, argumentIndex);
            argumentIndex++;
        }

        return current;
    }

    /// <summary>
    /// 添加捕获边。
    /// </summary>
    public Ast WithCaptureEdge(CpgNode source, CpgNode target) => WithEdge(source, target, CpgEdgeKind.Capture);

    /// <summary>
    /// 将临时 AST 写入图中。
    /// </summary>
    public void StoreInGraph(CpgGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        SetOrderWhereNotSet();

        foreach (AstEdge edge in Edges)
        {
            bool exists = graph.GetOutgoingEdges(edge.Source.Id, edge.Kind)
                .Any(existing => existing.TargetId == edge.Target.Id &&
                                 string.Equals(existing.Label, edge.Label, StringComparison.Ordinal));
            if (!exists)
            {
                graph.AddEdge(edge.Source.Id, edge.Target.Id, edge.Kind, edge.Label);
            }
        }
    }

    private void SetOrderWhereNotSet()
    {
        if (Root is not null && !Root.Properties.ContainsKey("Order"))
        {
            Root.SetProperty("Order", 1);
        }

        foreach (IGrouping<long, AstEdge> siblingGroup in Edges.Where(edge => edge.Kind == CpgEdgeKind.Ast).GroupBy(edge => edge.Source.Id))
        {
            int order = 1;
            foreach (AstEdge edge in siblingGroup)
            {
                if (!edge.Target.Properties.ContainsKey("Order"))
                {
                    edge.Target.SetProperty("Order", order);
                }

                order++;
            }
        }
    }
}
