using Analysis.Core;

namespace Analysis.Passes;

/// <summary>
/// 基于稳定方法全名补齐静态调用图。
///
/// 前端负责把 `CALL.MethodFullName` 算准。
/// 这个 pass 只做精确匹配：找到就连边，找不到就保留未解析状态。
/// </summary>
public sealed class BuildStaticCallGraphPass : CpgPass
{
    /// <inheritdoc />
    protected override void Execute(CpgGraphBuilder builder)
    {
        IReadOnlyDictionary<string, CpgNode> methodsByFullName = builder.Graph
            .GetNodes(CpgNodeKind.Method)
            .Where(node => node.TryGetProperty<string>("FullName", out string? fullName) &&
                           !string.IsNullOrWhiteSpace(fullName))
            .GroupBy(node => node.TryGetProperty<string>("FullName", out string? fullName) ? fullName! : string.Empty)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (CpgNode callNode in builder.Graph.GetNodes(CpgNodeKind.Call))
        {
            if (!callNode.TryGetProperty<string>("MethodFullName", out string? methodFullName) ||
                string.IsNullOrWhiteSpace(methodFullName))
            {
                continue;
            }

            if (!methodsByFullName.TryGetValue(methodFullName, out CpgNode? methodNode) &&
                !TryResolveGenericMethod(methodsByFullName.Values, methodFullName, out methodNode))
            {
                continue;
            }

            bool relationExists = builder.Graph
                .GetOutgoingEdges(callNode.Id, CpgEdgeKind.Call)
                .Any(edge => edge.TargetId == methodNode.Id);

            if (!relationExists)
            {
                builder.AddEdge(callNode.Id, methodNode.Id, CpgEdgeKind.Call);
            }

            callNode.SetProperty("ResolvedMethodFullName", methodFullName);
        }
    }

    private static bool TryResolveGenericMethod(
        IEnumerable<CpgNode> methods,
        string callMethodFullName,
        out CpgNode? methodNode)
    {
        methodNode = null;
        string callPrefix = MethodPrefix(callMethodFullName);
        if (string.IsNullOrWhiteSpace(callPrefix))
        {
            return false;
        }

        List<CpgNode> candidates = methods
            .Where(node => node.TryGetProperty<string>("FullName", out string? fullName) &&
                           string.Equals(MethodPrefix(fullName ?? string.Empty), callPrefix, StringComparison.Ordinal))
            .ToList();
        if (candidates.Count != 1)
        {
            return false;
        }

        methodNode = candidates[0];
        return true;
    }

    private static string MethodPrefix(string methodFullName)
    {
        int argumentStart = methodFullName.IndexOf('(', StringComparison.Ordinal);
        return argumentStart <= 0 ? string.Empty : methodFullName[..argumentStart];
    }
}
