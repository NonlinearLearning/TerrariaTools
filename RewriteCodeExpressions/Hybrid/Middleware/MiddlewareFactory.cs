using System;
using Microsoft.CodeAnalysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Middleware;

public static class MiddlewareFactory
{
    public static IMiddleware<TNode> Create<TNode>(Type middlewareType) where TNode : SyntaxNode
    {
        if (!typeof(IMiddleware<TNode>).IsAssignableFrom(middlewareType))
        {
            throw new ArgumentException($"Type {middlewareType.Name} does not implement IMiddleware<{typeof(TNode).Name}>");
        }

        // 这里使用简单的 Activator.CreateInstance，实际项目中建议使用依赖注入容器
        var instance = Activator.CreateInstance(middlewareType);
        if (instance == null)
        {
            throw new InvalidOperationException($"Failed to create instance of {middlewareType.Name}");
        }

        return (IMiddleware<TNode>)instance;
    }
}
