using Microsoft.CodeAnalysis;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;

/// <summary>
/// 定义重写规则的接口。
/// </summary>
public interface IRule
{
    /// <summary>
    /// 获取该规则适用于的语法节点类型。
    /// </summary>
    Type NodeType { get; }

    /// <summary>
    /// Rule priority. Lower values mean higher priority.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// 判断规则是否适用于给定的节点和上下文。
    /// </summary>
    /// <param name="node">语法节点。</param>
    /// <param name="context">重写上下文。</param>
    /// <returns>如果适用则返回 true，否则返回 false。</returns>
    bool IsApplicable(SyntaxNode node, IRewriteContext context);

    /// <summary>
    /// 获取中间件类型列表。
    /// </summary>
    /// <returns>中间件类型集合。</returns>
    IEnumerable<Type> GetMiddlewareTypes();
}
