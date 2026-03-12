using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Execution;
using TerrariaTools.RewriteCodeExpressions.Pipeline;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Middleware;

/// <summary>
/// 对标记的 InvocationExpression 执行占位符/删除策略。
/// </summary>
public sealed class MarkedInvocationExpressionMiddleware : IMiddleware<InvocationExpressionSyntax>
{
    public SyntaxNode Invoke(InvocationExpressionSyntax node, IRewriteContext context, MiddlewareDelegate<InvocationExpressionSyntax> next)
    {
        if (!IsMarked(node, context))
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
/// 对标记的 MemberAccessExpression 执行占位符/删除策略。
/// </summary>
public sealed class MarkedMemberAccessExpressionMiddleware : IMiddleware<MemberAccessExpressionSyntax>
{
    public SyntaxNode Invoke(MemberAccessExpressionSyntax node, IRewriteContext context, MiddlewareDelegate<MemberAccessExpressionSyntax> next)
    {
        if (!IsMarked(node, context))
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
/// 对标记的 ElementAccessExpression 执行占位符/删除策略。
/// </summary>
public sealed class MarkedElementAccessExpressionMiddleware : IMiddleware<ElementAccessExpressionSyntax>
{
    public SyntaxNode Invoke(ElementAccessExpressionSyntax node, IRewriteContext context, MiddlewareDelegate<ElementAccessExpressionSyntax> next)
    {
        if (!IsMarked(node, context))
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

internal static class MarkedExpressionCleanup
{
    internal static SyntaxNode ReplaceOrDelete(SyntaxNode node, IRewriteContext context)
    {
        if (PlaceholderFactory.IsValueRequiredContext(node))
        {
            var generator = SyntaxGenerator.GetGenerator(new AdhocWorkspace(), LanguageNames.CSharp);
            return PlaceholderFactory.CreatePlaceholder(node, context.SemanticModel, generator) ?? node;
        }

        return node.WithAdditionalAnnotations(ExecutionAnnotations.DeleteNode);
    }
}
