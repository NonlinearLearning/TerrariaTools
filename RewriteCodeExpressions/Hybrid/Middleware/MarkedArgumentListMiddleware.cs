using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Execution;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Middleware;

/// <summary>
/// 对 ArgumentList 执行标记驱动的删除语义。
/// </summary>
public sealed class MarkedArgumentListMiddleware : IMiddleware<ArgumentListSyntax>
{
    public SyntaxNode Invoke(ArgumentListSyntax node, IRewriteContext context, MiddlewareDelegate<ArgumentListSyntax> next)
    {
        if (IsMarked(node, context))
        {
            return node.WithAdditionalAnnotations(ExecutionAnnotations.DeleteNode);
        }

        if (node.Arguments.Count > 0 && node.Arguments.All(argument => IsMarked(argument, context)))
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
/// 对 BracketedArgumentList 执行标记驱动的删除语义。
/// </summary>
public sealed class MarkedBracketedArgumentListMiddleware : IMiddleware<BracketedArgumentListSyntax>
{
    public SyntaxNode Invoke(BracketedArgumentListSyntax node, IRewriteContext context, MiddlewareDelegate<BracketedArgumentListSyntax> next)
    {
        if (IsMarked(node, context))
        {
            return node.WithAdditionalAnnotations(ExecutionAnnotations.DeleteNode);
        }

        if (node.Arguments.Count > 0 && node.Arguments.All(argument => IsMarked(argument, context)))
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
