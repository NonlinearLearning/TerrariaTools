using System;
using Microsoft.CodeAnalysis.CSharp;

namespace TerrariaTools.Rules.Dome.Mark.ContextRules;

/// <summary>
/// 规则类型
/// </summary>
public enum SpreadingRuleType
{
    /// <summary>
    /// 节点守卫规则 (无上下文)：仅检查节点本身，如果 Blocked 则该节点不可被传播
    /// </summary>
    NodeGuard,

    /// <summary>
    /// 边传播规则 (需上下文)：检查传播路径，决定是否通过该边传播
    /// </summary>
    EdgePropagator
}

/// <summary>
/// 传播规则特性，用于自动发现
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class SpreadingRuleAttribute : Attribute
{
    /// <summary>
    /// 优先级 (0-100)，数字越小优先级越高
    /// </summary>
    public int Priority { get; }

    /// <summary>
    /// 规则类型
    /// </summary>
    public SpreadingRuleType RuleType { get; }

    /// <summary>
    /// 适用的节点类型
    /// </summary>
    public SyntaxKind[] TargetKinds { get; }

    public SpreadingRuleAttribute(int priority, SpreadingRuleType ruleType, params SyntaxKind[] targetKinds)
    {
        Priority = priority;
        RuleType = ruleType;
        TargetKinds = targetKinds;
    }
}
