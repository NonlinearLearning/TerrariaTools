using Analysis.Core;

namespace Analysis.Passes;

/// <summary>
/// 执行最小类型恢复。
///
/// 当前实现不追求复刻 Joern `XTypeRecovery.scala` 的全部细节，
/// 但保留两条最关键路径：
/// - 从声明节点和引用关系恢复 `PossibleTypes`；
/// - 基于接收者候选类型为动态调用补 `DynamicTypeHintFullNames`。
/// </summary>
public sealed class BuildTypeRecoveryPass : CpgPass
{
    private const string DummyReturnType = "<returnValue>";
    private const string DummyMemberLoad = "<member>";
    private const string DummyIndexAccess = "<indexAccess>";

    /// <inheritdoc />
    protected override void Execute(CpgGraphBuilder builder)
    {
        PropagateImportAliasTypes(builder.Graph);
        PropagateMethodReferenceAliases(builder.Graph);
        RecoverStaticImportCallHints(builder.Graph);
        SeedDeclarationPossibleTypes(builder.Graph);
        RecoverMethodAliasCallHints(builder.Graph);
        PersistAssignedMemberTypes(builder.Graph);
        PropagateAssignedTypes(builder.Graph);
        PropagateIdentifierTypes(builder.Graph);
        RecoverDynamicCallHints(builder.Graph);
        PropagateArgumentTypesToParameters(builder.Graph);
        PropagateIdentifierTypes(builder.Graph);
        PropagateMethodReturnTypes(builder.Graph);
        PropagateCallResultTypes(builder.Graph);
        PropagateMethodReturnTypes(builder.Graph);
        PropagateAssignedTypes(builder.Graph);
        PropagateIdentifierTypes(builder.Graph);
        ResolveDummyPossibleTypes(builder.Graph);
        RecoverDynamicCallHints(builder.Graph);
    }

    private static void PropagateImportAliasTypes(CpgGraph graph)
    {
        Dictionary<string, string[]> aliasTypes = graph.GetNodes(CpgNodeKind.Import)
            .Where(node =>
                node.TryGetProperty<string>("ImportedAs", out string? importedAs) &&
                !string.IsNullOrWhiteSpace(importedAs) &&
                node.TryGetProperty<string>("ResolvedImportKind", out string? resolvedImportKind) &&
                string.Equals(resolvedImportKind, "TYPE", StringComparison.Ordinal) &&
                node.TryGetProperty<string>("ImportedEntity", out string? importedEntity) &&
                !string.IsNullOrWhiteSpace(importedEntity))
            .GroupBy(
                node => node.TryGetProperty<string>("ImportedAs", out string? importedAs) ? importedAs! : string.Empty,
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(node => node.TryGetProperty<string>("ImportedEntity", out string? importedEntity) ? importedEntity : null)
                    .Where(static importedEntity => !string.IsNullOrWhiteSpace(importedEntity))
                    .Cast<string>()
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        if (aliasTypes.Count == 0)
        {
            return;
        }

        foreach (CpgNode node in graph.Nodes.Where(node => node.Kind is CpgNodeKind.Identifier or CpgNodeKind.Local))
        {
            if (!node.TryGetProperty<string>("Name", out string? name) ||
                string.IsNullOrWhiteSpace(name) ||
                !aliasTypes.TryGetValue(name, out string[]? importedTypes))
            {
                continue;
            }

            node.SetProperty("PossibleTypes", MergeTypes(GetPossibleTypes(node), importedTypes).ToArray());
        }
    }

    private static void PropagateMethodReferenceAliases(CpgGraph graph)
    {
        foreach (CpgNode declarationNode in graph.Nodes.Where(IsDeclarationLikeNode))
        {
            string[] methodAliases = GetAstDescendants(graph, declarationNode)
                .Where(node => node.Kind == CpgNodeKind.MethodRef)
                .Select(node => node.TryGetProperty<string>("MethodFullName", out string? fullName) ? fullName : null)
                .Where(static fullName => !string.IsNullOrWhiteSpace(fullName))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (methodAliases.Length > 0)
            {
                declarationNode.SetProperty("AliasMethodFullNames", methodAliases);
            }
        }
    }

    private static void PropagateMethodReturnTypes(CpgGraph graph)
    {
        foreach (CpgNode methodReturnNode in graph.GetNodes(CpgNodeKind.MethodReturn))
        {
            if (!methodReturnNode.TryGetProperty<long>("AstParentId", out long methodId))
            {
                continue;
            }

            string[] returnTypes = graph
                .GetNodes(CpgNodeKind.ControlStructure)
                .Where(node =>
                    node.TryGetProperty<string>("ControlStructureType", out string? controlType) &&
                    string.Equals(controlType, "RETURN", StringComparison.Ordinal) &&
                    IsDescendantOfMethod(graph, node, methodId))
                .SelectMany(returnNode => graph.GetOutgoingEdges(returnNode.Id, CpgEdgeKind.Ast))
                .Select(edge => graph.GetNode(edge.TargetId))
                .SelectMany(node => GetBestKnownTypes(graph, node))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (returnTypes.Length > 0)
            {
                methodReturnNode.SetProperty("PossibleTypes", returnTypes);
            }
        }
    }

    private static void PropagateCallResultTypes(CpgGraph graph)
    {
        Dictionary<string, CpgNode> methodsByFullName = graph
            .GetNodes(CpgNodeKind.Method)
            .Where(node => node.TryGetProperty<string>("FullName", out string? fullName) && !string.IsNullOrWhiteSpace(fullName))
            .GroupBy(node => node.TryGetProperty<string>("FullName", out string? fullName) ? fullName! : string.Empty)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (CpgNode callNode in graph.GetNodes(CpgNodeKind.Call))
        {
            string[] callResultTypes = Array.Empty<string>();

            IEnumerable<CpgNode> targetMethods = GetCandidateTargetMethods(callNode, methodsByFullName);
            string[] propagatedReturnTypes = targetMethods
                .SelectMany(methodNode => graph.GetNodes(CpgNodeKind.MethodReturn)
                    .Where(node =>
                        node.TryGetProperty<long>("AstParentId", out long parentId) &&
                        parentId == methodNode.Id))
                .SelectMany(methodReturnNode => MergeTypes(
                    GetPossibleTypes(methodReturnNode),
                    GetKnownTypes(methodReturnNode)))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (propagatedReturnTypes.Length > 0)
            {
                callResultTypes = propagatedReturnTypes;
            }

            if (callResultTypes.Length == 0 &&
                callNode.TryGetProperty<string>("Name", out string? indexAccessName) &&
                string.Equals(indexAccessName, "[]", StringComparison.Ordinal))
            {
                CpgNode? receiverNode = GetReceiverNode(graph, callNode);
                if (receiverNode is not null)
                {
                    callResultTypes = RecoverIndexAccessTypes(graph, receiverNode);
                }
            }

            if (callResultTypes.Length == 0 &&
                callNode.TryGetProperty<string>("Name", out string? name) &&
                string.Equals(name, ".", StringComparison.Ordinal))
            {
                CpgNode? memberNode = graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Ref)
                    .Select(edge => graph.GetNode(edge.TargetId))
                    .FirstOrDefault(node => node.Kind == CpgNodeKind.Member);
                if (memberNode is not null)
                {
                    callResultTypes = MergeTypes(
                            GetPossibleTypes(memberNode),
                            GetKnownTypes(memberNode))
                        .ToArray();
                }

                if (callResultTypes.Length == 0)
                {
                    callResultTypes = RecoverFieldAccessTypes(graph, callNode, out CpgNode? resolvedMemberNode);
                    if (resolvedMemberNode is not null &&
                        !graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Ref).Any(edge => edge.TargetId == resolvedMemberNode.Id))
                    {
                        graph.AddEdge(callNode.Id, resolvedMemberNode.Id, CpgEdgeKind.Ref);
                    }
                }
            }

            if (callResultTypes.Length == 0)
            {
                callResultTypes = RecoverDummyCallResultTypes(graph, callNode);
            }

            if (callResultTypes.Length > 0)
            {
                callNode.SetProperty("PossibleTypes", callResultTypes);
            }
        }
    }

    private static string[] RecoverDummyCallResultTypes(CpgGraph graph, CpgNode callNode)
    {
        if (!callNode.TryGetProperty<string>("Name", out string? callName) ||
            string.IsNullOrWhiteSpace(callName))
        {
            return Array.Empty<string>();
        }

        if (string.Equals(callName, "[]", StringComparison.Ordinal))
        {
            CpgNode? receiverNode = GetReceiverNode(graph, callNode);
            string receiverName = receiverNode is null ? callName : GetNodeName(receiverNode);
            if (string.IsNullOrWhiteSpace(receiverName))
            {
                receiverName = "[]";
            }

            return new[] { $"{receiverName}.{DummyIndexAccess}" };
        }

        if (string.Equals(callName, ".", StringComparison.Ordinal))
        {
            string memberName = GetFieldName(callNode);
            CpgNode? receiverNode = GetReceiverNode(graph, callNode);
            string[] receiverTypes = receiverNode is null
                ? Array.Empty<string>()
                : MergeTypes(GetPossibleTypes(receiverNode), GetKnownTypes(receiverNode)).ToArray();
            if (receiverTypes.Length == 0 && receiverNode is not null && !string.IsNullOrWhiteSpace(GetNodeName(receiverNode)))
            {
                receiverTypes = new[] { GetNodeName(receiverNode) };
            }

            if (receiverTypes.Length == 0 || string.IsNullOrWhiteSpace(memberName))
            {
                return Array.Empty<string>();
            }

            return receiverTypes
                .Select(typeFullName => $"{typeFullName}.{DummyMemberLoad}({memberName})")
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        if (callName.StartsWith("<operator>", StringComparison.Ordinal) ||
            string.Equals(callName, "=", StringComparison.Ordinal) ||
            string.Equals(callName, "arrayInitializer", StringComparison.Ordinal) ||
            string.Equals(callName, "collectionInitializer", StringComparison.Ordinal))
        {
            return Array.Empty<string>();
        }

        return new[] { $"{callName}.{DummyReturnType}" };
    }

    private static IEnumerable<CpgNode> GetCandidateTargetMethods(
        CpgNode callNode,
        IReadOnlyDictionary<string, CpgNode> methodsByFullName)
    {
        HashSet<string> candidateFullNames = new(StringComparer.Ordinal);

        if (callNode.TryGetProperty<string>("MethodFullName", out string? methodFullName) &&
            !string.IsNullOrWhiteSpace(methodFullName))
        {
            _ = candidateFullNames.Add(methodFullName);
        }

        foreach (string hintedMethodFullName in GetHintedMethodFullNames(callNode))
        {
            _ = candidateFullNames.Add(hintedMethodFullName);
        }

        CpgNode[] exactMatches = candidateFullNames
            .Where(methodsByFullName.ContainsKey)
            .Select(fullName => methodsByFullName[fullName])
            .Distinct()
            .ToArray();
        if (exactMatches.Length > 0)
        {
            return exactMatches;
        }

        string recoveredMethodName = callNode.TryGetProperty<string>("Name", out string? callName)
            ? callName ?? string.Empty
            : string.Empty;
        if (string.IsNullOrWhiteSpace(recoveredMethodName))
        {
            return Array.Empty<CpgNode>();
        }

        return methodsByFullName.Values
            .Where(node =>
                node.TryGetProperty<string>("Name", out string? methodName) &&
                string.Equals(methodName, recoveredMethodName, StringComparison.Ordinal))
            .Distinct();
    }

    private static string[] RecoverIndexAccessTypes(CpgGraph graph, CpgNode receiverNode)
    {
        Dictionary<string, List<CpgNode>> declarationsByName = graph.Nodes
            .Where(IsDeclarationLikeNode)
            .Where(node => node.TryGetProperty<string>("Name", out string? name) && !string.IsNullOrWhiteSpace(name))
            .GroupBy(node => node.TryGetProperty<string>("Name", out string? name) ? name! : string.Empty, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        IEnumerable<string> receiverTypes = MergeTypes(GetPossibleTypes(receiverNode), GetKnownTypes(receiverNode));
        if (!receiverTypes.Any() &&
            receiverNode.Kind == CpgNodeKind.Identifier &&
            declarationsByName.TryGetValue(GetNodeName(receiverNode), out List<CpgNode>? declarations) &&
            declarations.Count == 1)
        {
            receiverTypes = MergeTypes(GetPossibleTypes(declarations[0]), GetKnownTypes(declarations[0]));
        }

        return receiverTypes
            .SelectMany(GetIndexElementTypes)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] RecoverFieldAccessTypes(CpgGraph graph, CpgNode callNode, out CpgNode? resolvedMemberNode)
    {
        resolvedMemberNode = null;

        CpgNode? receiverNode = GetReceiverNode(graph, callNode);
        if (receiverNode is null)
        {
            return Array.Empty<string>();
        }

        string memberName = GetFieldName(callNode);
        if (string.IsNullOrWhiteSpace(memberName))
        {
            return Array.Empty<string>();
        }

        string[] receiverTypes = GetBestKnownTypes(graph, receiverNode)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (receiverTypes.Length == 0 &&
            receiverNode.Kind == CpgNodeKind.Identifier)
        {
            receiverTypes = ResolveReceiverTypes(graph, GetNodeName(receiverNode));
        }

        if (receiverTypes.Length == 0)
        {
            return Array.Empty<string>();
        }

        CpgNode[] matchedMembers = graph.GetNodes(CpgNodeKind.Member)
            .Where(node =>
                node.TryGetProperty<string>("Name", out string? currentMemberName) &&
                string.Equals(currentMemberName, memberName, StringComparison.Ordinal))
            .Where(node =>
                node.TryGetProperty<string>("FullName", out string? fullName) &&
                receiverTypes.Any(typeName => string.Equals(fullName, $"{typeName}.{memberName}", StringComparison.Ordinal)))
            .ToArray();

        resolvedMemberNode = matchedMembers.FirstOrDefault();
        string[] memberTypes = matchedMembers
            .SelectMany(node => MergeTypes(GetPossibleTypes(node), GetKnownTypes(node)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (memberTypes.Length > 0)
        {
            return memberTypes;
        }

        return RecoverPropertyGetterTypes(graph, receiverTypes, memberName);
    }

    private static string[] RecoverPropertyGetterTypes(
        CpgGraph graph,
        IReadOnlyCollection<string> receiverTypes,
        string memberName)
    {
        if (receiverTypes.Count == 0 || string.IsNullOrWhiteSpace(memberName))
        {
            return Array.Empty<string>();
        }

        string getterName = $"get_{memberName}";
        CpgNode[] getterMethods = graph.GetNodes(CpgNodeKind.Method)
            .Where(node =>
                node.TryGetProperty<string>("Name", out string? currentMethodName) &&
                string.Equals(currentMethodName, getterName, StringComparison.Ordinal))
            .Where(node =>
                node.TryGetProperty<string>("ContainingTypeFullName", out string? containingType) &&
                receiverTypes.Any(typeName => string.Equals(typeName, containingType, StringComparison.Ordinal)))
            .ToArray();

        if (getterMethods.Length == 0)
        {
            return Array.Empty<string>();
        }

        return getterMethods
            .SelectMany(methodNode => graph.GetNodes(CpgNodeKind.MethodReturn)
                .Where(node =>
                    node.TryGetProperty<long>("AstParentId", out long parentId) &&
                    parentId == methodNode.Id))
            .SelectMany(methodReturnNode => MergeTypes(
                GetPossibleTypes(methodReturnNode),
                GetKnownTypes(methodReturnNode)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static void SeedDeclarationPossibleTypes(CpgGraph graph)
    {
        foreach (CpgNode node in graph.Nodes.Where(IsDeclarationLikeNode))
        {
            string[] types = GetKnownTypes(node).ToArray();
            if (types.Length > 0)
            {
                node.SetProperty("PossibleTypes", types);
            }
        }
    }

    private static void PropagateAssignedTypes(CpgGraph graph)
    {
        foreach (CpgNode declarationNode in graph.Nodes.Where(IsDeclarationLikeNode))
        {
            IReadOnlyCollection<string> assignedTypes = graph
                .GetOutgoingEdges(declarationNode.Id, CpgEdgeKind.Ast)
                .Select(edge => graph.GetNode(edge.TargetId))
                .SelectMany(node => GetBestKnownTypes(graph, node))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (assignedTypes.Count == 0)
            {
                continue;
            }

            declarationNode.SetProperty(
                "PossibleTypes",
                MergeTypes(GetPossibleTypes(declarationNode), assignedTypes).ToArray());
        }
    }

    private static void PersistAssignedMemberTypes(CpgGraph graph)
    {
        foreach (CpgNode callNode in graph.GetNodes(CpgNodeKind.Call))
        {
            if (!TryGetAssignedMemberAndValueNode(graph, callNode, out CpgNode? memberNode, out CpgNode? valueNode) ||
                memberNode is null ||
                valueNode is null)
            {
                continue;
            }

            string[] assignedTypes = GetBestKnownTypes(graph, valueNode)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (assignedTypes.Length == 0)
            {
                continue;
            }

            memberNode.SetProperty(
                "PossibleTypes",
                MergeTypes(GetPossibleTypes(memberNode), assignedTypes).ToArray());
        }
    }

    private static void PropagateIdentifierTypes(CpgGraph graph)
    {
        Dictionary<string, List<CpgNode>> declarationsByName = graph.Nodes
            .Where(IsDeclarationLikeNode)
            .Where(node => node.TryGetProperty<string>("Name", out string? name) && !string.IsNullOrWhiteSpace(name))
            .GroupBy(node => node.TryGetProperty<string>("Name", out string? name) ? name! : string.Empty, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        foreach (CpgNode identifierNode in graph.GetNodes(CpgNodeKind.Identifier))
        {
            CpgNode? targetNode = graph
                .GetOutgoingEdges(identifierNode.Id, CpgEdgeKind.Ref)
                .Select(edge => graph.GetNode(edge.TargetId))
                .FirstOrDefault();
            string[] possibleTypes = targetNode is null
                ? Array.Empty<string>()
                : MergeTypes(GetPossibleTypes(targetNode), GetKnownTypes(targetNode)).ToArray();
            if (possibleTypes.Length == 0)
            {
                targetNode = FindUniqueDeclarationByName(identifierNode, declarationsByName);
                possibleTypes = targetNode is null
                    ? Array.Empty<string>()
                    : MergeTypes(GetPossibleTypes(targetNode), GetKnownTypes(targetNode)).ToArray();
            }

            if (targetNode is null || possibleTypes.Length == 0)
            {
                continue;
            }

            identifierNode.SetProperty("PossibleTypes", possibleTypes);
        }
    }

    private static void RecoverMethodAliasCallHints(CpgGraph graph)
    {
        Dictionary<string, List<CpgNode>> declarationsByName = graph.Nodes
            .Where(IsDeclarationLikeNode)
            .Where(node => node.TryGetProperty<string>("Name", out string? name) && !string.IsNullOrWhiteSpace(name))
            .GroupBy(node => node.TryGetProperty<string>("Name", out string? name) ? name! : string.Empty, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        foreach (CpgNode callNode in graph.GetNodes(CpgNodeKind.Call))
        {
            if (HasKnownMethodFullName(callNode))
            {
                continue;
            }

            List<string> aliasMethodFullNames = GetAstChildren(graph, callNode)
                .Where(node => node.Kind == CpgNodeKind.Identifier)
                .SelectMany(identifierNode => ResolveDeclarationCandidates(graph, identifierNode, declarationsByName))
                .SelectMany(declarationNode => GetAliasMethodFullNames(graph, declarationNode))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (aliasMethodFullNames.Count == 0)
            {
                continue;
            }

            callNode.SetProperty("DynamicTypeHintFullNames", aliasMethodFullNames.ToArray());
        }
    }

    private static void RecoverStaticImportCallHints(CpgGraph graph)
    {
        Dictionary<(string Name, string ContainingType), List<CpgNode>> methodsByNameAndType = graph
            .GetNodes(CpgNodeKind.Method)
            .Where(node =>
                node.TryGetProperty<string>("Name", out string? name) &&
                !string.IsNullOrWhiteSpace(name) &&
                node.TryGetProperty<string>("ContainingTypeFullName", out string? containingType) &&
                !string.IsNullOrWhiteSpace(containingType))
            .GroupBy(node => (
                node.TryGetProperty<string>("Name", out string? name) ? name! : string.Empty,
                node.TryGetProperty<string>("ContainingTypeFullName", out string? containingType) ? containingType! : string.Empty))
            .ToDictionary(group => group.Key, group => group.ToList());

        string[] staticImportedTypes = graph.GetNodes(CpgNodeKind.Import)
            .Where(node =>
                node.TryGetProperty<bool>("IsStatic", out bool isStatic) &&
                isStatic &&
                node.TryGetProperty<string>("ResolvedImportKind", out string? resolvedImportKind) &&
                string.Equals(resolvedImportKind, "TYPE", StringComparison.Ordinal) &&
                node.TryGetProperty<string>("ImportedEntity", out string? importedEntity) &&
                !string.IsNullOrWhiteSpace(importedEntity))
            .Select(node => node.TryGetProperty<string>("ImportedEntity", out string? importedEntity) ? importedEntity : null)
            .Where(static importedEntity => !string.IsNullOrWhiteSpace(importedEntity))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (staticImportedTypes.Length == 0)
        {
            return;
        }

        foreach (CpgNode callNode in graph.GetNodes(CpgNodeKind.Call))
        {
            if (HasKnownMethodFullName(callNode) || GetReceiverNode(graph, callNode) is not null)
            {
                continue;
            }

            string methodName = GetRecoveredMethodName(graph, callNode);
            if (string.IsNullOrWhiteSpace(methodName))
            {
                continue;
            }

            string[] hintedMethodFullNames = staticImportedTypes
                .SelectMany(typeFullName =>
                    methodsByNameAndType.TryGetValue((methodName, typeFullName), out List<CpgNode>? methods)
                        ? methods
                        : Enumerable.Empty<CpgNode>())
                .Select(methodNode => methodNode.TryGetProperty<string>("FullName", out string? fullName) ? fullName : null)
                .Where(static fullName => !string.IsNullOrWhiteSpace(fullName))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (hintedMethodFullNames.Length == 0)
            {
                continue;
            }

            callNode.SetProperty(
                "DynamicTypeHintFullNames",
                MergeTypes(GetHintedMethodFullNames(callNode), hintedMethodFullNames).ToArray());
        }
    }

    private static void RecoverDynamicCallHints(CpgGraph graph)
    {
        Dictionary<string, List<CpgNode>> declarationsByName = graph.Nodes
            .Where(IsDeclarationLikeNode)
            .Where(node => node.TryGetProperty<string>("Name", out string? name) && !string.IsNullOrWhiteSpace(name))
            .GroupBy(node => node.TryGetProperty<string>("Name", out string? name) ? name! : string.Empty, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        Dictionary<(string Name, string ContainingType), List<CpgNode>> methodsByNameAndType = graph
            .GetNodes(CpgNodeKind.Method)
            .Where(node =>
                node.TryGetProperty<string>("Name", out string? name) &&
                !string.IsNullOrWhiteSpace(name) &&
                node.TryGetProperty<string>("ContainingTypeFullName", out string? containingType) &&
                !string.IsNullOrWhiteSpace(containingType))
            .GroupBy(node => (
                node.TryGetProperty<string>("Name", out string? name) ? name! : string.Empty,
                node.TryGetProperty<string>("ContainingTypeFullName", out string? containingType) ? containingType! : string.Empty))
            .ToDictionary(group => group.Key, group => group.ToList());

        foreach (CpgNode callNode in graph.GetNodes(CpgNodeKind.Call))
        {
            if (HasKnownMethodFullName(callNode))
            {
                continue;
            }

            CpgNode? receiverNode = GetReceiverNode(graph, callNode);
            if (receiverNode is null)
            {
                continue;
            }

            string methodName = GetRecoveredMethodName(graph, callNode);
            if (string.IsNullOrWhiteSpace(methodName))
            {
                continue;
            }

            string[] receiverTypes = MergeTypes(GetPossibleTypes(receiverNode), GetKnownTypes(receiverNode)).ToArray();
            if (receiverTypes.Length == 0 &&
                receiverNode.Kind == CpgNodeKind.Identifier &&
                declarationsByName.TryGetValue(GetNodeName(receiverNode), out List<CpgNode>? declarations) &&
                declarations.Count == 1)
            {
                receiverTypes = MergeTypes(GetPossibleTypes(declarations[0]), GetKnownTypes(declarations[0])).ToArray();
            }

            if (receiverTypes.Length == 0)
            {
                continue;
            }

            string[] hintedMethodFullNames = receiverTypes
                .SelectMany(typeFullName =>
                    methodsByNameAndType.TryGetValue((methodName, typeFullName), out List<CpgNode>? methods)
                        ? methods
                        : Enumerable.Empty<CpgNode>())
                .Select(methodNode => methodNode.TryGetProperty<string>("FullName", out string? fullName) ? fullName : null)
                .Where(static fullName => !string.IsNullOrWhiteSpace(fullName))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (hintedMethodFullNames.Length == 0)
            {
                continue;
            }

            callNode.SetProperty("DynamicTypeHintFullNames", hintedMethodFullNames);
        }
    }

    private static void PropagateArgumentTypesToParameters(CpgGraph graph)
    {
        Dictionary<string, CpgNode> methodsByFullName = graph
            .GetNodes(CpgNodeKind.Method)
            .Where(node => node.TryGetProperty<string>("FullName", out string? fullName) && !string.IsNullOrWhiteSpace(fullName))
            .GroupBy(node => node.TryGetProperty<string>("FullName", out string? fullName) ? fullName! : string.Empty)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (CpgNode callNode in graph.GetNodes(CpgNodeKind.Call))
        {
            CpgNode[] targetMethods = GetCandidateTargetMethods(callNode, methodsByFullName)
                .ToArray();
            if (targetMethods.Length == 0)
            {
                continue;
            }

            List<CpgNode> argumentNodes = GetCallArgumentNodes(graph, callNode);
            if (argumentNodes.Count == 0)
            {
                continue;
            }

            foreach (CpgNode methodNode in targetMethods)
            {
                CpgNode[] parameterNodes = graph.GetNodes(CpgNodeKind.MethodParameterIn)
                    .Where(node =>
                        node.TryGetProperty<long>("AstParentId", out long parentId) &&
                        parentId == methodNode.Id)
                    .OrderBy(node => node.TryGetProperty<int>("Index", out int index) ? index : int.MaxValue)
                    .ToArray();

                int pairCount = Math.Min(argumentNodes.Count, parameterNodes.Length);
                for (int index = 0; index < pairCount; index++)
                {
                    string[] argumentTypes = GetBestKnownTypes(graph, argumentNodes[index])
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    if (argumentTypes.Length == 0)
                    {
                        continue;
                    }

                    CpgNode parameterNode = parameterNodes[index];
                    parameterNode.SetProperty(
                        "PossibleTypes",
                        MergeTypes(GetPossibleTypes(parameterNode), argumentTypes).ToArray());
                }
            }
        }
    }

    private static void ResolveDummyPossibleTypes(CpgGraph graph)
    {
        // 这里做的是占位类型收敛：
        // 前面的步骤会先把类型信息尽量铺开，未知时再落成 dummy。
        // 当后续节点已经拿到真实类型后，需要把这些 dummy 回收掉，
        // 否则下游看到的类型集合会长期混着“真实类型 + 占位类型”。
        for (int iteration = 0; iteration < 4; iteration++)
        {
            bool changed = false;

            foreach (CpgNode node in graph.Nodes)
            {
                string[] currentTypes = GetPossibleTypes(node)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (currentTypes.Length == 0)
                {
                    continue;
                }

                string[] resolvedTypes = currentTypes
                    .SelectMany(type => ResolvePossibleType(graph, type))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                string[] normalizedTypes = NormalizePossibleTypes(resolvedTypes);

                if (currentTypes.SequenceEqual(normalizedTypes, StringComparer.Ordinal))
                {
                    continue;
                }

                node.SetProperty("PossibleTypes", normalizedTypes);
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            // 收敛后再做一轮传播，保证局部变量和标识符能吃到最新真实类型。
            PropagateAssignedTypes(graph);
            PropagateIdentifierTypes(graph);
        }
    }

    private static bool IsDeclarationLikeNode(CpgNode node)
    {
        return node.Kind is CpgNodeKind.Local
            or CpgNodeKind.Member
            or CpgNodeKind.MethodParameterIn;
    }

    private static bool HasKnownMethodFullName(CpgNode callNode)
    {
        return callNode.TryGetProperty<string>("MethodFullName", out string? fullName) &&
               !string.IsNullOrWhiteSpace(fullName) &&
               !string.Equals(fullName, "<unknown>", StringComparison.Ordinal);
    }

    private static CpgNode? GetReceiverNode(CpgGraph graph, CpgNode callNode)
    {
        if (IsImplicitSelfCall(graph, callNode))
        {
            return null;
        }

        List<CpgNode> astChildren = GetAstChildren(graph, callNode);

        string methodName = GetRecoveredMethodName(graph, callNode);
        return astChildren.FirstOrDefault(child =>
            child.Kind is CpgNodeKind.Identifier or CpgNodeKind.Call &&
            !string.Equals(GetNodeName(child), methodName, StringComparison.Ordinal));
    }

    private static bool IsImplicitSelfCall(CpgGraph graph, CpgNode callNode)
    {
        if (!callNode.TryGetProperty<string>("MethodFullName", out string? methodFullName) ||
            string.IsNullOrWhiteSpace(methodFullName) ||
            string.Equals(methodFullName, "<unknown>", StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryGetContainingTypeFullNameForMethod(callNode, out string? calledContainingType) ||
            string.IsNullOrWhiteSpace(calledContainingType))
        {
            return false;
        }

        CpgNode? enclosingMethodNode = GetEnclosingMethodNode(graph, callNode);
        if (enclosingMethodNode is null ||
            !enclosingMethodNode.TryGetProperty<string>("ContainingTypeFullName", out string? enclosingContainingType) ||
            string.IsNullOrWhiteSpace(enclosingContainingType))
        {
            return false;
        }

        return string.Equals(calledContainingType, enclosingContainingType, StringComparison.Ordinal);
    }

    private static CpgNode? GetEnclosingMethodNode(CpgGraph graph, CpgNode node)
    {
        long? currentId = node.Id;
        while (currentId is not null)
        {
            CpgNode currentNode = graph.GetNode(currentId.Value);
            if (currentNode.Kind == CpgNodeKind.Method)
            {
                return currentNode;
            }

            if (!currentNode.TryGetProperty<long>("AstParentId", out long parentId))
            {
                return null;
            }

            currentId = parentId;
        }

        return null;
    }

    private static bool TryGetContainingTypeFullNameForMethod(CpgNode callNode, out string? containingTypeFullName)
    {
        containingTypeFullName = null;

        if (!callNode.TryGetProperty<string>("MethodFullName", out string? methodFullName) ||
            string.IsNullOrWhiteSpace(methodFullName))
        {
            return false;
        }

        int signatureIndex = methodFullName.IndexOf('(');
        if (signatureIndex <= 0)
        {
            return false;
        }

        string methodPrefix = methodFullName[..signatureIndex];
        int separatorIndex = methodPrefix.LastIndexOf('.');
        if (separatorIndex <= 0)
        {
            return false;
        }

        containingTypeFullName = methodPrefix[..separatorIndex];
        return !string.IsNullOrWhiteSpace(containingTypeFullName);
    }

    private static List<CpgNode> GetCallArgumentNodes(CpgGraph graph, CpgNode callNode)
    {
        List<CpgNode> astChildren = GetAstChildren(graph, callNode);
        if (astChildren.Count == 0)
        {
            return new List<CpgNode>();
        }

        CpgNode? receiverNode = GetReceiverNode(graph, callNode);
        string methodName = GetRecoveredMethodName(graph, callNode);

        return astChildren
            .Where(node => receiverNode is null || node.Id != receiverNode.Id)
            .Where(node => node.Kind != CpgNodeKind.MethodRef)
            .Where(node =>
                node.Kind != CpgNodeKind.Identifier ||
                !string.Equals(GetNodeName(node), methodName, StringComparison.Ordinal))
            .ToList();
    }

    private static string GetRecoveredMethodName(CpgGraph graph, CpgNode callNode)
    {
        string callName = callNode.TryGetProperty<string>("Name", out string? name) ? name ?? string.Empty : string.Empty;
        if (!string.IsNullOrWhiteSpace(callName) && callName.Contains('.', StringComparison.Ordinal))
        {
            return callName.Split('.').Last();
        }

        if (!string.IsNullOrWhiteSpace(callName) && !callName.Contains('.', StringComparison.Ordinal))
        {
            return callName;
        }

        CpgNode? methodIdentifierNode = graph
            .GetOutgoingEdges(callNode.Id, CpgEdgeKind.Ast)
            .Select(edge => graph.GetNode(edge.TargetId))
            .FirstOrDefault(node =>
                node.Kind == CpgNodeKind.Identifier &&
                node.TryGetProperty<string>("Name", out string? identifierName) &&
                !string.IsNullOrWhiteSpace(identifierName) &&
                !string.Equals(identifierName, "this", StringComparison.Ordinal));

        return methodIdentifierNode is null ? callName : GetNodeName(methodIdentifierNode);
    }

    private static string GetNodeName(CpgNode node)
    {
        return node.TryGetProperty<string>("Name", out string? name) ? name ?? string.Empty : string.Empty;
    }

    private static string GetFieldName(CpgNode callNode)
    {
        if (callNode.TryGetProperty<string>("FieldFullName", out string? fieldFullName) &&
            !string.IsNullOrWhiteSpace(fieldFullName))
        {
            return fieldFullName.Split('.').Last();
        }

        return GetNodeName(callNode);
    }

    private static CpgNode? FindUniqueDeclarationByName(
        CpgNode identifierNode,
        IReadOnlyDictionary<string, List<CpgNode>> declarationsByName)
    {
        string name = GetNodeName(identifierNode);
        if (string.IsNullOrWhiteSpace(name) ||
            !declarationsByName.TryGetValue(name, out List<CpgNode>? declarations) ||
            declarations.Count != 1)
        {
            return null;
        }

        return declarations[0];
    }

    private static bool TryGetAssignedMemberAndValueNode(
        CpgGraph graph,
        CpgNode callNode,
        out CpgNode? memberNode,
        out CpgNode? valueNode)
    {
        memberNode = null;
        valueNode = null;

        if (!callNode.TryGetProperty<string>("Name", out string? callName) || string.IsNullOrWhiteSpace(callName))
        {
            return false;
        }

        List<CpgNode> astChildren = GetAstChildren(graph, callNode);
        if (astChildren.Count == 0)
        {
            return false;
        }

        if (string.Equals(callName, "=", StringComparison.Ordinal))
        {
            if (astChildren.Count < 2)
            {
                return false;
            }

            memberNode = ResolveReferencedMember(graph, astChildren[0]);
            valueNode = astChildren[1];
            return memberNode is not null;
        }

        if (!callName.StartsWith("set_", StringComparison.Ordinal))
        {
            return false;
        }

        string memberName = callName["set_".Length..];
        memberNode = ResolveSetterMember(graph, callNode, memberName);
        valueNode = astChildren.LastOrDefault();
        return memberNode is not null && valueNode is not null;
    }

    private static CpgNode? ResolveSetterMember(CpgGraph graph, CpgNode setterCallNode, string memberName)
    {
        if (!setterCallNode.TryGetProperty<string>("MethodFullName", out string? methodFullName) ||
            string.IsNullOrWhiteSpace(methodFullName) ||
            string.Equals(methodFullName, "<unknown>", StringComparison.Ordinal))
        {
            return ResolveSetterMemberByReceiverTypes(graph, setterCallNode, memberName);
        }

        int separatorIndex = methodFullName.LastIndexOf($".set_{memberName}(", StringComparison.Ordinal);
        if (separatorIndex <= 0)
        {
            return ResolveSetterMemberByReceiverTypes(graph, setterCallNode, memberName);
        }

        string expectedMemberFullName = $"{methodFullName[..separatorIndex]}.{memberName}";
        CpgNode? exactMatch = graph.GetNodes(CpgNodeKind.Member)
            .FirstOrDefault(node =>
                node.TryGetProperty<string>("FullName", out string? fullName) &&
                string.Equals(fullName, expectedMemberFullName, StringComparison.Ordinal));
        return exactMatch ?? ResolveSetterMemberByReceiverTypes(graph, setterCallNode, memberName);
    }

    private static CpgNode? ResolveSetterMemberByReceiverTypes(CpgGraph graph, CpgNode setterCallNode, string memberName)
    {
        CpgNode? receiverNode = GetReceiverNode(graph, setterCallNode);
        if (receiverNode is null)
        {
            return null;
        }

        string[] receiverTypes = GetBestKnownTypes(graph, receiverNode)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (receiverTypes.Length == 0 &&
            receiverNode.Kind == CpgNodeKind.Identifier)
        {
            receiverTypes = ResolveReceiverTypes(graph, GetNodeName(receiverNode));
        }

        if (receiverTypes.Length == 0)
        {
            return null;
        }

        return graph.GetNodes(CpgNodeKind.Member)
            .FirstOrDefault(node =>
                node.TryGetProperty<string>("Name", out string? currentMemberName) &&
                string.Equals(currentMemberName, memberName, StringComparison.Ordinal) &&
                node.TryGetProperty<string>("FullName", out string? fullName) &&
                receiverTypes.Any(typeName => string.Equals(fullName, $"{typeName}.{memberName}", StringComparison.Ordinal)));
    }

    private static CpgNode? ResolveReferencedMember(CpgGraph graph, CpgNode node)
    {
        if (node.Kind == CpgNodeKind.Member)
        {
            return node;
        }

        return graph.GetOutgoingEdges(node.Id, CpgEdgeKind.Ref)
            .Select(edge => graph.GetNode(edge.TargetId))
            .FirstOrDefault(target => target.Kind == CpgNodeKind.Member);
    }

    private static IEnumerable<CpgNode> ResolveDeclarationCandidates(
        CpgGraph graph,
        CpgNode identifierNode,
        IReadOnlyDictionary<string, List<CpgNode>> declarationsByName)
    {
        List<CpgNode> refTargets = graph.GetOutgoingEdges(identifierNode.Id, CpgEdgeKind.Ref)
            .Select(edge => graph.GetNode(edge.TargetId))
            .Where(IsDeclarationLikeNode)
            .ToList();
        if (refTargets.Count > 0)
        {
            return refTargets;
        }

        CpgNode? declarationNode = FindUniqueDeclarationByName(identifierNode, declarationsByName);
        return declarationNode is null ? Array.Empty<CpgNode>() : new[] { declarationNode };
    }

    private static IEnumerable<string> GetAliasMethodFullNames(CpgGraph graph, CpgNode declarationNode)
    {
        HashSet<string> aliasSet = new(StringComparer.Ordinal);

        if (declarationNode.TryGetProperty<string[]>("AliasMethodFullNames", out string[]? aliasArray))
        {
            foreach (string alias in aliasArray.Where(alias => !string.IsNullOrWhiteSpace(alias)))
            {
                _ = aliasSet.Add(alias);
            }
        }
        else if (declarationNode.TryGetProperty<IReadOnlyCollection<string>>("AliasMethodFullNames", out IReadOnlyCollection<string>? collection))
        {
            foreach (string alias in collection.Where(alias => !string.IsNullOrWhiteSpace(alias)))
            {
                _ = aliasSet.Add(alias);
            }
        }

        foreach (CpgNode methodRefNode in GetAstDescendants(graph, declarationNode).Where(node => node.Kind == CpgNodeKind.MethodRef))
        {
            if (methodRefNode.TryGetProperty<string>("MethodFullName", out string? methodFullName) &&
                !string.IsNullOrWhiteSpace(methodFullName))
            {
                _ = aliasSet.Add(methodFullName);
            }
        }

        return aliasSet;
    }

    private static List<CpgNode> GetAstChildren(CpgGraph graph, CpgNode parentNode)
    {
        return graph.GetOutgoingEdges(parentNode.Id, CpgEdgeKind.Ast)
            .Select(edge => graph.GetNode(edge.TargetId))
            .ToList();
    }

    private static IEnumerable<CpgNode> GetAstDescendants(CpgGraph graph, CpgNode rootNode)
    {
        Stack<CpgNode> stack = new(GetAstChildren(graph, rootNode).AsEnumerable().Reverse());

        while (stack.Count > 0)
        {
            CpgNode currentNode = stack.Pop();
            yield return currentNode;

            foreach (CpgNode childNode in GetAstChildren(graph, currentNode).AsEnumerable().Reverse())
            {
                stack.Push(childNode);
            }
        }
    }

    private static IEnumerable<string> GetKnownTypes(CpgNode node)
    {
        if (node.TryGetProperty<string>("TypeFullName", out string? typeFullName) &&
            !string.IsNullOrWhiteSpace(typeFullName) &&
            !IsUnknownLikeType(typeFullName))
        {
            yield return typeFullName;
        }
    }

    private static IEnumerable<string> GetBestKnownTypes(CpgGraph graph, CpgNode node)
    {
        string[] possibleTypes = NormalizePossibleTypes(
                MergeTypes(GetPossibleTypes(node), GetKnownTypes(node)))
            .ToArray();
        if (possibleTypes.Length > 0)
        {
            return possibleTypes;
        }

        if (node.Kind == CpgNodeKind.Call &&
            node.TryGetProperty<string>("Name", out string? name) &&
            string.Equals(name, ".", StringComparison.Ordinal))
        {
            CpgNode? memberNode = graph.GetOutgoingEdges(node.Id, CpgEdgeKind.Ref)
                .Select(edge => graph.GetNode(edge.TargetId))
                .FirstOrDefault(target => target.Kind == CpgNodeKind.Member);
            if (memberNode is not null)
            {
                string[] memberTypes = MergeTypes(GetPossibleTypes(memberNode), GetKnownTypes(memberNode)).ToArray();
                if (memberTypes.Length > 0)
                {
                    return memberTypes;
                }
            }
        }

        return NormalizePossibleTypes(GetKnownTypes(node)).ToArray();
    }

    private static IEnumerable<string> GetPossibleTypes(CpgNode node)
    {
        if (node.TryGetProperty<string[]>("PossibleTypes", out string[]? arrayValue))
        {
            foreach (string value in arrayValue.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                yield return value;
            }
        }
        else if (node.TryGetProperty<IReadOnlyCollection<string>>("PossibleTypes", out IReadOnlyCollection<string>? collectionValue))
        {
            foreach (string value in collectionValue.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<string> GetHintedMethodFullNames(CpgNode callNode)
    {
        if (callNode.TryGetProperty<string[]>("DynamicTypeHintFullNames", out string[]? arrayValue))
        {
            return arrayValue.Where(value => !string.IsNullOrWhiteSpace(value));
        }

        if (callNode.TryGetProperty<IReadOnlyCollection<string>>("DynamicTypeHintFullNames", out IReadOnlyCollection<string>? collectionValue))
        {
            return collectionValue.Where(value => !string.IsNullOrWhiteSpace(value));
        }

        return Array.Empty<string>();
    }

    private static IEnumerable<string> MergeTypes(IEnumerable<string> first, IEnumerable<string> second)
    {
        return first
            .Concat(second)
            .Where(type => !string.IsNullOrWhiteSpace(type) && !IsUnknownLikeType(type))
            .Distinct(StringComparer.Ordinal);
    }

    private static IEnumerable<string> ResolvePossibleType(CpgGraph graph, string typeFullName)
    {
        if (string.IsNullOrWhiteSpace(typeFullName) || !IsDummyType(typeFullName))
        {
            return string.IsNullOrWhiteSpace(typeFullName)
                ? Array.Empty<string>()
                : new[] { typeFullName };
        }

        if (TryResolveDummyMemberType(graph, typeFullName, out string[]? memberTypes))
        {
            return memberTypes;
        }

        if (TryResolveDummyIndexType(graph, typeFullName, out string[]? indexTypes))
        {
            return indexTypes;
        }

        if (TryResolveDummyReturnType(graph, typeFullName, out string[]? returnTypes))
        {
            return returnTypes;
        }

        return new[] { typeFullName };
    }

    private static string[] NormalizePossibleTypes(IEnumerable<string> types)
    {
        string[] normalizedTypes = types
            .Where(type => !string.IsNullOrWhiteSpace(type) && !IsUnknownLikeType(type))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalizedTypes.Length == 0)
        {
            return Array.Empty<string>();
        }

        string[] concreteTypes = normalizedTypes
            .Where(type => !IsDummyType(type))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return concreteTypes.Length > 0 ? concreteTypes : normalizedTypes;
    }

    private static bool TryResolveDummyMemberType(
        CpgGraph graph,
        string dummyTypeFullName,
        out string[]? resolvedTypes)
    {
        resolvedTypes = null;

        int markerIndex = dummyTypeFullName.IndexOf($".{DummyMemberLoad}(", StringComparison.Ordinal);
        if (markerIndex <= 0 || !dummyTypeFullName.EndsWith(')'))
        {
            return false;
        }

        string receiverToken = dummyTypeFullName[..markerIndex];
        string memberName = dummyTypeFullName[(markerIndex + DummyMemberLoad.Length + 2)..^1];
        if (string.IsNullOrWhiteSpace(receiverToken) || string.IsNullOrWhiteSpace(memberName))
        {
            return false;
        }

        string[] receiverTypes = ResolveReceiverTypes(graph, receiverToken);
        if (receiverTypes.Length == 0)
        {
            return false;
        }

        resolvedTypes = receiverTypes
            .SelectMany(typeName => graph.GetNodes(CpgNodeKind.Member)
                .Where(node =>
                    node.TryGetProperty<string>("FullName", out string? fullName) &&
                    string.Equals(fullName, $"{typeName}.{memberName}", StringComparison.Ordinal))
                .SelectMany(node => MergeTypes(GetPossibleTypes(node), GetKnownTypes(node))))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return resolvedTypes.Length > 0;
    }

    private static bool TryResolveDummyIndexType(
        CpgGraph graph,
        string dummyTypeFullName,
        out string[]? resolvedTypes)
    {
        resolvedTypes = null;

        if (!dummyTypeFullName.EndsWith($".{DummyIndexAccess}", StringComparison.Ordinal))
        {
            return false;
        }

        string receiverToken = dummyTypeFullName[..^(DummyIndexAccess.Length + 1)];
        if (string.IsNullOrWhiteSpace(receiverToken))
        {
            return false;
        }

        resolvedTypes = ResolveReceiverTypes(graph, receiverToken)
            .SelectMany(GetIndexElementTypes)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return resolvedTypes.Length > 0;
    }

    private static bool TryResolveDummyReturnType(
        CpgGraph graph,
        string dummyTypeFullName,
        out string[]? resolvedTypes)
    {
        resolvedTypes = null;

        if (!dummyTypeFullName.EndsWith($".{DummyReturnType}", StringComparison.Ordinal))
        {
            return false;
        }

        string callName = dummyTypeFullName[..^(DummyReturnType.Length + 1)];
        if (string.IsNullOrWhiteSpace(callName))
        {
            return false;
        }

        resolvedTypes = graph.GetNodes(CpgNodeKind.Method)
            .Where(node =>
                node.TryGetProperty<string>("Name", out string? methodName) &&
                string.Equals(methodName, callName, StringComparison.Ordinal))
            .SelectMany(methodNode => graph.GetNodes(CpgNodeKind.MethodReturn)
                .Where(node =>
                    node.TryGetProperty<long>("AstParentId", out long parentId) &&
                    parentId == methodNode.Id)
                .SelectMany(node => MergeTypes(GetPossibleTypes(node), GetKnownTypes(node))))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return resolvedTypes.Length > 0;
    }

    private static string[] ResolveReceiverTypes(CpgGraph graph, string receiverToken)
    {
        string[] directTypes = IsDummyType(receiverToken)
            ? Array.Empty<string>()
            : new[] { receiverToken };
        string[] declarationTypes = graph.Nodes
            .Where(IsDeclarationLikeNode)
            .Where(node =>
                node.TryGetProperty<string>("Name", out string? name) &&
                string.Equals(name, receiverToken, StringComparison.Ordinal))
            .SelectMany(node => NormalizePossibleTypes(MergeTypes(GetPossibleTypes(node), GetKnownTypes(node))))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return NormalizePossibleTypes(directTypes.Concat(declarationTypes));
    }

    private static bool IsDummyType(string typeFullName)
    {
        return typeFullName.Contains($".{DummyReturnType}", StringComparison.Ordinal) ||
               typeFullName.Contains($".{DummyMemberLoad}(", StringComparison.Ordinal) ||
               typeFullName.EndsWith($".{DummyIndexAccess}", StringComparison.Ordinal);
    }

    private static IEnumerable<string> GetIndexElementTypes(string collectionType)
    {
        if (string.IsNullOrWhiteSpace(collectionType))
        {
            yield break;
        }

        if (collectionType.EndsWith("[]", StringComparison.Ordinal))
        {
            yield return collectionType[..^2];
            yield break;
        }

        int genericStart = collectionType.IndexOf('<');
        int genericEnd = collectionType.LastIndexOf('>');
        if (genericStart > 0 && genericEnd > genericStart)
        {
            string genericBody = collectionType[(genericStart + 1)..genericEnd];
            string[] genericArguments = genericBody
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (genericArguments.Length > 0)
            {
                yield return genericArguments[^1];
            }
        }
    }

    private static bool IsUnknownLikeType(string typeFullName)
    {
        return string.Equals(typeFullName, "dynamic", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(typeFullName, "<unknown>", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(typeFullName, "ANY", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDescendantOfMethod(CpgGraph graph, CpgNode node, long methodId)
    {
        long? currentId = node.Id;
        while (currentId is not null)
        {
            CpgNode currentNode = graph.GetNode(currentId.Value);
            if (currentNode.Id == methodId)
            {
                return true;
            }

            if (!currentNode.TryGetProperty<long>("AstParentId", out long parentId))
            {
                return false;
            }

            currentId = parentId;
        }

        return false;
    }
}
