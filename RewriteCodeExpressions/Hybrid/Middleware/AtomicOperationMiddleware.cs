using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Execution;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Middleware;

/// <summary>
/// Atomically removes a matched node by setting delete annotation.
/// </summary>
public sealed class RemoveNodeMiddleware<TNode> : IMiddleware<TNode> where TNode : SyntaxNode
{
    public SyntaxNode Invoke(TNode node, IRewriteContext context, MiddlewareDelegate<TNode> next)
    {
        var passthrough = next(node, context);
        return passthrough.WithAdditionalAnnotations(ExecutionAnnotations.DeleteNode);
    }
}

/// <summary>
/// Replaces a matched node with a pre-configured node from context state.
/// </summary>
public sealed class ReplaceNodeMiddleware<TNode> : IMiddleware<TNode> where TNode : SyntaxNode
{
    public SyntaxNode Invoke(TNode node, IRewriteContext context, MiddlewareDelegate<TNode> next)
    {
        _ = next(node, context);
        var replacement = context.GetState<SyntaxNode>(AtomicOperationStateKeys.ReplacementNode);
        if (replacement is TNode typedReplacement)
        {
            return typedReplacement.WithTriviaFrom(node);
        }

        return node;
    }
}

/// <summary>
/// Inserts statements before the current statement node.
/// </summary>
public sealed class InsertBeforeMiddleware<TNode> : IMiddleware<TNode> where TNode : StatementSyntax
{
    public SyntaxNode Invoke(TNode node, IRewriteContext context, MiddlewareDelegate<TNode> next)
    {
        var passthrough = next(node, context);
        var statements = context.GetState<IReadOnlyList<StatementSyntax>>(AtomicOperationStateKeys.InsertBeforeStatements);
        if (statements is null || statements.Count == 0)
        {
            return passthrough;
        }

        var id = Guid.NewGuid().ToString("N");
        var registry = context.GetState<Dictionary<string, List<StatementSyntax>>>(AtomicOperationStateKeys.InsertBeforeRegistry)
            ?? new Dictionary<string, List<StatementSyntax>>(StringComparer.Ordinal);
        registry[id] = statements.Select(s => s.WithoutTrivia()).ToList();
        context.SetState(AtomicOperationStateKeys.InsertBeforeRegistry, registry);

        return passthrough.WithAdditionalAnnotations(new SyntaxAnnotation(ExecutionAnnotations.InsertBeforeStatements.Kind, id));
    }
}

/// <summary>
/// Inserts statements after the current statement node.
/// </summary>
public sealed class InsertAfterMiddleware<TNode> : IMiddleware<TNode> where TNode : StatementSyntax
{
    public SyntaxNode Invoke(TNode node, IRewriteContext context, MiddlewareDelegate<TNode> next)
    {
        var passthrough = next(node, context);
        var statements = context.GetState<IReadOnlyList<StatementSyntax>>(AtomicOperationStateKeys.InsertAfterStatements);
        if (statements is null || statements.Count == 0)
        {
            return passthrough;
        }

        var id = Guid.NewGuid().ToString("N");
        var registry = context.GetState<Dictionary<string, List<StatementSyntax>>>(AtomicOperationStateKeys.InsertAfterRegistry)
            ?? new Dictionary<string, List<StatementSyntax>>(StringComparer.Ordinal);
        registry[id] = statements.Select(s => s.WithoutTrivia()).ToList();
        context.SetState(AtomicOperationStateKeys.InsertAfterRegistry, registry);

        return passthrough.WithAdditionalAnnotations(new SyntaxAnnotation(ExecutionAnnotations.InsertAfterStatements.Kind, id));
    }
}

