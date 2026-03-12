using Microsoft.CodeAnalysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Rules;

/// <summary>
/// 通用的重写规则实现。
/// </summary>
/// <typeparam name="TNode">规则适用的语法节点类型。</typeparam>
public class RewriteRule<TNode> : IRule where TNode : SyntaxNode
{
    private Func<TNode, IRewriteContext, bool>? _predicate;
    private readonly List<Type> _middlewareTypes = new();

    public Type NodeType => typeof(TNode);
    public int Priority { get; private set; } = 100;

    public bool IsApplicable(SyntaxNode node, IRewriteContext context)
    {
        if (node is not TNode typedNode) return false;

        return _predicate?.Invoke(typedNode, context) ?? true;
    }

    public IEnumerable<Type> GetMiddlewareTypes() => _middlewareTypes;

    /// <summary>
    /// 添加一个匹配条件。
    /// </summary>
    /// <param name="condition">条件表达式。</param>
    /// <returns>当前规则实例。</returns>
    public RewriteRule<TNode> When(Func<TNode, IRewriteContext, bool> condition)
    {
        if (_predicate is null)
        {
            _predicate = condition;
        }
        else
        {
            var current = _predicate;
            _predicate = (node, context) => current(node, context) && condition(node, context);
        }

        return this;
    }

    public RewriteRule<TNode> And(Func<TNode, IRewriteContext, bool> condition)
    {
        return When(condition);
    }

    public RewriteRule<TNode> Or(Func<TNode, IRewriteContext, bool> condition)
    {
        if (_predicate is null)
        {
            _predicate = condition;
        }
        else
        {
            var current = _predicate;
            _predicate = (node, context) => current(node, context) || condition(node, context);
        }

        return this;
    }

    public RewriteRule<TNode> Not(Func<TNode, IRewriteContext, bool> condition)
    {
        return And((node, context) => !condition(node, context));
    }

    public RewriteRule<TNode> WithPriority(int priority)
    {
        Priority = priority;
        return this;
    }

    /// <summary>
    /// 注册一个中间件。
    /// </summary>
    /// <typeparam name="TMiddleware">中间件类型。</typeparam>
    /// <returns>当前规则实例。</returns>
    public RewriteRule<TNode> Use<TMiddleware>() where TMiddleware : IMiddleware<TNode>
    {
        _middlewareTypes.Add(typeof(TMiddleware));
        return this;
    }

    /// <summary>
    /// 注册一个中间件类型。
    /// </summary>
    /// <param name="middlewareType">中间件类型。</param>
    /// <returns>当前规则实例。</returns>
    public RewriteRule<TNode> Use(Type middlewareType)
    {
        if (!typeof(IMiddleware<TNode>).IsAssignableFrom(middlewareType))
        {
            throw new ArgumentException($"Type {middlewareType.Name} does not implement IMiddleware<{typeof(TNode).Name}>");
        }
        _middlewareTypes.Add(middlewareType);
        return this;
    }
}
