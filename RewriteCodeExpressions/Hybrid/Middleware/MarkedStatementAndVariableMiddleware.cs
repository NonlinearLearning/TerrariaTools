using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Execution;
using TerrariaTools.RewriteCodeExpressions.Pipeline;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Middleware;

/// <summary>
/// 对标记的 return 语句执行占位符替换或删除。
/// </summary>
public sealed class MarkedReturnStatementMiddleware : IMiddleware<ReturnStatementSyntax>
{
    public SyntaxNode Invoke(ReturnStatementSyntax node, IRewriteContext context, MiddlewareDelegate<ReturnStatementSyntax> next)
    {
        if (!IsMarked(node, context))
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
/// 对标记的局部声明语句执行删除。
/// </summary>
public sealed class MarkedLocalDeclarationStatementMiddleware : IMiddleware<LocalDeclarationStatementSyntax>
{
    public SyntaxNode Invoke(LocalDeclarationStatementSyntax node, IRewriteContext context, MiddlewareDelegate<LocalDeclarationStatementSyntax> next)
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
/// 对标记的变量声明器执行删除。
/// </summary>
public sealed class MarkedVariableDeclaratorMiddleware : IMiddleware<VariableDeclaratorSyntax>
{
    public SyntaxNode Invoke(VariableDeclaratorSyntax node, IRewriteContext context, MiddlewareDelegate<VariableDeclaratorSyntax> next)
    {
        if (IsMarked(node, context) || (node.Initializer is not null && IsMarked(node.Initializer.Value, context)))
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
/// 对标记的变量声明节点执行删除（通常用于所有声明器均被标记的情况）。
/// </summary>
public sealed class MarkedVariableDeclarationMiddleware : IMiddleware<VariableDeclarationSyntax>
{
    public SyntaxNode Invoke(VariableDeclarationSyntax node, IRewriteContext context, MiddlewareDelegate<VariableDeclarationSyntax> next)
    {
        if (IsMarked(node, context))
        {
            return node.WithAdditionalAnnotations(ExecutionAnnotations.DeleteNode);
        }

        if (node.Variables.Count > 0 && node.Variables.All(variable => IsMarked(variable, context)))
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
/// 对标记的初始化子句执行删除（`int x = value` -> `int x`）。
/// </summary>
public sealed class MarkedEqualsValueClauseMiddleware : IMiddleware<EqualsValueClauseSyntax>
{
    public SyntaxNode Invoke(EqualsValueClauseSyntax node, IRewriteContext context, MiddlewareDelegate<EqualsValueClauseSyntax> next)
    {
        if (IsMarked(node, context) || IsMarked(node.Value, context))
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
