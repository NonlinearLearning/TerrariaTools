using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Analysis.Dome;
using TerrariaTools.Rules.Dome.Mark.ContextRules;
using QuikGraph;

namespace TerrariaTools.Rules.Dome.Mark.StaticRules;

/// <summary>
/// P0: 继承链安全熔断规则
/// 防止标记向 virtual/override/abstract 方法或接口实现传播。
/// </summary>
[SpreadingRule(0, SpreadingRuleType.NodeGuard, SyntaxKind.MethodDeclaration, SyntaxKind.PropertyDeclaration, SyntaxKind.IndexerDeclaration)]
public class InheritanceShieldRule : ISpreadingRule
{
    public PropagationResult Propagate(DataFlowDependencyNode source, DataFlowDependencyNode target, DataFlowDependencyEdge edge, SpreadingContext context)
    {
        var symbol = target.Symbol;
        if (symbol == null) return PropagationResult.None;

        // 1. 检查基本修饰符 (Virtual/Abstract/Override)
        if (symbol.IsVirtual || symbol.IsAbstract || symbol.IsOverride)
        {
            return PropagationResult.Blocked;
        }

        // 2. 检查继承图中的接口实现关系
        // 通过图查询避免昂贵的 FindImplementationForInterfaceMember 调用
        if (context.InheritanceGraph != null && context.InheritanceGraph.ContainsVertex(symbol))
        {
            // 如果存在指向接口成员的 Implementation 边，说明它是接口实现
            if (context.InheritanceGraph.OutEdges(symbol)
                .Any(e => e.RelationType == InheritanceRelationType.Implementation))
            {
                return PropagationResult.Blocked;
            }
        }

        return PropagationResult.None;
    }
}
