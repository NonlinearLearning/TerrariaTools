using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Dome.Rewrite.Roslyn;

using TerrariaTools.Dome.Core;

/// <summary>
/// Roslyn 重写执行器。
/// </summary>
public sealed class RoslynRewriteExecutor
{
    /// <summary>
    /// 异步执行重写计划。
    /// </summary>
    /// <param name="source">源代码。</param>
    /// <param name="plan">审计计划。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>重写执行结果。</returns>
    public Task<RewriteExecutionResult> ExecuteAsync(string source, AuditPlan plan, CancellationToken cancellationToken)
    {
        if (plan.Conflicts.Count > 0)
        {
            return Task.FromResult(RewriteExecutionResult.Failure("Rewrite cannot execute a plan with unresolved conflicts."));
        }

        var tree = CSharpSyntaxTree.ParseText(source);
        SyntaxNode root = tree.GetCompilationUnitRoot(cancellationToken);
        var orderedChanges = plan.Changes
            .OrderBy(change => change.Target.DocumentPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(change => change.Target.MemberId.Value, StringComparer.Ordinal)
            .ThenByDescending(change => change.Target.SpanStart)
            .ThenBy(change => change.ExecutionOrder)
            .ToArray();

        foreach (var change in orderedChanges)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var resolution = FindTargetNode(root, change.Target);
            if (!resolution.IsSuccess || resolution.Node == null)
            {
                return Task.FromResult(RewriteExecutionResult.Failure(resolution.Message));
            }

            var applyResult = ApplyChange(root, resolution.Node, change);
            if (!applyResult.IsSuccess || applyResult.Root == null)
            {
                return Task.FromResult(RewriteExecutionResult.Failure(applyResult.Message));
            }

            root = applyResult.Root;
        }

        return Task.FromResult(RewriteExecutionResult.Success(root.NormalizeWhitespace().ToFullString()));
    }

    /// <summary>
    /// 查找目标节点。
    /// </summary>
    /// <param name="root">语法根节点。</param>
    /// <param name="target">计划目标。</param>
    /// <returns>目标解析结果。</returns>
    private static TargetResolutionResult FindTargetNode(SyntaxNode root, PlanTarget target)
    {
        if (target.TargetKind == TargetKind.Class)
        {
            var classCandidates = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Where(@class => ClassMatches(@class, target.MemberId.Value))
                .Cast<SyntaxNode>()
                .ToArray();

            if (classCandidates.Length == 0)
            {
                return TargetResolutionResult.Failure($"Target '{target.TargetKey}' could not be resolved during rewrite because the class '{target.MemberId.Value}' was not found.");
            }

            if (classCandidates.Length == 1)
            {
                return TargetResolutionResult.Success(classCandidates[0]);
            }

            var classSpanCandidates = classCandidates
                .Where(node => node.SpanStart == target.SpanStart && node.Span.Length == target.SpanLength)
                .ToArray();

            if (classSpanCandidates.Length == 0)
            {
                return TargetResolutionResult.Failure($"Target '{target.TargetKey}' could not be resolved during rewrite because the span did not match the class '{target.MemberId.Value}'.");
            }

            var classTextMatch = classSpanCandidates.FirstOrDefault(node => string.Equals(node.ToString().Trim(), target.DisplayText, StringComparison.Ordinal));
            if (classTextMatch == null)
            {
                return TargetResolutionResult.Failure($"Target '{target.TargetKey}' could not be resolved during rewrite because the class text did not match '{target.DisplayText}'.");
            }

            return TargetResolutionResult.Success(classTextMatch);
        }

        if (target.TargetKind == TargetKind.Method)
        {
            var methodCandidates = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(method => MemberMatches(method, target.MemberId.Value))
                .Cast<SyntaxNode>()
                .ToArray();

            if (methodCandidates.Length == 0)
            {
                return TargetResolutionResult.Failure($"Target '{target.TargetKey}' could not be resolved during rewrite because the member '{target.MemberId.Value}' was not found.");
            }

            if (methodCandidates.Length == 1)
            {
                return TargetResolutionResult.Success(methodCandidates[0]);
            }

            var methodSpanCandidates = methodCandidates
                .Where(node => node.SpanStart == target.SpanStart && node.Span.Length == target.SpanLength)
                .ToArray();

            if (methodSpanCandidates.Length == 0)
            {
                return TargetResolutionResult.Failure($"Target '{target.TargetKey}' could not be resolved during rewrite because the span did not match the method '{target.MemberId.Value}'.");
            }

            var methodTextMatch = methodSpanCandidates.FirstOrDefault(node => string.Equals(node.ToString().Trim(), target.DisplayText, StringComparison.Ordinal));
            if (methodTextMatch == null)
            {
                return TargetResolutionResult.Failure($"Target '{target.TargetKey}' could not be resolved during rewrite because the method text did not match '{target.DisplayText}'.");
            }

            return TargetResolutionResult.Success(methodTextMatch);
        }

        var memberCandidates = root.DescendantNodes()
            .OfType<StatementSyntax>()
            .Where(statement => MemberMatches(statement, target.MemberId.Value))
            .ToArray();

        if (memberCandidates.Length == 0)
        {
            return TargetResolutionResult.Failure($"Target '{target.TargetKey}' could not be resolved during rewrite because the member '{target.MemberId.Value}' was not found.");
        }

        var spanCandidates = memberCandidates
            .Where(statement => statement.SpanStart == target.SpanStart && statement.Span.Length == target.SpanLength)
            .ToArray();

        if (spanCandidates.Length == 0)
        {
            return TargetResolutionResult.Failure($"Target '{target.TargetKey}' could not be resolved during rewrite because the span did not match any statement in member '{target.MemberId.Value}'.");
        }

        var textMatch = spanCandidates
            .FirstOrDefault(statement => string.Equals(statement.ToString().Trim(), target.DisplayText, StringComparison.Ordinal));

        if (textMatch == null)
        {
            return TargetResolutionResult.Failure($"Target '{target.TargetKey}' could not be resolved during rewrite because the statement text did not match '{target.DisplayText}'.");
        }

        return TargetResolutionResult.Success(textMatch);
    }

    /// <summary>
    /// 检查语句是否匹配成员 ID。
    /// </summary>
    /// <param name="statement">语句语法节点。</param>
    /// <param name="memberId">成员 ID。</param>
    /// <returns>如果匹配则返回 true，否则返回 false。</returns>
    private static bool MemberMatches(StatementSyntax statement, string memberId)
    {
        var method = statement.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault();
        if (method != null)
        {
            var identifier = method switch
            {
                MethodDeclarationSyntax methodDeclaration => methodDeclaration.Identifier.Text,
                ConstructorDeclarationSyntax ctor => ctor.Identifier.Text,
                _ => null
            };

            return identifier != null && memberId.Contains(identifier, StringComparison.Ordinal);
        }

        var accessor = statement.Ancestors().OfType<AccessorDeclarationSyntax>().FirstOrDefault();
        return accessor == null || memberId.EndsWith(accessor.Keyword.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// 检查方法是否匹配成员 ID。
    /// </summary>
    /// <param name="method">方法声明语法节点。</param>
    /// <param name="memberId">成员 ID。</param>
    /// <returns>如果匹配则返回 true，否则返回 false。</returns>
    private static bool MemberMatches(MethodDeclarationSyntax method, string memberId)
    {
        return string.Equals(memberId, $"{GetContainingTypeName(method)}.{method.Identifier.Text}({BuildParameterList(method.ParameterList)})", StringComparison.Ordinal);
    }

    /// <summary>
    /// 检查类是否匹配类 ID。
    /// </summary>
    /// <param name="classDeclaration">类声明语法节点。</param>
    /// <param name="classId">类 ID。</param>
    /// <returns>如果匹配则返回 true，否则返回 false。</returns>
    private static bool ClassMatches(ClassDeclarationSyntax classDeclaration, string classId)
    {
        return string.Equals(classId, GetContainingTypeName(classDeclaration), StringComparison.Ordinal);
    }

    /// <summary>
    /// 获取包含类型名称。
    /// </summary>
    /// <param name="member">成员声明语法节点。</param>
    /// <returns>包含类型名称。</returns>
    private static string GetContainingTypeName(MemberDeclarationSyntax member)
    {
        if (member is TypeDeclarationSyntax typeDeclaration)
        {
            var containingTypes = typeDeclaration.Ancestors().OfType<TypeDeclarationSyntax>().Reverse().Select(type => type.Identifier.Text);
            var classNamespaces = member.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(ns => ns.Name.ToString());
            var segments = classNamespaces.Concat(containingTypes).Append(typeDeclaration.Identifier.Text);
            return string.Join(".", segments.Where(segment => !string.IsNullOrWhiteSpace(segment)));
        }

        var type = member.Ancestors().OfType<TypeDeclarationSyntax>().First();
        var namespaces = member.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(ns => ns.Name.ToString());
        var namespacePrefix = string.Join(".", namespaces);
        return string.IsNullOrEmpty(namespacePrefix) ? type.Identifier.Text : $"{namespacePrefix}.{type.Identifier.Text}";
    }

    /// <summary>
    /// 构建参数列表字符串。
    /// </summary>
    /// <param name="parameterList">参数列表语法节点。</param>
    /// <returns>参数列表字符串。</returns>
    private static string BuildParameterList(ParameterListSyntax parameterList)
    {
        return string.Join(", ", parameterList.Parameters.Select(parameter => parameter.Type?.ToString() ?? "object"));
    }

    /// <summary>
    /// 注释掉语句。
    /// </summary>
    /// <param name="statement">语句语法节点。</param>
    /// <param name="change">计划更改。</param>
    /// <returns>注释后的语句语法节点。</returns>
    private static StatementSyntax CommentOut(StatementSyntax statement, PlannedChange change)
    {
        return SyntaxFactory.EmptyStatement()
            .WithLeadingTrivia(
                SyntaxFactory.Comment($"// {change.Reason.RuleId}: {statement.ToString().Trim()}"),
                SyntaxFactory.CarriageReturnLineFeed);
    }

    /// <summary>
    /// 使用默认值替换语句。
    /// </summary>
    /// <param name="statement">语句语法节点。</param>
    /// <param name="change">计划更改。</param>
    /// <returns>替换后的语句语法节点。</returns>
    private static StatementSyntax ReplaceWithDefault(StatementSyntax statement, PlannedChange change)
    {
        if (statement is not ExpressionStatementSyntax expressionStatement ||
            expressionStatement.Expression is not AssignmentExpressionSyntax assignment)
        {
            throw new InvalidOperationException($"Action '{PlanActionKind.ReplaceWithDefault}' is unsupported for target '{change.Target.TargetKey}' because the statement is not an assignment.");
        }

        var payload = change.Action.Payload ?? "default";
        var expression = SyntaxFactory.ParseExpression(payload);
        return statement.ReplaceNode(assignment.Right, expression);
    }

    /// <summary>
    /// 添加返回语句。
    /// </summary>
    /// <param name="statement">语句语法节点。</param>
    /// <param name="change">计划更改。</param>
    /// <returns>添加返回语句后的语句语法节点。</returns>
    private static StatementSyntax AddReturn(StatementSyntax statement, PlannedChange change)
    {
        var payload = change.Action.Payload;
        var returnStatement = string.IsNullOrWhiteSpace(payload)
            ? "return;"
            : $"return {payload};";
        return SyntaxFactory.ParseStatement(returnStatement)
            .WithTriviaFrom(statement);
    }

    /// <summary>
    /// 应用更改。
    /// </summary>
    /// <param name="root">语法根节点。</param>
    /// <param name="node">目标节点。</param>
    /// <param name="change">计划更改。</param>
    /// <returns>更改应用结果。</returns>
    private static ApplyChangeResult ApplyChange(SyntaxNode root, SyntaxNode node, PlannedChange change)
    {
        if (change.Target.TargetKind == TargetKind.Class)
        {
            if (node is not ClassDeclarationSyntax)
            {
                return ApplyChangeResult.Failure($"Action '{change.Action.Kind}' is unsupported for target '{change.Target.TargetKey}' because the target is not a class.");
            }

            try
            {
                var updatedRoot = change.Action.Kind switch
                {
                    PlanActionKind.Delete => root.RemoveNode(node, SyntaxRemoveOptions.KeepNoTrivia)
                                                 ?? throw new InvalidOperationException($"Action '{PlanActionKind.Delete}' invalidated target '{change.Target.TargetKey}'."),
                    _ => throw new InvalidOperationException($"Action '{change.Action.Kind}' is unsupported for class target '{change.Target.TargetKey}'.")
                };

                return ApplyChangeResult.Success(updatedRoot);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return ApplyChangeResult.Failure(ex.Message);
            }
        }

        if (change.Target.TargetKind == TargetKind.Method)
        {
            if (node is not MethodDeclarationSyntax methodNode)
            {
                return ApplyChangeResult.Failure($"Action '{change.Action.Kind}' is unsupported for target '{change.Target.TargetKey}' because the target is not a method.");
            }

            try
            {
                var updatedRoot = change.Action.Kind switch
                {
                    PlanActionKind.Delete => root.RemoveNode(node, SyntaxRemoveOptions.KeepNoTrivia)
                                                 ?? throw new InvalidOperationException($"Action '{PlanActionKind.Delete}' invalidated target '{change.Target.TargetKey}'."),
                    PlanActionKind.AddReturn => root.ReplaceNode(node, AddReturn(methodNode, change)),
                    _ => throw new InvalidOperationException($"Action '{change.Action.Kind}' is unsupported for method target '{change.Target.TargetKey}'.")
                };

                return ApplyChangeResult.Success(updatedRoot);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return ApplyChangeResult.Failure(ex.Message);
            }
        }

        if (node is not StatementSyntax statementNode)
        {
            return ApplyChangeResult.Failure($"Action '{change.Action.Kind}' is unsupported for target '{change.Target.TargetKey}' because the target is not a statement.");
        }

        try
        {
            var updatedRoot = change.Action.Kind switch
            {
                PlanActionKind.Delete => root.RemoveNode(node, SyntaxRemoveOptions.KeepNoTrivia)
                                             ?? throw new InvalidOperationException($"Action '{PlanActionKind.Delete}' invalidated target '{change.Target.TargetKey}'."),
                PlanActionKind.CommentOut => root.ReplaceNode(node, CommentOut(statementNode, change)),
                PlanActionKind.ReplaceWithDefault => root.ReplaceNode(node, ReplaceWithDefault(statementNode, change)),
                PlanActionKind.AddReturn => root.ReplaceNode(node, AddReturn(statementNode, change)),
                _ => throw new InvalidOperationException($"Action '{change.Action.Kind}' is unsupported for target '{change.Target.TargetKey}'.")
            };

            return ApplyChangeResult.Success(updatedRoot);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return ApplyChangeResult.Failure(ex.Message);
        }
    }

    /// <summary>
    /// 为方法添加返回语句。
    /// </summary>
    /// <param name="method">方法声明语法节点。</param>
    /// <param name="change">计划更改。</param>
    /// <returns>添加返回语句后的方法声明语法节点。</returns>
    private static MethodDeclarationSyntax AddReturn(MethodDeclarationSyntax method, PlannedChange change)
    {
        if (method.Body == null)
        {
            throw new InvalidOperationException($"Action '{PlanActionKind.AddReturn}' is unsupported for target '{change.Target.TargetKey}' because the method has no block body.");
        }

        if (method.Body.Statements.Count > 0)
        {
            throw new InvalidOperationException($"Action '{PlanActionKind.AddReturn}' is unsupported for target '{change.Target.TargetKey}' because the method body is not empty.");
        }

        var payload = change.Action.Payload;
        var returnStatement = string.IsNullOrWhiteSpace(payload)
            ? "return;"
            : $"return {payload};";
        return method.WithBody(
            method.Body.WithStatements(
                SyntaxFactory.SingletonList<StatementSyntax>(SyntaxFactory.ParseStatement(returnStatement))));
    }

    /// <summary>
    /// 目标解析结果记录。
    /// </summary>
    private sealed record TargetResolutionResult(bool IsSuccess, SyntaxNode? Node, string Message)
    {
        /// <summary>
        /// 创建成功结果。
        /// </summary>
        public static TargetResolutionResult Success(SyntaxNode node) => new(true, node, string.Empty);

        /// <summary>
        /// 创建失败结果。
        /// </summary>
        public static TargetResolutionResult Failure(string message) => new(false, null, message);
    }

    /// <summary>
    /// 更改应用结果记录。
    /// </summary>
    private sealed record ApplyChangeResult(bool IsSuccess, SyntaxNode? Root, string Message)
    {
        /// <summary>
        /// 创建成功结果。
        /// </summary>
        public static ApplyChangeResult Success(SyntaxNode root) => new(true, root, string.Empty);

        /// <summary>
        /// 创建失败结果。
        /// </summary>
        public static ApplyChangeResult Failure(string message) => new(false, null, message);
    }
}
