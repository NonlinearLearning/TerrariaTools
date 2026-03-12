using Microsoft.CodeAnalysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Middleware;

/// <summary>
/// Preserves leading/trailing trivia from original node when node is rewritten.
/// </summary>
public sealed class PreserveTriviaMiddleware<TNode> : IMiddleware<TNode> where TNode : SyntaxNode
{
    public SyntaxNode Invoke(TNode node, IRewriteContext context, MiddlewareDelegate<TNode> next)
    {
        var rewritten = next(node, context);
        if (ReferenceEquals(rewritten, node) || rewritten.HasAnnotation(Execution.ExecutionAnnotations.DeleteNode))
        {
            return rewritten;
        }

        return rewritten.WithLeadingTrivia(node.GetLeadingTrivia())
            .WithTrailingTrivia(node.GetTrailingTrivia());
    }
}

/// <summary>
/// Normalizes whitespace for rewritten node.
/// </summary>
public sealed class FormatNodeMiddleware<TNode> : IMiddleware<TNode> where TNode : SyntaxNode
{
    public SyntaxNode Invoke(TNode node, IRewriteContext context, MiddlewareDelegate<TNode> next)
    {
        var rewritten = next(node, context);
        if (ReferenceEquals(rewritten, node) || rewritten.HasAnnotation(Execution.ExecutionAnnotations.DeleteNode))
        {
            return rewritten;
        }

        return rewritten.NormalizeWhitespace();
    }
}

/// <summary>
/// Logs per-rule hit counts into context state.
/// </summary>
public sealed class LogMetricMiddleware<TNode> : IMiddleware<TNode> where TNode : SyntaxNode
{
    public SyntaxNode Invoke(TNode node, IRewriteContext context, MiddlewareDelegate<TNode> next)
    {
        var hitCounts = context.GetState<Dictionary<string, int>>(HybridMetricsStateKeys.RuleHitCounts)
            ?? new Dictionary<string, int>(StringComparer.Ordinal);

        var key = $"{typeof(TNode).Name}:{GetType().Name}";
        hitCounts[key] = hitCounts.TryGetValue(key, out var count) ? count + 1 : 1;
        context.SetState(HybridMetricsStateKeys.RuleHitCounts, hitCounts);

        return next(node, context);
    }
}

