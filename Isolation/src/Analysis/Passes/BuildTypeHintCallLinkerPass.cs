using Analysis.Core;

namespace Analysis.Passes;

/// <summary>
/// 使用类型恢复给出的候选方法全名补齐调用图。
///
/// 这个 pass 对齐 Joern `XTypeHintCallLinker` 的最小职责：
/// - 读取 `CALL.DynamicTypeHintFullNames`；
/// - 优先连接已有 `METHOD`；
/// - 若不存在，则创建最小方法桩并连接；
/// - 当候选唯一时，回写 `CALL.MethodFullName`。
/// </summary>
public sealed class BuildTypeHintCallLinkerPass : CpgPass
{
    /// <inheritdoc />
    protected override void Execute(CpgGraphBuilder builder)
    {
        Dictionary<string, CpgNode> methodsByFullName = builder.Graph
            .GetNodes(CpgNodeKind.Method)
            .Where(node => node.TryGetProperty<string>("FullName", out string? fullName) && !string.IsNullOrWhiteSpace(fullName))
            .GroupBy(node => node.TryGetProperty<string>("FullName", out string? fullName) ? fullName! : string.Empty)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (CpgNode callNode in builder.Graph.GetNodes(CpgNodeKind.Call).ToList())
        {
            if (!TryGetHintedMethodFullNames(callNode, out IReadOnlyList<string>? hintedMethodFullNames) ||
                hintedMethodFullNames.Count == 0)
            {
                continue;
            }

            List<string> effectiveHints = hintedMethodFullNames
                .Where(fullName => !string.IsNullOrWhiteSpace(fullName) && !string.Equals(fullName, "<unknown>", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (effectiveHints.Count == 0)
            {
                continue;
            }

            foreach (string fullName in effectiveHints)
            {
                if (!methodsByFullName.TryGetValue(fullName, out CpgNode? methodNode))
                {
                    methodNode = CreateMethodStub(builder, fullName);
                    methodsByFullName[fullName] = methodNode;
                }

                bool edgeExists = builder.Graph
                    .GetOutgoingEdges(callNode.Id, CpgEdgeKind.Call)
                    .Any(edge => edge.TargetId == methodNode.Id);

                if (!edgeExists)
                {
                    builder.AddEdge(callNode.Id, methodNode.Id, CpgEdgeKind.Call);
                }
            }

            if (effectiveHints.Count == 1)
            {
                callNode.SetProperty("MethodFullName", effectiveHints[0]);
            }
        }
    }

    private static bool TryGetHintedMethodFullNames(CpgNode callNode, out IReadOnlyList<string>? fullNames)
    {
        if (callNode.TryGetProperty<IReadOnlyList<string>>("DynamicTypeHintFullNames", out IReadOnlyList<string>? listValue))
        {
            fullNames = listValue;
            return true;
        }

        if (callNode.TryGetProperty<string[]>("DynamicTypeHintFullNames", out string[]? arrayValue))
        {
            fullNames = arrayValue;
            return true;
        }

        if (callNode.TryGetProperty<List<string>>("DynamicTypeHintFullNames", out List<string>? mutableListValue))
        {
            fullNames = mutableListValue;
            return true;
        }

        fullNames = null;
        return false;
    }

    private static CpgNode CreateMethodStub(CpgGraphBuilder builder, string fullName)
    {
        string name = GetMethodName(fullName);

        CpgNode methodNode = builder.CreateNode(CpgNodeKind.Method);
        methodNode.SetProperty("Name", name);
        methodNode.SetProperty("FullName", fullName);
        methodNode.SetProperty("Signature", "<unknown>");
        methodNode.SetProperty("ContainingTypeFullName", GetContainingTypeFullName(fullName, name));
        methodNode.SetProperty("ReturnTypeFullName", "<unknown>");
        methodNode.SetProperty("IsExternal", true);
        return methodNode;
    }

    private static string GetMethodName(string fullName)
    {
        int parameterIndex = fullName.IndexOf('(');
        int lastDot = parameterIndex > 0
            ? fullName.LastIndexOf('.', parameterIndex)
            : fullName.LastIndexOf('.');
        return lastDot >= 0 ? fullName[(lastDot + 1)..parameterIndex] : fullName;
    }

    private static string GetContainingTypeFullName(string fullName, string methodName)
    {
        int separatorIndex = fullName.LastIndexOf($".{methodName}(", StringComparison.Ordinal);
        return separatorIndex > 0 ? fullName[..separatorIndex] : string.Empty;
    }
}
