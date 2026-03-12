using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Execution;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Middleware;

/// <summary>
/// 对标记的 do-while 语句执行删除。
/// </summary>
public sealed class MarkedDoStatementMiddleware : IMiddleware<DoStatementSyntax>
{
    public SyntaxNode Invoke(DoStatementSyntax node, IRewriteContext context, MiddlewareDelegate<DoStatementSyntax> next)
    {
        if (IsMarked(node, context) || IsMarked(node.Condition, context) || IsMarked(node.Statement, context))
        {
            return node.WithAdditionalAnnotations(ExecutionAnnotations.DeleteNode);
        }

        return next(node, context);
    }

    private static bool IsMarked(SyntaxNode node, IRewriteContext context)
    {
        var marked = context.GetState<HashSet<SyntaxNode>>(HybridInputStateKeys.MarkedNodes);
        return marked is not null && marked.Contains(node);
    }
}

/// <summary>
/// 对标记的 for 语句执行删除。
/// </summary>
public sealed class MarkedForStatementMiddleware : IMiddleware<ForStatementSyntax>
{
    public SyntaxNode Invoke(ForStatementSyntax node, IRewriteContext context, MiddlewareDelegate<ForStatementSyntax> next)
    {
        var conditionMarked = node.Condition is not null && IsMarked(node.Condition, context);
        var declarationMarked = node.Declaration is not null && IsMarked(node.Declaration, context);
        if (IsMarked(node, context) || conditionMarked || declarationMarked || IsMarked(node.Statement, context))
        {
            return node.WithAdditionalAnnotations(ExecutionAnnotations.DeleteNode);
        }

        return next(node, context);
    }

    private static bool IsMarked(SyntaxNode node, IRewriteContext context)
    {
        var marked = context.GetState<HashSet<SyntaxNode>>(HybridInputStateKeys.MarkedNodes);
        return marked is not null && marked.Contains(node);
    }
}

/// <summary>
/// 对标记的 switch 语句执行删除。
/// </summary>
public sealed class MarkedSwitchStatementMiddleware : IMiddleware<SwitchStatementSyntax>
{
    public SyntaxNode Invoke(SwitchStatementSyntax node, IRewriteContext context, MiddlewareDelegate<SwitchStatementSyntax> next)
    {
        if (IsMarked(node, context) || IsMarked(node.Expression, context))
        {
            return node.WithAdditionalAnnotations(ExecutionAnnotations.DeleteNode);
        }

        return next(node, context);
    }

    private static bool IsMarked(SyntaxNode node, IRewriteContext context)
    {
        var marked = context.GetState<HashSet<SyntaxNode>>(HybridInputStateKeys.MarkedNodes);
        return marked is not null && marked.Contains(node);
    }
}

/// <summary>
/// 对标记的 with 表达式执行占位符/删除策略。
/// </summary>
public sealed class MarkedWithExpressionMiddleware : IMiddleware<WithExpressionSyntax>
{
    public SyntaxNode Invoke(WithExpressionSyntax node, IRewriteContext context, MiddlewareDelegate<WithExpressionSyntax> next)
    {
        if (IsMarked(node, context) || IsMarked(node.Expression, context))
        {
            return MarkedExpressionCleanup.ReplaceOrDelete(node, context);
        }

        return next(node, context);
    }

    private static bool IsMarked(SyntaxNode node, IRewriteContext context)
    {
        var marked = context.GetState<HashSet<SyntaxNode>>(HybridInputStateKeys.MarkedNodes);
        return marked is not null && marked.Contains(node);
    }
}
