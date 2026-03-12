using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Dome.Rewrite.Roslyn;

using TerrariaTools.Dome.Core;

public sealed class RoslynRewriteExecutor
{
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

    private static TargetResolutionResult FindTargetNode(SyntaxNode root, PlanTarget target)
    {
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

    private static StatementSyntax CommentOut(StatementSyntax statement, PlannedChange change)
    {
        return SyntaxFactory.EmptyStatement()
            .WithLeadingTrivia(
                SyntaxFactory.Comment($"// {change.Reason.RuleId}: {statement.ToString().Trim()}"),
                SyntaxFactory.CarriageReturnLineFeed);
    }

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

    private static StatementSyntax AddReturn(StatementSyntax statement, PlannedChange change)
    {
        var payload = change.Action.Payload;
        var returnStatement = string.IsNullOrWhiteSpace(payload)
            ? "return;"
            : $"return {payload};";
        return SyntaxFactory.ParseStatement(returnStatement)
            .WithTriviaFrom(statement);
    }

    private static ApplyChangeResult ApplyChange(SyntaxNode root, SyntaxNode node, PlannedChange change)
    {
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

    private sealed record TargetResolutionResult(bool IsSuccess, SyntaxNode? Node, string Message)
    {
        public static TargetResolutionResult Success(SyntaxNode node) => new(true, node, string.Empty);

        public static TargetResolutionResult Failure(string message) => new(false, null, message);
    }

    private sealed record ApplyChangeResult(bool IsSuccess, SyntaxNode? Root, string Message)
    {
        public static ApplyChangeResult Success(SyntaxNode root) => new(true, root, string.Empty);

        public static ApplyChangeResult Failure(string message) => new(false, null, message);
    }
}
