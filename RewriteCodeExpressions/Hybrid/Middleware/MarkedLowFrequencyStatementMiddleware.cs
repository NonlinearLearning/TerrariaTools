using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Execution;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Middleware;

/// <summary>
/// 对标记的 throw 语句执行删除。
/// </summary>
public sealed class MarkedThrowStatementMiddleware : IMiddleware<ThrowStatementSyntax>
{
    public SyntaxNode Invoke(ThrowStatementSyntax node, IRewriteContext context, MiddlewareDelegate<ThrowStatementSyntax> next)
    {
        if (IsMarked(node, context) || (node.Expression is not null && IsMarked(node.Expression, context)))
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
/// 对标记的 lock 语句执行删除。
/// </summary>
public sealed class MarkedLockStatementMiddleware : IMiddleware<LockStatementSyntax>
{
    public SyntaxNode Invoke(LockStatementSyntax node, IRewriteContext context, MiddlewareDelegate<LockStatementSyntax> next)
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
/// 对标记的 using 语句执行删除。
/// </summary>
public sealed class MarkedUsingStatementMiddleware : IMiddleware<UsingStatementSyntax>
{
    public SyntaxNode Invoke(UsingStatementSyntax node, IRewriteContext context, MiddlewareDelegate<UsingStatementSyntax> next)
    {
        var expressionMarked = node.Expression is not null && IsMarked(node.Expression, context);
        var declarationMarked = node.Declaration is not null && IsMarked(node.Declaration, context);
        if (IsMarked(node, context) || expressionMarked || declarationMarked)
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
/// 对标记的 fixed 语句执行删除。
/// </summary>
public sealed class MarkedFixedStatementMiddleware : IMiddleware<FixedStatementSyntax>
{
    public SyntaxNode Invoke(FixedStatementSyntax node, IRewriteContext context, MiddlewareDelegate<FixedStatementSyntax> next)
    {
        if (IsMarked(node, context) || IsMarked(node.Declaration, context))
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
/// 对标记的 foreach 语句执行删除。
/// </summary>
public sealed class MarkedForEachStatementMiddleware : IMiddleware<ForEachStatementSyntax>
{
    public SyntaxNode Invoke(ForEachStatementSyntax node, IRewriteContext context, MiddlewareDelegate<ForEachStatementSyntax> next)
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
