using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace TerrariaTools.Rules.Dome.Mark.ContextRules;

/// <summary>
/// 传播规则注册中心，负责规则的自动发现和按节点类型索引
/// </summary>
public class SpreadingRuleRegistry
{
    private static readonly Lazy<SpreadingRuleRegistry> _instance = new(() => new SpreadingRuleRegistry());
    public static SpreadingRuleRegistry Instance => _instance.Value;

    private readonly Dictionary<SyntaxKind, List<(ISpreadingRule Rule, int Priority, SpreadingRuleType Type)>> _ruleIndex = new();

    private SpreadingRuleRegistry()
    {
        Initialize();
    }

    private void Initialize()
    {
        // 自动发现所有实现了 ISpreadingRule 且标记了 SpreadingRuleAttribute 的类
        var ruleTypes = typeof(SpreadingRuleRegistry).Assembly.GetTypes()
            .Where(t => typeof(ISpreadingRule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .ToList();

        var annotatedRules = ruleTypes
            .Select(t => (Type: t, Attribute: t.GetCustomAttribute<SpreadingRuleAttribute>()))
            .Where(x => x.Attribute != null)
            .ToList();

        foreach (var (type, attribute) in annotatedRules)
        {
            var ruleInstance = (ISpreadingRule)Activator.CreateInstance(type);

            foreach (var kind in attribute.TargetKinds)
            {
                if (!_ruleIndex.ContainsKey(kind))
                {
                    _ruleIndex[kind] = new List<(ISpreadingRule, int, SpreadingRuleType)>();
                }

                _ruleIndex[kind].Add((ruleInstance, attribute.Priority, attribute.RuleType));
            }
        }

        // 按优先级对规则进行排序
        foreach (var kind in _ruleIndex.Keys.ToList())
        {
            _ruleIndex[kind] = _ruleIndex[kind].OrderBy(x => x.Priority).ToList();
        }
    }

    /// <summary>
    /// 获取适用于特定节点类型的规则列表（已按优先级排序）
    /// </summary>
    public IEnumerable<ISpreadingRule> GetRulesForKind(SyntaxKind kind)
    {
        if (_ruleIndex.TryGetValue(kind, out var rules))
        {
            return rules.Select(x => x.Rule);
        }
        return Enumerable.Empty<ISpreadingRule>();
    }

    /// <summary>
    /// 获取适用于特定节点类型的节点守卫规则（无上下文，P0优先）
    /// </summary>
    public IEnumerable<ISpreadingRule> GetNodeGuardRules(SyntaxKind kind)
    {
        if (_ruleIndex.TryGetValue(kind, out var rules))
        {
            return rules.Where(x => x.Type == SpreadingRuleType.NodeGuard).Select(x => x.Rule);
        }
        return Enumerable.Empty<ISpreadingRule>();
    }

    /// <summary>
    /// 获取适用于特定节点类型的边传播规则（需上下文）
    /// </summary>
    public IEnumerable<ISpreadingRule> GetEdgePropagatorRules(SyntaxKind kind)
    {
        if (_ruleIndex.TryGetValue(kind, out var rules))
        {
            return rules.Where(x => x.Type == SpreadingRuleType.EdgePropagator).Select(x => x.Rule);
        }
        return Enumerable.Empty<ISpreadingRule>();
    }
}
