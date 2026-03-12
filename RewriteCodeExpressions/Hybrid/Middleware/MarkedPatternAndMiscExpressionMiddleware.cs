using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Middleware;

/// <summary>
/// 对标记的模式匹配与杂项表达式节点执行占位符/删除策略。
/// </summary>
public sealed class MarkedIsPatternExpressionMiddleware : IMiddleware<IsPatternExpressionSyntax>
{
    public SyntaxNode Invoke(IsPatternExpressionSyntax node, IRewriteContext context, MiddlewareDelegate<IsPatternExpressionSyntax> next)
    {
        if (IsMarked(node, context) || IsMarked(node.Expression, context) || IsMarked(node.Pattern, context))
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

public sealed class MarkedDeclarationPatternMiddleware : IMiddleware<DeclarationPatternSyntax>
{
    public SyntaxNode Invoke(DeclarationPatternSyntax node, IRewriteContext context, MiddlewareDelegate<DeclarationPatternSyntax> next)
    {
        if (IsMarked(node, context) || IsMarked(node.Type, context) || IsMarked(node.Designation, context))
        {
            return node.WithAdditionalAnnotations(TerrariaTools.RewriteCodeExpressions.Hybrid.Execution.ExecutionAnnotations.DeleteNode);
        }

        return next(node, context);
    }

    private static bool IsMarked(SyntaxNode node, IRewriteContext context)
    {
        var marked = context.GetState<HashSet<SyntaxNode>>(HybridInputStateKeys.MarkedNodes);
        return marked is not null && marked.Contains(node);
    }
}

public sealed class MarkedVarPatternMiddleware : IMiddleware<VarPatternSyntax>
{
    public SyntaxNode Invoke(VarPatternSyntax node, IRewriteContext context, MiddlewareDelegate<VarPatternSyntax> next)
    {
        if (IsMarked(node, context) || IsMarked(node.Designation, context))
        {
            return node.WithAdditionalAnnotations(TerrariaTools.RewriteCodeExpressions.Hybrid.Execution.ExecutionAnnotations.DeleteNode);
        }

        return next(node, context);
    }

    private static bool IsMarked(SyntaxNode node, IRewriteContext context)
    {
        var marked = context.GetState<HashSet<SyntaxNode>>(HybridInputStateKeys.MarkedNodes);
        return marked is not null && marked.Contains(node);
    }
}

public sealed class MarkedRecursivePatternMiddleware : IMiddleware<RecursivePatternSyntax>
{
    public SyntaxNode Invoke(RecursivePatternSyntax node, IRewriteContext context, MiddlewareDelegate<RecursivePatternSyntax> next)
    {
        var designationMarked = node.Designation is not null && IsMarked(node.Designation, context);
        if (IsMarked(node, context) || designationMarked)
        {
            return node.WithAdditionalAnnotations(TerrariaTools.RewriteCodeExpressions.Hybrid.Execution.ExecutionAnnotations.DeleteNode);
        }

        return next(node, context);
    }

    private static bool IsMarked(SyntaxNode node, IRewriteContext context)
    {
        var marked = context.GetState<HashSet<SyntaxNode>>(HybridInputStateKeys.MarkedNodes);
        return marked is not null && marked.Contains(node);
    }
}

public sealed class MarkedInterpolatedStringExpressionMiddleware : IMiddleware<InterpolatedStringExpressionSyntax>
{
    public SyntaxNode Invoke(InterpolatedStringExpressionSyntax node, IRewriteContext context, MiddlewareDelegate<InterpolatedStringExpressionSyntax> next)
    {
        if (IsMarked(node, context) || node.Contents.Any(content => IsMarked(content, context)))
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

public sealed class MarkedCheckedExpressionMiddleware : IMiddleware<CheckedExpressionSyntax>
{
    public SyntaxNode Invoke(CheckedExpressionSyntax node, IRewriteContext context, MiddlewareDelegate<CheckedExpressionSyntax> next)
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

public sealed class MarkedRefExpressionMiddleware : IMiddleware<RefExpressionSyntax>
{
    public SyntaxNode Invoke(RefExpressionSyntax node, IRewriteContext context, MiddlewareDelegate<RefExpressionSyntax> next)
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
