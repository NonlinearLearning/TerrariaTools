using Analysis.Core;

namespace Analysis.Passes;

/// <summary>
/// 为 delegate 调用补齐到真实目标方法的关系。
///
/// 当前版本只覆盖阶段二最核心的情况：
/// - 本地变量以方法组或 lambda 初始化；
/// - 后续对该变量执行 `Invoke` 调用；
/// - 调用节点通过 `IDENTIFIER -> REF -> LOCAL` 找回声明；
/// - 再从声明的 AST 子树中找到 `METHOD_REF`；
/// - 最后把 `METHOD_REF -> METHOD` 的目标补成 `CALL` 边。
///
/// 这样做的目的，是把“方法引用”和“委托调用”这两段语义链闭合起来。
/// </summary>
public sealed class BuildDelegateCallGraphPass : CpgPass
{
    /// <inheritdoc />
    protected override void Execute(CpgGraphBuilder builder)
    {
        foreach (CpgNode callNode in builder.Graph.GetNodes(CpgNodeKind.Call).ToList())
        {
            if (!LooksLikeDelegateInvoke(callNode))
            {
                continue;
            }

            foreach (CpgNode operandNode in GetAstChildren(builder.Graph, callNode)
                         .Where(node => node.Kind == CpgNodeKind.Identifier || node.Kind == CpgNodeKind.Call)
                         .ToList())
            {
                foreach (CpgNode declarationNode in ResolveDeclarationNodesFromOperand(builder.Graph, operandNode))
                {
                    foreach (CpgNode methodRefNode in GetMethodReferencesForDeclaration(builder.Graph, declarationNode))
                    {
                        foreach (CpgNode methodNode in builder.Graph
                                     .GetOutgoingEdges(methodRefNode.Id, CpgEdgeKind.MethodRef)
                                     .Select(edge => builder.Graph.GetNode(edge.TargetId))
                                     .ToList())
                        {
                            EnsureCallEdge(builder, callNode, methodNode);
                        }
                    }
                }
            }
        }
    }

    private static IEnumerable<CpgNode> ResolveDeclarationNodesFromOperand(CpgGraph graph, CpgNode operandNode)
    {
        if (operandNode.Kind == CpgNodeKind.Identifier)
        {
            return graph.GetOutgoingEdges(operandNode.Id, CpgEdgeKind.Ref)
                .Select(edge => graph.GetNode(edge.TargetId))
                .ToList();
        }

        if (operandNode.Kind == CpgNodeKind.Call &&
            operandNode.TryGetProperty<string>("Name", out string? name) &&
            name is not null &&
            name.StartsWith("get_", StringComparison.Ordinal) &&
            operandNode.TryGetProperty<string>("MethodFullName", out string? methodFullName) &&
            !string.IsNullOrWhiteSpace(methodFullName))
        {
            string memberName = name["get_".Length..];
            int separatorIndex = methodFullName.LastIndexOf($".{name}(", StringComparison.Ordinal);
            if (separatorIndex < 0)
            {
                return Enumerable.Empty<CpgNode>();
            }

            string containingTypeFullName = methodFullName[..separatorIndex];
            string expectedMemberFullName = $"{containingTypeFullName}.{memberName}";
            return graph.GetNodes(CpgNodeKind.Member)
                .Where(node => node.TryGetProperty<string>("FullName", out string? fullName) &&
                               string.Equals(fullName, expectedMemberFullName, StringComparison.Ordinal))
                .ToList();
        }

        return Enumerable.Empty<CpgNode>();
    }

    private static IEnumerable<CpgNode> GetMethodReferencesForDeclaration(CpgGraph graph, CpgNode declarationNode)
    {
        List<CpgNode> directMethodRefs = GetAstDescendants(graph, declarationNode)
            .Where(node => node.Kind == CpgNodeKind.MethodRef)
            .ToList();

        if (directMethodRefs.Count > 0)
        {
            return directMethodRefs;
        }

        if (declarationNode.Kind == CpgNodeKind.Member)
        {
            List<CpgNode> propertyAssignmentRefs = GetPropertyAssignmentMethodRefs(graph, declarationNode).ToList();
            if (propertyAssignmentRefs.Count > 0)
            {
                return propertyAssignmentRefs;
            }
        }

        List<CpgNode> assignmentMethodRefs = new();
        foreach (CpgNode identifierNode in graph
                     .GetNodes(CpgNodeKind.Identifier)
                     .Where(identifierNode => graph.GetOutgoingEdges(identifierNode.Id, CpgEdgeKind.Ref)
                         .Any(edge => edge.TargetId == declarationNode.Id))
                     .ToList())
        {
            CpgNode? parentNode = GetAstParent(graph, identifierNode);
            if (parentNode is null || !LooksLikeAssignmentNode(parentNode))
            {
                continue;
            }

            assignmentMethodRefs.AddRange(
                GetAstDescendants(graph, parentNode)
                    .Where(node => node.Kind == CpgNodeKind.MethodRef));
        }

        return assignmentMethodRefs;
    }

    private static IEnumerable<CpgNode> GetPropertyAssignmentMethodRefs(CpgGraph graph, CpgNode declarationNode)
    {
        string memberFullName = GetStringProperty(declarationNode, "FullName");
        string memberName = GetStringProperty(declarationNode, "Name");
        if (string.IsNullOrWhiteSpace(memberFullName) || string.IsNullOrWhiteSpace(memberName))
        {
            return Enumerable.Empty<CpgNode>();
        }

        int separatorIndex = memberFullName.LastIndexOf($".{memberName}", StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return Enumerable.Empty<CpgNode>();
        }

        string containingTypeFullName = memberFullName[..separatorIndex];
        string setterPrefix = $"{containingTypeFullName}.set_{memberName}(";

        return graph.GetNodes(CpgNodeKind.Call)
            .Where(node => node.TryGetProperty<string>("MethodFullName", out string? methodFullName) &&
                           methodFullName?.StartsWith(setterPrefix, StringComparison.Ordinal) is true)
            .SelectMany(node => GetAstDescendants(graph, node))
            .Where(node => node.Kind == CpgNodeKind.MethodRef)
            .ToList();
    }

    private static bool LooksLikeDelegateInvoke(CpgNode callNode)
    {
        if (!callNode.TryGetProperty<string>("Name", out string? name) ||
            !string.Equals(name, "Invoke", StringComparison.Ordinal))
        {
            return false;
        }

        return callNode.TryGetProperty<string>("MethodFullName", out string? methodFullName) &&
               methodFullName?.Contains(".Invoke(", StringComparison.Ordinal) is true;
    }

    private static bool LooksLikeAssignmentNode(CpgNode node)
    {
        return node.Kind == CpgNodeKind.Call &&
               node.TryGetProperty<string>("Name", out string? name) &&
               (string.Equals(name, "=", StringComparison.Ordinal) ||
                string.Equals(name, "set", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("set_", StringComparison.Ordinal));
    }

    private static IEnumerable<CpgNode> GetAstChildren(CpgGraph graph, CpgNode parentNode)
    {
        return graph.GetOutgoingEdges(parentNode.Id, CpgEdgeKind.Ast)
            .Select(edge => graph.GetNode(edge.TargetId))
            .ToList();
    }

    private static CpgNode? GetAstParent(CpgGraph graph, CpgNode node)
    {
        CpgEdge? edge = graph.GetIncomingEdges(node.Id, CpgEdgeKind.Ast).FirstOrDefault();
        return edge is null ? null : graph.GetNode(edge.SourceId);
    }

    private static IEnumerable<CpgNode> GetAstDescendants(CpgGraph graph, CpgNode rootNode)
    {
        Stack<CpgNode> stack = new(GetAstChildren(graph, rootNode).Reverse());

        while (stack.Count > 0)
        {
            CpgNode current = stack.Pop();
            yield return current;

            foreach (CpgNode child in GetAstChildren(graph, current).Reverse())
            {
                stack.Push(child);
            }
        }
    }

    private static void EnsureCallEdge(CpgGraphBuilder builder, CpgNode callNode, CpgNode methodNode)
    {
        bool exists = builder.Graph
            .GetOutgoingEdges(callNode.Id, CpgEdgeKind.Call)
            .Any(edge => edge.TargetId == methodNode.Id);

        if (!exists)
        {
            builder.AddEdge(callNode.Id, methodNode.Id, CpgEdgeKind.Call);
        }
    }

    private static string GetStringProperty(CpgNode node, string propertyName)
    {
        return node.TryGetProperty<string>(propertyName, out string? value) ? value ?? string.Empty : string.Empty;
    }
}
