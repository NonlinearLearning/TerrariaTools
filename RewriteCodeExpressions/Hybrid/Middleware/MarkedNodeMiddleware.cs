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
/// 对预标记的 if 节点执行删除式简化。
/// </summary>
public sealed class MarkedIfStatementMiddleware : IMiddleware<IfStatementSyntax>
{
    public SyntaxNode Invoke(IfStatementSyntax node, IRewriteContext context, MiddlewareDelegate<IfStatementSyntax> next)
    {
        var nodeMarked = IsMarked(node, context);
        var conditionMarked = IsMarked(node.Condition, context);
        var trueBranchMarked = IsMarked(node.Statement, context);
        var elseBranchMarked = node.Else is not null && IsMarked(node.Else.Statement, context);

        if (!nodeMarked && !conditionMarked && !trueBranchMarked && !elseBranchMarked)
        {
            return next(node, context);
        }

        // 对齐旧 IfStatementHandler: 条件移除时优先提升 else，否则删除整个 if。
        if (conditionMarked)
        {
            if (node.Else is not null)
            {
                return node.Else.Statement.WithTriviaFrom(node);
            }

            return node.WithAdditionalAnnotations(ExecutionAnnotations.DeleteNode);
        }

        // true 分支被标记时：无 else 则删除；有 else 且 else 未标记则提升 else；否则删除。
        if (trueBranchMarked)
        {
            if (node.Else is null || elseBranchMarked)
            {
                return node.WithAdditionalAnnotations(ExecutionAnnotations.DeleteNode);
            }

            return node.Else.Statement.WithTriviaFrom(node);
        }

        return node.Else?.Statement.WithTriviaFrom(node) ?? SyntaxFactory.EmptyStatement().WithTriviaFrom(node);
    }

    private static bool IsMarked(SyntaxNode node, IRewriteContext context)
    {
        var marked = context.GetState<HashSet<SyntaxNode>>(HybridInputStateKeys.MarkedNodes);
        return marked is not null && marked.Contains(node);
    }
}

/// <summary>
/// 对预标记的 while 节点执行删除式简化。
/// </summary>
public sealed class MarkedWhileStatementMiddleware : IMiddleware<WhileStatementSyntax>
{
    public SyntaxNode Invoke(WhileStatementSyntax node, IRewriteContext context, MiddlewareDelegate<WhileStatementSyntax> next)
    {
        var nodeMarked = IsMarked(node, context);
        var conditionMarked = IsMarked(node.Condition, context);
        var bodyMarked = IsMarked(node.Statement, context);

        if (!nodeMarked && !conditionMarked && !bodyMarked)
        {
            return next(node, context);
        }

        // 对齐旧 WhileStatementHandler: 条件或循环体失效时移除整个 while。
        if (conditionMarked || bodyMarked)
        {
            return node.WithAdditionalAnnotations(ExecutionAnnotations.DeleteNode);
        }

        return SyntaxFactory.EmptyStatement().WithTriviaFrom(node);
    }

    private static bool IsMarked(SyntaxNode node, IRewriteContext context)
    {
        var marked = context.GetState<HashSet<SyntaxNode>>(HybridInputStateKeys.MarkedNodes);
        return marked is not null && marked.Contains(node);
    }
}

/// <summary>
/// 对预标记的三元表达式使用占位符替换。
/// </summary>
public sealed class MarkedConditionalExpressionMiddleware : IMiddleware<ConditionalExpressionSyntax>
{
    public SyntaxNode Invoke(ConditionalExpressionSyntax node, IRewriteContext context, MiddlewareDelegate<ConditionalExpressionSyntax> next)
    {
        var nodeMarked = IsMarked(node, context);
        var conditionMarked = IsMarked(node.Condition, context);
        var trueMarked = IsMarked(node.WhenTrue, context);
        var falseMarked = IsMarked(node.WhenFalse, context);

        if (!nodeMarked && !conditionMarked && !trueMarked && !falseMarked)
        {
            return next(node, context);
        }

        // 对齐旧 ConditionalExpressionHandler:
        // 1) 条件失效 -> 提升 false 分支
        if (conditionMarked)
        {
            return node.WhenFalse.WithTriviaFrom(node);
        }

        // 2) 两个分支都失效 -> 生成占位，失败则删除
        if (trueMarked && falseMarked)
        {
            var generator = SyntaxGenerator.GetGenerator(new AdhocWorkspace(), LanguageNames.CSharp);
            return PlaceholderFactory.CreatePlaceholder(node, context.SemanticModel, generator)
                ?? node.WithAdditionalAnnotations(TerrariaTools.RewriteCodeExpressions.Hybrid.Execution.ExecutionAnnotations.DeleteNode);
        }

        // 3) true 失效 -> 提升 false
        if (trueMarked)
        {
            return node.WhenFalse.WithTriviaFrom(node);
        }

        // 4) false 失效 -> 提升 true
        if (falseMarked)
        {
            return node.WhenTrue.WithTriviaFrom(node);
        }

        var fallbackGenerator = SyntaxGenerator.GetGenerator(new AdhocWorkspace(), LanguageNames.CSharp);
        return PlaceholderFactory.CreatePlaceholder(node, context.SemanticModel, fallbackGenerator) ?? node;
    }

    private static bool IsMarked(SyntaxNode node, IRewriteContext context)
    {
        var marked = context.GetState<HashSet<SyntaxNode>>(HybridInputStateKeys.MarkedNodes);
        return marked is not null && marked.Contains(node);
    }
}
