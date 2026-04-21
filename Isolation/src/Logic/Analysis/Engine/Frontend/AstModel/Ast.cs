using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Frontend.AstModel;

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

    public IReadOnlyList<CpgNode> Nodes { get; }

    public IReadOnlyList<AstEdge> Edges { get; }

    public CpgNode? Root => Nodes.FirstOrDefault();

    public CpgNode? RightMostLeaf => Nodes.LastOrDefault();

    public static Ast Empty() => new(Array.Empty<CpgNode>(), Array.Empty<AstEdge>());

    public static Ast FromRoot(CpgNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new Ast(new[] { node }, Array.Empty<AstEdge>());
    }

    public Ast WithChild(Ast other)
    {
        ArgumentNullException.ThrowIfNull(other);

        IEnumerable<AstEdge> bridge = Root is null || other.Root is null
            ? Array.Empty<AstEdge>()
            : new[] { new AstEdge(Root, other.Root, CpgEdgeKind.Ast) };
        return new Ast(Nodes.Concat(other.Nodes), Edges.Concat(other.Edges).Concat(bridge));
    }

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

    public Ast Merge(Ast other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new Ast(Nodes.Concat(other.Nodes), Edges.Concat(other.Edges));
    }

    public Ast WithEdge(CpgNode source, CpgNode target, CpgEdgeKind kind, string label = "")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        return new Ast(Nodes, Edges.Concat(new[] { new AstEdge(source, target, kind, label) }));
    }

    public Ast WithConditionEdge(CpgNode source, CpgNode target) => WithEdge(source, target, CpgEdgeKind.Condition);

    public Ast WithTrueBodyEdge(CpgNode source, CpgNode target) => WithEdge(source, target, CpgEdgeKind.TrueBody);

    public Ast WithFalseBodyEdge(CpgNode source, CpgNode target) => WithEdge(source, target, CpgEdgeKind.FalseBody);

    public Ast WithReceiverEdge(CpgNode source, CpgNode target) => WithEdge(source, target, CpgEdgeKind.Receiver);

    public Ast WithArgEdge(CpgNode source, CpgNode target, int? argumentIndex = null)
    {
        if (argumentIndex.HasValue)
        {
            target.SetProperty("ArgumentIndex", argumentIndex.Value);
        }

        return WithEdge(source, target, CpgEdgeKind.Argument);
    }

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

    public Ast WithCaptureEdge(CpgNode source, CpgNode target) => WithEdge(source, target, CpgEdgeKind.Capture);

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
