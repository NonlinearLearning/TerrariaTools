using Analysis.Core;
using Analysis.Semantic.Flows;

namespace Analysis.Passes.DataFlow;

/// <summary>
/// 根据外部方法语义规则补齐调用点上的数据流边。
///
/// 这一步对应 Joern 数据流里的 method semantics：当方法体不可见，或者不想展开
/// 方法体时，用规则表达“第几个实参会流到返回值/接收者/其他实参”。
/// </summary>
public sealed class BuildSemanticDataFlowPass : CpgPass
{
    private readonly ISemantics semantics;

    /// <summary>
    /// 使用语义规则初始化 pass。
    /// </summary>
    /// <param name="semantics">外部方法语义规则。</param>
    public BuildSemanticDataFlowPass(ISemantics semantics)
    {
        this.semantics = semantics ?? throw new ArgumentNullException(nameof(semantics));
    }

    /// <inheritdoc />
    protected override void Execute(CpgGraphBuilder builder)
    {
        foreach (CpgNode callNode in builder.Graph.GetNodes(CpgNodeKind.Call))
        {
            IReadOnlyList<MethodFlowRule> rules = RulesForCall(callNode);
            foreach (MethodFlowRule rule in rules)
            {
                IReadOnlyList<CpgNode> sources = ResolveEndpoint(builder.Graph, callNode, rule.Source);
                IReadOnlyList<CpgNode> targets = ResolveEndpoint(builder.Graph, callNode, rule.Target);
                foreach (CpgNode source in sources)
                {
                    foreach (CpgNode target in targets)
                    {
                        EnsureParameterLink(builder, source, target, BuildLabel(rule));
                    }
                }
            }
        }
    }

    private IReadOnlyList<MethodFlowRule> RulesForCall(CpgNode callNode)
    {
        string methodFullName = GetStringProperty(callNode, "MethodFullName");
        if (string.IsNullOrWhiteSpace(methodFullName))
        {
            return Array.Empty<MethodFlowRule>();
        }

        CpgNode methodLikeNode = new(callNode.Id, CpgNodeKind.Method);
        methodLikeNode.SetProperty("FullName", methodFullName);
        return semantics.ForMethod(methodLikeNode);
    }

    private static IReadOnlyList<CpgNode> ResolveEndpoint(
        CpgGraph graph,
        CpgNode callNode,
        FlowEndpoint endpoint)
    {
        return endpoint.Kind switch
        {
            FlowEndpointKind.Return => new[] { callNode },
            FlowEndpointKind.Receiver => graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Receiver)
                .Select(edge => graph.GetNode(edge.TargetId))
                .ToArray(),
            FlowEndpointKind.Argument => ResolveArguments(graph, callNode, endpoint.ArgumentIndex!.Value),
            _ => Array.Empty<CpgNode>(),
        };
    }

    private static IReadOnlyList<CpgNode> ResolveArguments(CpgGraph graph, CpgNode callNode, int argumentIndex)
    {
        return graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Argument)
            .Concat(graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Ast))
            .Select(edge => graph.GetNode(edge.TargetId))
            .Where(node => node.TryGetProperty<int>("ArgumentIndex", out int actualIndex) &&
                           actualIndex == argumentIndex)
            .DistinctBy(node => node.Id)
            .ToArray();
    }

    private static void EnsureParameterLink(
        CpgGraphBuilder builder,
        CpgNode source,
        CpgNode target,
        string label)
    {
        bool exists = builder.Graph.GetOutgoingEdges(source.Id, CpgEdgeKind.ParameterLink)
            .Any(edge => edge.TargetId == target.Id &&
                         string.Equals(edge.Label, label, StringComparison.Ordinal));
        if (!exists)
        {
            builder.AddEdge(source.Id, target.Id, CpgEdgeKind.ParameterLink, label);
        }
    }

    private static string BuildLabel(MethodFlowRule rule)
    {
        return $"{EndpointName(rule.Source)}->{EndpointName(rule.Target)}";
    }

    private static string EndpointName(FlowEndpoint endpoint)
    {
        return endpoint.Kind == FlowEndpointKind.Argument
            ? $"ARG[{endpoint.ArgumentIndex}]"
            : endpoint.Kind.ToString().ToUpperInvariant();
    }

    private static string GetStringProperty(CpgNode node, string propertyName)
    {
        return node.TryGetProperty<string>(propertyName, out string? value) ? value ?? string.Empty : string.Empty;
    }
}
