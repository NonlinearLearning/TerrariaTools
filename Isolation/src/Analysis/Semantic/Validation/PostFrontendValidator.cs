using Analysis.Core;

namespace Analysis.Semantic.Validation;

/// <summary>
/// 在前端和基础语义 pass 完成后，对图执行一致性校验。
/// </summary>
public static class PostFrontendValidator
{
    /// <summary>
    /// 执行图校验并返回所有违规项。
    /// </summary>
    /// <param name="graph">待校验图。</param>
    /// <returns>违规项集合。</returns>
    public static IReadOnlyList<ValidationViolation> Validate(CpgGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        List<ValidationViolation> violations = new();
        CheckFullNameUniqueness(graph, CpgNodeKind.Method, "FULLNAME_UNIQUE_METHOD", violations);
        CheckFullNameUniqueness(graph, CpgNodeKind.Type, "FULLNAME_UNIQUE_TYPE", violations);
        CheckFullNameUniqueness(graph, CpgNodeKind.TypeDecl, "FULLNAME_UNIQUE_TYPEDECL", violations);
        CheckIdentifierRefs(graph, violations);
        CheckAstIncoming(graph, violations);
        return violations;
    }

    /// <summary>
    /// 执行图校验，并在存在达到指定严重级别的违规项时抛出异常。
    /// </summary>
    /// <param name="graph">待校验图。</param>
    /// <param name="fatalLevel">致命级别阈值。</param>
    public static void ValidateOrThrow(CpgGraph graph, ValidationLevel fatalLevel)
    {
        IReadOnlyList<ValidationViolation> violations = Validate(graph);
        List<ValidationViolation> fatalViolations = violations
            .Where(violation => violation.Level <= fatalLevel)
            .ToList();

        if (fatalViolations.Count == 0)
        {
            return;
        }

        string message = string.Join(
            Environment.NewLine,
            fatalViolations.Select(violation => $"{violation.Code}: {violation.Message}"));
        throw new ValidationError(message);
    }

    private static void CheckFullNameUniqueness(
        CpgGraph graph,
        CpgNodeKind kind,
        string code,
        ICollection<ValidationViolation> violations)
    {
        foreach (IGrouping<string, CpgNode> group in graph.GetNodes(kind)
                     .Where(node => node.TryGetProperty<string>("FullName", out string? fullName) &&
                                    !string.IsNullOrWhiteSpace(fullName))
                     .GroupBy(node => node.TryGetProperty<string>("FullName", out string? fullName) ? fullName! : string.Empty,
                         StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            violations.Add(new ValidationViolation(
                code,
                ValidationLevel.V1,
                $"节点类型 {kind} 的 FullName '{group.Key}' 重复出现 {group.Count()} 次。"));
        }
    }

    private static void CheckIdentifierRefs(CpgGraph graph, ICollection<ValidationViolation> violations)
    {
        foreach (CpgNode identifierNode in graph.GetNodes(CpgNodeKind.Identifier))
        {
            List<CpgNode> refTargets = graph.GetOutgoingEdges(identifierNode.Id, CpgEdgeKind.Ref)
                .GroupBy(edge => edge.TargetId)
                .Select(group => graph.GetNode(group.Key))
                .ToList();

            if (refTargets.Count > 1)
            {
                violations.Add(new ValidationViolation(
                    "MULTI_REF",
                    ValidationLevel.V1,
                    $"标识符节点 {identifierNode.Id} 存在多个 REF 目标。"));
            }

            foreach (CpgNode targetNode in refTargets)
            {
                if (!IsValidRefTarget(targetNode.Kind))
                {
                    violations.Add(new ValidationViolation(
                        "BAD_REF_TYPE",
                        ValidationLevel.V1,
                        $"标识符节点 {identifierNode.Id} 指向了非法 REF 目标类型 {targetNode.Kind}。"));
                }

                if (NeedsLocalScopeCheck(targetNode.Kind))
                {
                    long? sourceMethodId = FindEnclosingMethodId(graph, identifierNode.Id);
                    long? targetMethodId = FindEnclosingMethodId(graph, targetNode.Id);

                    if (sourceMethodId.HasValue &&
                        targetMethodId.HasValue &&
                        sourceMethodId.Value != targetMethodId.Value)
                    {
                        violations.Add(new ValidationViolation(
                            "NONLOCAL_REF",
                            ValidationLevel.V1,
                            $"标识符节点 {identifierNode.Id} 跨方法绑定到局部声明节点 {targetNode.Id}。"));
                    }
                }
            }
        }
    }

    private static void CheckAstIncoming(CpgGraph graph, ICollection<ValidationViolation> violations)
    {
        foreach (CpgNode node in graph.Nodes)
        {
            int incomingAstCount = graph.GetIncomingEdges(node.Id, CpgEdgeKind.Ast).Count();
            if (incomingAstCount > 1)
            {
                violations.Add(new ValidationViolation(
                    "MULTI_AST_IN",
                    ValidationLevel.V1,
                    $"节点 {node.Id} 存在多个 AST 入边。"));
            }
        }
    }

    private static bool IsValidRefTarget(CpgNodeKind kind)
    {
        return kind is CpgNodeKind.Local or
            CpgNodeKind.MethodParameterIn or
            CpgNodeKind.Member or
            CpgNodeKind.Method or
            CpgNodeKind.TypeDecl;
    }

    private static bool NeedsLocalScopeCheck(CpgNodeKind kind)
    {
        return kind is CpgNodeKind.Local or CpgNodeKind.MethodParameterIn;
    }

    private static long? FindEnclosingMethodId(CpgGraph graph, long nodeId)
    {
        long currentId = nodeId;
        HashSet<long> visited = new();

        while (visited.Add(currentId))
        {
            CpgNode currentNode = graph.GetNode(currentId);
            if (currentNode.Kind == CpgNodeKind.Method)
            {
                return currentId;
            }

            CpgEdge? astIn = graph.GetIncomingEdges(currentId, CpgEdgeKind.Ast).FirstOrDefault();
            if (astIn is not null)
            {
                currentId = astIn.SourceId;
                continue;
            }

            if (currentNode.TryGetProperty<long>("AstParentId", out long astParentId))
            {
                currentId = astParentId;
                continue;
            }

            break;
        }

        return null;
    }
}
