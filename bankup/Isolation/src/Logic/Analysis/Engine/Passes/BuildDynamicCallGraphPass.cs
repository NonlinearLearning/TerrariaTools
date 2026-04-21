using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Passes;

/// <summary>
/// 为动态分派调用补齐可能的候选目标。
///
/// 这个实现直接参考 Joern `DynamicCallLinker` 的核心思路：
/// - 先保留精确静态链接结果；
/// - 再根据类型层级向下展开子类型；
/// - 对每个子类型按“方法名 + 签名”查找候选实现；
/// - 最后把这些候选方法补成 `CALL` 边。
///
/// 当前版本只做阶段二最重要的近似：
/// - 接口调用 -> 实现类方法；
/// - 抽象 / virtual 调用 -> override 方法。
///
/// 当前刻意不做更激进的情况：
/// - delegate 调用；
/// - 反射调用；
/// - 泛型约束驱动的进一步求精；
/// - `new` 隐藏成员的精确区分。
/// </summary>
public sealed class BuildDynamicCallGraphPass : CpgPass
{

    protected override void Execute(CpgGraphBuilder builder)
    {
        Dictionary<string, CpgNode> typeDeclsByFullName = builder.Graph
            .GetNodes(CpgNodeKind.TypeDecl)
            .Where(node => node.TryGetProperty<string>("FullName", out string? fullName) &&
                           !string.IsNullOrWhiteSpace(fullName))
            .ToDictionary(
                node => node.TryGetProperty<string>("FullName", out string? fullName) ? fullName! : string.Empty,
                node => node,
                StringComparer.Ordinal);

        Dictionary<string, CpgNode> methodsByFullName = builder.Graph
            .GetNodes(CpgNodeKind.Method)
            .Where(node => node.TryGetProperty<string>("FullName", out string? fullName) &&
                           !string.IsNullOrWhiteSpace(fullName))
            .GroupBy(node => node.TryGetProperty<string>("FullName", out string? fullName) ? fullName! : string.Empty)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        Dictionary<long, string> typeFullNamesByNodeId = typeDeclsByFullName.Values
            .ToDictionary(node => node.Id, node => node.TryGetProperty<string>("FullName", out string? fullName) ? fullName ?? string.Empty : string.Empty);

        Dictionary<string, List<string>> directSubtypesByBaseType = BuildDirectSubtypeMap(typeDeclsByFullName.Values);
        Dictionary<string, List<string>> directSupertypesByType = BuildDirectSupertypeMap(typeDeclsByFullName.Values);
        Dictionary<string, List<CpgNode>> methodsByTypeAndShape = BuildMethodMap(builder.Graph, typeFullNamesByNodeId);
        Dictionary<string, HashSet<string>> validCandidatesByMethodFullName = BuildCandidateCache(
            typeDeclsByFullName,
            methodsByTypeAndShape,
            directSubtypesByBaseType);

        foreach (CpgNode callNode in builder.Graph.GetNodes(CpgNodeKind.Call))
        {
            if (!callNode.TryGetProperty<string>("DispatchType", out string? dispatchType) ||
                !string.Equals(dispatchType, "DYNAMIC_DISPATCH", StringComparison.Ordinal))
            {
                continue;
            }

            if (!callNode.TryGetProperty<string>("MethodFullName", out string? methodFullName) ||
                string.IsNullOrWhiteSpace(methodFullName) ||
                !methodsByFullName.TryGetValue(methodFullName, out CpgNode? declaredTarget))
            {
                continue;
            }

            if (!ShouldExpandDynamicTargets(declaredTarget, typeDeclsByFullName))
            {
                continue;
            }

            string declaredTypeFullName = GetStringProperty(declaredTarget, "ContainingTypeFullName");
            if (string.IsNullOrWhiteSpace(declaredTypeFullName))
            {
                continue;
            }

            HashSet<string> candidateMethodFullNames = GetCandidateMethodFullNames(
                declaredTarget,
                methodsByTypeAndShape,
                directSubtypesByBaseType,
                directSupertypesByType,
                validCandidatesByMethodFullName);

            if (candidateMethodFullNames.Count == 0)
            {
                candidateMethodFullNames.Add(GetStringProperty(declaredTarget, "FullName"));
            }

            List<CpgNode> candidateNodes = candidateMethodFullNames
                .Where(methodsByFullName.ContainsKey)
                .Select(methodFullName => methodsByFullName[methodFullName])
                .ToList();

            List<CpgNode> internalCandidates = candidateNodes.Where(candidate => !IsExternalStub(candidate)).ToList();
            if (internalCandidates.Count > 0)
            {
                foreach (CpgNode externalCandidate in builder.Graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Call)
                             .Select(edge => builder.Graph.GetNode(edge.TargetId))
                             .Where(IsExternalStub)
                             .ToList())
                {
                    builder.Graph.RemoveEdge(callNode.Id, externalCandidate.Id, CpgEdgeKind.Call);
                }
            }

            foreach (CpgNode candidate in (internalCandidates.Count > 0 ? internalCandidates : candidateNodes))
            {
                EnsureCallEdge(builder, callNode, candidate);
            }
        }
    }

    private static Dictionary<string, List<string>> BuildDirectSubtypeMap(IEnumerable<CpgNode> typeDeclNodes)
    {
        Dictionary<string, List<string>> map = new(StringComparer.Ordinal);

        foreach (CpgNode typeDeclNode in typeDeclNodes)
        {
            string currentType = GetStringProperty(typeDeclNode, "FullName");
            if (string.IsNullOrWhiteSpace(currentType) ||
                !typeDeclNode.TryGetProperty<IReadOnlyCollection<string>>("InheritsFromTypeFullNames", out IReadOnlyCollection<string>? baseTypes))
            {
                continue;
            }

            foreach (string baseType in baseTypes.Where(baseType => !string.IsNullOrWhiteSpace(baseType)))
            {
                if (!map.TryGetValue(baseType, out List<string>? subtypes))
                {
                    subtypes = new List<string>();
                    map[baseType] = subtypes;
                }

                if (!subtypes.Contains(currentType, StringComparer.Ordinal))
                {
                    subtypes.Add(currentType);
                }
            }
        }

        return map;
    }

    private static Dictionary<string, List<string>> BuildDirectSupertypeMap(IEnumerable<CpgNode> typeDeclNodes)
    {
        Dictionary<string, List<string>> map = new(StringComparer.Ordinal);

        foreach (CpgNode typeDeclNode in typeDeclNodes)
        {
            string currentType = GetStringProperty(typeDeclNode, "FullName");
            if (string.IsNullOrWhiteSpace(currentType) ||
                !typeDeclNode.TryGetProperty<IReadOnlyCollection<string>>("InheritsFromTypeFullNames", out IReadOnlyCollection<string>? baseTypes))
            {
                continue;
            }

            map[currentType] = baseTypes.Where(baseType => !string.IsNullOrWhiteSpace(baseType)).Distinct(StringComparer.Ordinal).ToList();
        }

        return map;
    }

    private static Dictionary<string, List<CpgNode>> BuildMethodMap(CpgGraph graph, IReadOnlyDictionary<long, string> typeFullNamesByNodeId)
    {
        Dictionary<string, List<CpgNode>> map = new(StringComparer.Ordinal);

        foreach (CpgNode methodNode in graph.GetNodes(CpgNodeKind.Method))
        {
            string containingTypeFullName = GetStringProperty(methodNode, "ContainingTypeFullName");
            if (string.IsNullOrWhiteSpace(containingTypeFullName) &&
                methodNode.TryGetProperty<long>("AstParentId", out long astParentId) &&
                typeFullNamesByNodeId.TryGetValue(astParentId, out string? parentType))
            {
                containingTypeFullName = parentType;
            }

            if (string.IsNullOrWhiteSpace(containingTypeFullName))
            {
                continue;
            }

            foreach (string key in BuildMethodLookupKeys(methodNode, containingTypeFullName))
            {
                if (!map.TryGetValue(key, out List<CpgNode>? methods))
                {
                    methods = new List<CpgNode>();
                    map[key] = methods;
                }

                if (!methods.Contains(methodNode))
                {
                    methods.Add(methodNode);
                }
            }
        }

        return map;
    }

    private static IEnumerable<string> GetAllSubtypes(
        string declaredTypeFullName,
        IReadOnlyDictionary<string, List<string>> directSubtypesByBaseType)
    {
        HashSet<string> visited = new(StringComparer.Ordinal);
        Queue<string> pending = new();

        pending.Enqueue(declaredTypeFullName);
        visited.Add(declaredTypeFullName);

        while (pending.Count > 0)
        {
            string current = pending.Dequeue();
            yield return current;

            if (!directSubtypesByBaseType.TryGetValue(current, out List<string>? children))
            {
                continue;
            }

            foreach (string child in children)
            {
                if (visited.Add(child))
                {
                    pending.Enqueue(child);
                }
            }
        }
    }

    private static IEnumerable<string> GetAllSupertypes(
        string declaredTypeFullName,
        IReadOnlyDictionary<string, List<string>> directSupertypesByType)
    {
        HashSet<string> visited = new(StringComparer.Ordinal);
        Queue<string> pending = new();

        pending.Enqueue(declaredTypeFullName);
        visited.Add(declaredTypeFullName);

        while (pending.Count > 0)
        {
            string current = pending.Dequeue();
            yield return current;

            if (!directSupertypesByType.TryGetValue(current, out List<string>? parents))
            {
                continue;
            }

            foreach (string parent in parents)
            {
                if (visited.Add(parent))
                {
                    pending.Enqueue(parent);
                }
            }
        }
    }

    private static bool ShouldExpandDynamicTargets(
        CpgNode declaredTarget,
        IReadOnlyDictionary<string, CpgNode> typeDeclsByFullName)
    {
        bool isAbstract = declaredTarget.TryGetProperty<bool>("IsAbstract", out bool abstractValue) && abstractValue;
        bool isVirtual = declaredTarget.TryGetProperty<bool>("IsVirtual", out bool virtualValue) && virtualValue;
        bool isOverride = declaredTarget.TryGetProperty<bool>("IsOverride", out bool overrideValue) && overrideValue;

        return isAbstract || isVirtual || isOverride || IsInterfaceMethod(declaredTarget, typeDeclsByFullName);
    }

    private static bool IsInterfaceMethod(
        CpgNode declaredTarget,
        IReadOnlyDictionary<string, CpgNode> typeDeclsByFullName)
    {
        string declaredTypeFullName = GetStringProperty(declaredTarget, "ContainingTypeFullName");
        return !string.IsNullOrWhiteSpace(declaredTypeFullName) &&
               typeDeclsByFullName.TryGetValue(declaredTypeFullName, out CpgNode? typeDeclNode) &&
               string.Equals(GetStringProperty(typeDeclNode, "TypeKind"), "Interface", StringComparison.Ordinal);
    }

    private static bool IsExternalStub(CpgNode methodNode)
    {
        return methodNode.TryGetProperty<bool>("IsExternal", out bool isExternal) && isExternal;
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

    private static string ComposeMethodLookupKey(string containingTypeFullName, string methodShape)
    {
        return $"{containingTypeFullName}::{methodShape}";
    }

    private static Dictionary<string, HashSet<string>> BuildCandidateCache(
        IReadOnlyDictionary<string, CpgNode> typeDeclsByFullName,
        IReadOnlyDictionary<string, List<CpgNode>> methodsByTypeAndShape,
        IReadOnlyDictionary<string, List<string>> directSubtypesByBaseType)
    {
        Dictionary<string, HashSet<string>> cache = new(StringComparer.Ordinal);

        foreach (CpgNode typeDeclNode in typeDeclsByFullName.Values)
        {
            string typeFullName = GetStringProperty(typeDeclNode, "FullName");
            foreach (CpgNode methodNode in methodsByTypeAndShape.Values.SelectMany(nodes => nodes).Where(methodNode =>
                         string.Equals(GetStringProperty(methodNode, "ContainingTypeFullName"), typeFullName, StringComparison.Ordinal)))
            {
                string methodFullName = GetStringProperty(methodNode, "FullName");
                if (string.IsNullOrWhiteSpace(methodFullName))
                {
                    continue;
                }

                HashSet<string> candidates = new(StringComparer.Ordinal);
                string methodShape = BuildMethodShape(methodNode);
                foreach (string subtype in GetAllSubtypes(typeFullName, directSubtypesByBaseType))
                {
                    string key = ComposeMethodLookupKey(subtype, methodShape);
                    if (!methodsByTypeAndShape.TryGetValue(key, out List<CpgNode>? nodes))
                    {
                        continue;
                    }

                    foreach (CpgNode candidate in nodes)
                    {
                        candidates.Add(GetStringProperty(candidate, "FullName"));
                    }
                }

                cache[methodFullName] = candidates;
            }
        }

        return cache;
    }

    private static HashSet<string> GetCandidateMethodFullNames(
        CpgNode declaredTarget,
        IReadOnlyDictionary<string, List<CpgNode>> methodsByTypeAndShape,
        IReadOnlyDictionary<string, List<string>> directSubtypesByBaseType,
        IReadOnlyDictionary<string, List<string>> directSupertypesByType,
        IReadOnlyDictionary<string, HashSet<string>> validCandidatesByMethodFullName)
    {
        string declaredMethodFullName = GetStringProperty(declaredTarget, "FullName");
        if (validCandidatesByMethodFullName.TryGetValue(declaredMethodFullName, out HashSet<string>? cached))
        {
            return new HashSet<string>(cached, StringComparer.Ordinal);
        }

        HashSet<string> candidates = new(StringComparer.Ordinal);
        string declaredTypeFullName = GetStringProperty(declaredTarget, "ContainingTypeFullName");
        string methodShape = BuildMethodShape(declaredTarget);

        foreach (string subtype in GetAllSubtypes(declaredTypeFullName, directSubtypesByBaseType))
        {
            string key = ComposeMethodLookupKey(subtype, methodShape);
            if (!methodsByTypeAndShape.TryGetValue(key, out List<CpgNode>? nodes))
            {
                continue;
            }

            foreach (CpgNode candidate in nodes)
            {
                candidates.Add(GetStringProperty(candidate, "FullName"));
            }
        }

        if (candidates.Count == 0)
        {
            foreach (string supertype in GetAllSupertypes(declaredTypeFullName, directSupertypesByType))
            {
                string key = ComposeMethodLookupKey(supertype, methodShape);
                if (!methodsByTypeAndShape.TryGetValue(key, out List<CpgNode>? nodes))
                {
                    continue;
                }

                foreach (CpgNode candidate in nodes)
                {
                    candidates.Add(GetStringProperty(candidate, "FullName"));
                }
            }
        }

        return candidates;
    }

    private static IEnumerable<string> BuildMethodLookupKeys(CpgNode methodNode, string containingTypeFullName)
    {
        string methodShape = BuildMethodShape(methodNode);
        yield return ComposeMethodLookupKey(containingTypeFullName, methodShape);

        string fullName = GetStringProperty(methodNode, "FullName");
        int interfaceSeparator = fullName.LastIndexOf($".{GetStringProperty(methodNode, "Name")}(", StringComparison.Ordinal);
        if (interfaceSeparator <= containingTypeFullName.Length)
        {
            yield break;
        }

        string middleSegment = fullName.Substring(containingTypeFullName.Length + 1, interfaceSeparator - containingTypeFullName.Length - 1);
        if (string.IsNullOrWhiteSpace(middleSegment))
        {
            yield break;
        }

        int lastDot = middleSegment.LastIndexOf('.');
        if (lastDot <= 0)
        {
            yield break;
        }

        string explicitInterfaceType = middleSegment[..lastDot];
        yield return ComposeMethodLookupKey(containingTypeFullName, $"{middleSegment[(lastDot + 1)..]}::{GetStringProperty(methodNode, "Signature")}");
        yield return ComposeMethodLookupKey(explicitInterfaceType, methodShape);
    }

    private static string BuildMethodShape(CpgNode methodNode)
    {
        return $"{GetStringProperty(methodNode, "Name")}::{GetStringProperty(methodNode, "Signature")}";
    }

    private static string GetStringProperty(CpgNode node, string propertyName)
    {
        return node.TryGetProperty<string>(propertyName, out string? value) ? value ?? string.Empty : string.Empty;
    }
}
