using Microsoft.CodeAnalysis;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;

/// <summary>
/// 定义中间件管道中的下一个操作。
/// </summary>
/// <typeparam name="TNode">节点类型。</typeparam>
/// <param name="node">当前节点。</param>
/// <param name="context">重写上下文。</param>
/// <returns>处理后的节点。</returns>
public delegate SyntaxNode MiddlewareDelegate<TNode>(TNode node, IRewriteContext context) where TNode : SyntaxNode;

/// <summary>
/// 定义重写中间件的接口。
/// </summary>
/// <typeparam name="TNode">中间件处理的节点类型。</typeparam>
public interface IMiddleware<TNode> where TNode : SyntaxNode
{
    /// <summary>
    /// 执行中间件逻辑。
    /// </summary>
    /// <param name="node">当前节点。</param>
    /// <param name="context">重写上下文。</param>
    /// <param name="next">管道中的下一个中间件。</param>
    /// <returns>重写后的节点。</returns>
    SyntaxNode Invoke(TNode node, IRewriteContext context, MiddlewareDelegate<TNode> next);
}
