using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Execution;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Middleware;

/// <summary>
/// 对标记的字段声明执行删除。
/// </summary>
public sealed class MarkedFieldDeclarationMiddleware : IMiddleware<FieldDeclarationSyntax>
{
    public SyntaxNode Invoke(FieldDeclarationSyntax node, IRewriteContext context, MiddlewareDelegate<FieldDeclarationSyntax> next)
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
/// 对标记的属性声明执行删除。
/// </summary>
public sealed class MarkedPropertyDeclarationMiddleware : IMiddleware<PropertyDeclarationSyntax>
{
    public SyntaxNode Invoke(PropertyDeclarationSyntax node, IRewriteContext context, MiddlewareDelegate<PropertyDeclarationSyntax> next)
    {
        var exprBodyMarked = node.ExpressionBody is not null && IsMarked(node.ExpressionBody, context);
        if (IsMarked(node, context) || IsMarked(node.Type, context) || exprBodyMarked)
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
/// 对标记的索引器声明执行删除。
/// </summary>
public sealed class MarkedIndexerDeclarationMiddleware : IMiddleware<IndexerDeclarationSyntax>
{
    public SyntaxNode Invoke(IndexerDeclarationSyntax node, IRewriteContext context, MiddlewareDelegate<IndexerDeclarationSyntax> next)
    {
        var exprBodyMarked = node.ExpressionBody is not null && IsMarked(node.ExpressionBody, context);
        if (IsMarked(node, context) || IsMarked(node.Type, context) || exprBodyMarked)
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
/// 对标记的事件声明执行删除。
/// </summary>
public sealed class MarkedEventDeclarationMiddleware : IMiddleware<EventDeclarationSyntax>
{
    public SyntaxNode Invoke(EventDeclarationSyntax node, IRewriteContext context, MiddlewareDelegate<EventDeclarationSyntax> next)
    {
        if (IsMarked(node, context) || IsMarked(node.Type, context))
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
/// 对标记的参数声明执行删除。
/// </summary>
public sealed class MarkedParameterMiddleware : IMiddleware<ParameterSyntax>
{
    public SyntaxNode Invoke(ParameterSyntax node, IRewriteContext context, MiddlewareDelegate<ParameterSyntax> next)
    {
        if (IsMarked(node, context) || (node.Type is not null && IsMarked(node.Type, context)))
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
