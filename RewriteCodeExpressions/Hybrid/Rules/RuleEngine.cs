using Microsoft.CodeAnalysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Rules;

public class RuleEngine
{
    private readonly List<IRule> _rules = new();
    private bool _validated;

    public void RegisterRule(IRule rule)
    {
        ValidateRule(rule);
        _rules.Add(rule);
        _validated = true;
    }

    public void RegisterRule<TNode>(Action<RewriteRule<TNode>> configure) where TNode : SyntaxNode
    {
        var rule = new RewriteRule<TNode>();
        configure(rule);
        ValidateRule(rule);
        _rules.Add(rule);
        _validated = true;
    }

    public IRule? FindMatchingRule(SyntaxNode node, IRewriteContext context)
    {
        EnsureValidated();

        var matches = FindMatchingRules(node, context);
        if (matches.Count == 0)
        {
            return null;
        }

        var topPriority = matches[0].Priority;
        var topMatches = matches.Where(rule => rule.Priority == topPriority).ToList();
        if (topMatches.Count > 1)
        {
            throw new RuleConflictException(node, topMatches);
        }

        return matches[0];
    }

    public IReadOnlyList<IRule> FindMatchingRules(SyntaxNode node, IRewriteContext context)
    {
        EnsureValidated();

        return _rules
            .Where(rule => rule.NodeType.IsInstanceOfType(node) && rule.IsApplicable(node, context))
            .OrderBy(rule => rule.Priority)
            .ToList();
    }

    public void ValidateRules()
    {
        foreach (var rule in _rules)
        {
            ValidateRule(rule);
        }

        _validated = true;
    }

    private void EnsureValidated()
    {
        if (!_validated)
        {
            ValidateRules();
        }
    }

    private static void ValidateRule(IRule rule)
    {
        var expectedMiddlewareInterface = typeof(IMiddleware<>).MakeGenericType(rule.NodeType);
        foreach (var middlewareType in rule.GetMiddlewareTypes())
        {
            if (!expectedMiddlewareInterface.IsAssignableFrom(middlewareType))
            {
                throw new InvalidOperationException(
                    $"Middleware '{middlewareType.Name}' is incompatible with rule node type '{rule.NodeType.Name}'.");
            }
        }
    }
}
