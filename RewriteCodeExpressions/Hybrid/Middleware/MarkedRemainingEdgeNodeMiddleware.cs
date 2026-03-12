using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Execution;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Middleware;

/// <summary>
/// 对标记的表达式语句执行删除。
/// </summary>
public sealed class MarkedExpressionStatementMiddleware : IMiddleware<ExpressionStatementSyntax>
{
    public SyntaxNode Invoke(ExpressionStatementSyntax node, IRewriteContext context, MiddlewareDelegate<ExpressionStatementSyntax> next)
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
/// 对标记的 switch 表达式执行占位符/删除策略。
/// </summary>
public sealed class MarkedSwitchExpressionMiddleware : IMiddleware<SwitchExpressionSyntax>
{
    public SyntaxNode Invoke(SwitchExpressionSyntax node, IRewriteContext context, MiddlewareDelegate<SwitchExpressionSyntax> next)
    {
        if (IsMarked(node, context) || IsMarked(node.GoverningExpression, context))
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

/// <summary>
/// 对标记的 base 表达式执行占位符/删除策略。
/// </summary>
public sealed class MarkedBaseExpressionMiddleware : IMiddleware<BaseExpressionSyntax>
{
    public SyntaxNode Invoke(BaseExpressionSyntax node, IRewriteContext context, MiddlewareDelegate<BaseExpressionSyntax> next)
    {
        if (IsMarked(node, context))
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

/// <summary>
/// 对标记的 this 表达式执行占位符/删除策略。
/// </summary>
public sealed class MarkedThisExpressionMiddleware : IMiddleware<ThisExpressionSyntax>
{
    public SyntaxNode Invoke(ThisExpressionSyntax node, IRewriteContext context, MiddlewareDelegate<ThisExpressionSyntax> next)
    {
        if (IsMarked(node, context))
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

/// <summary>
/// 对被标记的方法声明执行删除（签名或表达式体受影响时）。
/// </summary>
public sealed class MarkedMethodDeclarationMiddleware : IMiddleware<MethodDeclarationSyntax>
{
    public SyntaxNode Invoke(MethodDeclarationSyntax node, IRewriteContext context, MiddlewareDelegate<MethodDeclarationSyntax> next)
    {
        var expressionBodyMarked = node.ExpressionBody is not null && IsMarked(node.ExpressionBody, context);
        if (IsMarked(node, context) || IsMarked(node.ReturnType, context) || expressionBodyMarked)
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
