using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Middleware;

public class LoggingMiddleware<TNode> : IMiddleware<TNode> where TNode : SyntaxNode
{
    public SyntaxNode Invoke(TNode node, IRewriteContext context, MiddlewareDelegate<TNode> next)
    {
        Console.WriteLine($"[Middleware] Before processing {node.Kind()} at {node.SpanStart}");

        var result = next(node, context);

        Console.WriteLine($"[Middleware] After processing {node.Kind()}");
        
        return result;
    }
}
