using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Execution;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Middleware;

/// <summary>
/// 对标记的属性节点执行删除。
/// </summary>
public sealed class MarkedAttributeMiddleware : IMiddleware<AttributeSyntax>
{
    public SyntaxNode Invoke(AttributeSyntax node, IRewriteContext context, MiddlewareDelegate<AttributeSyntax> next)
    {
        if (IsMarked(node, context) || IsMarked(node.Name, context))
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
/// 对标记的属性列表节点执行删除。
/// </summary>
public sealed class MarkedAttributeListMiddleware : IMiddleware<AttributeListSyntax>
{
    public SyntaxNode Invoke(AttributeListSyntax node, IRewriteContext context, MiddlewareDelegate<AttributeListSyntax> next)
    {
        if (IsMarked(node, context) || node.Attributes.Any(attribute => IsMarked(attribute, context)))
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
/// 对标记的 using 指令执行删除。
/// </summary>
public sealed class MarkedUsingDirectiveMiddleware : IMiddleware<UsingDirectiveSyntax>
{
    public SyntaxNode Invoke(UsingDirectiveSyntax node, IRewriteContext context, MiddlewareDelegate<UsingDirectiveSyntax> next)
    {
        if (IsMarked(node, context))
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
/// 对标记的变量标识执行替换为 discard（_）。
/// </summary>
public sealed class MarkedSingleVariableDesignationMiddleware : IMiddleware<SingleVariableDesignationSyntax>
{
    public SyntaxNode Invoke(SingleVariableDesignationSyntax node, IRewriteContext context, MiddlewareDelegate<SingleVariableDesignationSyntax> next)
    {
        if (IsMarked(node, context))
        {
            return SyntaxFactory.DiscardDesignation().WithTriviaFrom(node);
        }

        return next(node, context);
    }

    private static bool IsMarked(SyntaxNode node, IRewriteContext context)
    {
        var marked = context.GetState<HashSet<SyntaxNode>>(HybridInputStateKeys.MarkedNodes);
        return marked is not null && marked.Contains(node);
    }
}
