using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Execution;
using TerrariaTools.RewriteCodeExpressions.Pipeline;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Middleware;

/// <summary>
/// 对标记的 YieldStatement 执行占位符替换或删除。
/// </summary>
public sealed class MarkedYieldStatementMiddleware : IMiddleware<YieldStatementSyntax>
{
    public SyntaxNode Invoke(YieldStatementSyntax node, IRewriteContext context, MiddlewareDelegate<YieldStatementSyntax> next)
    {
        if (!IsMarked(node, context) && (node.Expression is null || !IsMarked(node.Expression, context)))
        {
            return next(node, context);
        }

        if (node.ReturnOrBreakKeyword.IsKind(SyntaxKind.ReturnKeyword))
        {
            var generator = SyntaxGenerator.GetGenerator(new AdhocWorkspace(), LanguageNames.CSharp);
            return PlaceholderFactory.CreatePlaceholder(node, context.SemanticModel, generator)
                ?? node.WithAdditionalAnnotations(ExecutionAnnotations.DeleteNode);
        }

        return node.WithAdditionalAnnotations(ExecutionAnnotations.DeleteNode);
    }

    private static bool IsMarked(SyntaxNode node, IRewriteContext context)
    {
        var marked = context.GetState<HashSet<SyntaxNode>>(HybridInputStateKeys.MarkedNodes);
        return marked is not null && marked.Contains(node);
    }
}

/// <summary>
/// 对标记的 ArrowExpressionClause 执行占位符替换或删除。
/// </summary>
public sealed class MarkedArrowExpressionClauseMiddleware : IMiddleware<ArrowExpressionClauseSyntax>
{
    public SyntaxNode Invoke(ArrowExpressionClauseSyntax node, IRewriteContext context, MiddlewareDelegate<ArrowExpressionClauseSyntax> next)
    {
        if (!IsMarked(node, context) && !IsMarked(node.Expression, context))
        {
            return next(node, context);
        }

        var generator = SyntaxGenerator.GetGenerator(new AdhocWorkspace(), LanguageNames.CSharp);
        return PlaceholderFactory.CreatePlaceholder(node, context.SemanticModel, generator)
            ?? node.WithAdditionalAnnotations(ExecutionAnnotations.DeleteNode);
    }

    private static bool IsMarked(SyntaxNode node, IRewriteContext context)
    {
        var marked = context.GetState<HashSet<SyntaxNode>>(HybridInputStateKeys.MarkedNodes);
        return marked is not null && marked.Contains(node);
    }
}

/// <summary>
/// 对标记的 SwitchExpressionArm 执行占位符替换或删除。
/// </summary>
public sealed class MarkedSwitchExpressionArmMiddleware : IMiddleware<SwitchExpressionArmSyntax>
{
    public SyntaxNode Invoke(SwitchExpressionArmSyntax node, IRewriteContext context, MiddlewareDelegate<SwitchExpressionArmSyntax> next)
    {
        var whenMarked = node.WhenClause is not null && IsMarked(node.WhenClause.Condition, context);
        if (!IsMarked(node, context) && !IsMarked(node.Expression, context) && !whenMarked)
        {
            return next(node, context);
        }

        var generator = SyntaxGenerator.GetGenerator(new AdhocWorkspace(), LanguageNames.CSharp);
        return PlaceholderFactory.CreatePlaceholder(node, context.SemanticModel, generator)
            ?? node.WithAdditionalAnnotations(ExecutionAnnotations.DeleteNode);
    }

    private static bool IsMarked(SyntaxNode node, IRewriteContext context)
    {
        var marked = context.GetState<HashSet<SyntaxNode>>(HybridInputStateKeys.MarkedNodes);
        return marked is not null && marked.Contains(node);
    }
}

/// <summary>
/// 对标记的 Interpolation 执行表达式占位符替换。
/// </summary>
public sealed class MarkedInterpolationMiddleware : IMiddleware<InterpolationSyntax>
{
    public SyntaxNode Invoke(InterpolationSyntax node, IRewriteContext context, MiddlewareDelegate<InterpolationSyntax> next)
    {
        if (!IsMarked(node, context) && !IsMarked(node.Expression, context))
        {
            return next(node, context);
        }

        var generator = SyntaxGenerator.GetGenerator(new AdhocWorkspace(), LanguageNames.CSharp);
        var placeholder = PlaceholderFactory.CreatePlaceholder(node.Expression, context.SemanticModel, generator) as ExpressionSyntax;
        if (placeholder is not null)
        {
            return node.WithExpression(placeholder);
        }

        return node.WithAdditionalAnnotations(ExecutionAnnotations.DeleteNode);
    }

    private static bool IsMarked(SyntaxNode node, IRewriteContext context)
    {
        var marked = context.GetState<HashSet<SyntaxNode>>(HybridInputStateKeys.MarkedNodes);
        return marked is not null && marked.Contains(node);
    }
}

/// <summary>
/// 对标记的匿名对象成员执行占位符替换或删除。
/// </summary>
public sealed class MarkedAnonymousObjectMemberDeclaratorMiddleware : IMiddleware<AnonymousObjectMemberDeclaratorSyntax>
{
    public SyntaxNode Invoke(AnonymousObjectMemberDeclaratorSyntax node, IRewriteContext context, MiddlewareDelegate<AnonymousObjectMemberDeclaratorSyntax> next)
    {
        if (!IsMarked(node, context) && !IsMarked(node.Expression, context))
        {
            return next(node, context);
        }

        var generator = SyntaxGenerator.GetGenerator(new AdhocWorkspace(), LanguageNames.CSharp);
        return PlaceholderFactory.CreatePlaceholder(node, context.SemanticModel, generator)
            ?? node.WithAdditionalAnnotations(ExecutionAnnotations.DeleteNode);
    }

    private static bool IsMarked(SyntaxNode node, IRewriteContext context)
    {
        var marked = context.GetState<HashSet<SyntaxNode>>(HybridInputStateKeys.MarkedNodes);
        return marked is not null && marked.Contains(node);
    }
}
