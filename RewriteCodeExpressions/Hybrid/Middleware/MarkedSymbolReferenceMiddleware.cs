using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Execution;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Middleware;

/// <summary>
/// 对标记的标识符引用执行占位符/删除策略。
/// </summary>
public sealed class MarkedIdentifierNameMiddleware : IMiddleware<IdentifierNameSyntax>
{
    public SyntaxNode Invoke(IdentifierNameSyntax node, IRewriteContext context, MiddlewareDelegate<IdentifierNameSyntax> next)
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
/// 对标记的委托声明执行删除。
/// </summary>
public sealed class MarkedDelegateDeclarationMiddleware : IMiddleware<DelegateDeclarationSyntax>
{
    public SyntaxNode Invoke(DelegateDeclarationSyntax node, IRewriteContext context, MiddlewareDelegate<DelegateDeclarationSyntax> next)
    {
        if (IsMarked(node, context) || IsMarked(node.ReturnType, context))
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
