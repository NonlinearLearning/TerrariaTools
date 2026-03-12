using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Middleware;

public class MiddlewarePipeline<TNode> where TNode : SyntaxNode
{
    private readonly MiddlewareDelegate<TNode> _pipeline;

    public MiddlewarePipeline(IEnumerable<IMiddleware<TNode>> middlewares)
    {
        // 构建管道：反向组合
        // 最后一个中间件的 next 是一个简单的返回原始节点的委托（或者抛出异常，视设计而定）
        MiddlewareDelegate<TNode> next = (node, context) => node;

        foreach (var middleware in middlewares.Reverse())
        {
            var currentNext = next;
            next = (node, context) => middleware.Invoke(node, context, currentNext);
        }

        _pipeline = next;
    }

    public SyntaxNode Execute(TNode node, IRewriteContext context)
    {
        return _pipeline(node, context);
    }
}
