using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Middleware;

/// <summary>
/// 对标记的对象创建表达式执行占位符/删除策略。
/// </summary>
public sealed class MarkedObjectCreationExpressionMiddleware : IMiddleware<ObjectCreationExpressionSyntax>
{
    public SyntaxNode Invoke(ObjectCreationExpressionSyntax node, IRewriteContext context, MiddlewareDelegate<ObjectCreationExpressionSyntax> next)
    {
        if (!IsMarked(node, context)
            && (node.ArgumentList is null || !IsMarked(node.ArgumentList, context))
            && (node.Initializer is null || !IsMarked(node.Initializer, context)))
        {
            return next(node, context);
        }

        return MarkedExpressionCleanup.ReplaceOrDelete(node, context);
    }

    private static bool IsMarked(SyntaxNode node, IRewriteContext context)
    {
        var marked = context.GetState<HashSet<SyntaxNode>>(HybridInputStateKeys.MarkedNodes);
        return marked is not null && marked.Contains(node);
    }
}

/// <summary>
/// 对标记的隐式对象创建表达式执行占位符/删除策略。
/// </summary>
public sealed class MarkedImplicitObjectCreationExpressionMiddleware : IMiddleware<ImplicitObjectCreationExpressionSyntax>
{
    public SyntaxNode Invoke(ImplicitObjectCreationExpressionSyntax node, IRewriteContext context, MiddlewareDelegate<ImplicitObjectCreationExpressionSyntax> next)
    {
        if (!IsMarked(node, context)
            && (node.ArgumentList is null || !IsMarked(node.ArgumentList, context))
            && (node.Initializer is null || !IsMarked(node.Initializer, context)))
        {
            return next(node, context);
        }

        return MarkedExpressionCleanup.ReplaceOrDelete(node, context);
    }

    private static bool IsMarked(SyntaxNode node, IRewriteContext context)
    {
        var marked = context.GetState<HashSet<SyntaxNode>>(HybridInputStateKeys.MarkedNodes);
        return marked is not null && marked.Contains(node);
    }
}

/// <summary>
/// 对标记的匿名对象创建表达式执行占位符/删除策略。
/// </summary>
public sealed class MarkedAnonymousObjectCreationExpressionMiddleware : IMiddleware<AnonymousObjectCreationExpressionSyntax>
{
    public SyntaxNode Invoke(AnonymousObjectCreationExpressionSyntax node, IRewriteContext context, MiddlewareDelegate<AnonymousObjectCreationExpressionSyntax> next)
    {
        if (!IsMarked(node, context) && !node.Initializers.Any(initializer => IsMarked(initializer, context)))
        {
            return next(node, context);
        }

        return MarkedExpressionCleanup.ReplaceOrDelete(node, context);
    }

    private static bool IsMarked(SyntaxNode node, IRewriteContext context)
    {
        var marked = context.GetState<HashSet<SyntaxNode>>(HybridInputStateKeys.MarkedNodes);
        return marked is not null && marked.Contains(node);
    }
}

/// <summary>
/// 对标记的初始化器表达式执行占位符/删除策略。
/// </summary>
public sealed class MarkedInitializerExpressionMiddleware : IMiddleware<InitializerExpressionSyntax>
{
    public SyntaxNode Invoke(InitializerExpressionSyntax node, IRewriteContext context, MiddlewareDelegate<InitializerExpressionSyntax> next)
    {
        if (!IsMarked(node, context) && !node.Expressions.Any(expression => IsMarked(expression, context)))
        {
            return next(node, context);
        }

        return MarkedExpressionCleanup.ReplaceOrDelete(node, context);
    }

    private static bool IsMarked(SyntaxNode node, IRewriteContext context)
    {
        var marked = context.GetState<HashSet<SyntaxNode>>(HybridInputStateKeys.MarkedNodes);
        return marked is not null && marked.Contains(node);
    }
}
