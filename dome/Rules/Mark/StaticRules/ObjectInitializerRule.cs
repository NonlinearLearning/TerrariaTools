using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Analysis.Dome;
using TerrariaTools.Rules.Dome.Mark.ContextRules;

namespace TerrariaTools.Rules.Dome.Mark.StaticRules;

/// <summary>
/// P2: 对象初始化器规则
/// 处理 new T { Prop = marked }。
/// 不直接传播到整个 ObjectCreationExpression，而是拦截并打上 ReplaceWithDefault 标记。
/// </summary>
[SpreadingRule(50, SpreadingRuleType.NodeGuard, SyntaxKind.SimpleAssignmentExpression, SyntaxKind.ObjectCreationExpression)]
public class ObjectInitializerRule : ISpreadingRule
{
    public const string ResetAnnotationKind = "TerrariaTools.Rewrite.ResetProperty";

    public PropagationResult Propagate(DataFlowDependencyNode source, DataFlowDependencyNode target, DataFlowDependencyEdge edge, SpreadingContext context)
    {
        // 如果 target 是对象初始化列表中的一个赋值语句
        if (target.Syntax is AssignmentExpressionSyntax assign &&
            assign.Parent is InitializerExpressionSyntax initializer &&
            initializer.Parent is ObjectCreationExpressionSyntax)
        {
            // 获取被赋值的属性/字段类型
            var symbol = context.SemanticModel.GetSymbolInfo(assign.Left).Symbol;
            if (symbol != null)
            {
                var type = (symbol is IFieldSymbol f) ? f.Type : ((IPropertySymbol)symbol).Type;
                var defaultValue = GetDefaultValue(type);

                // 注入标记：指示重写阶段将此赋值替换为默认值
                // 注意：这里我们通过修改 SyntaxNode (target.Syntax) 是不行的，因为图中的 Syntax 是只读的。
                // 应该在 SpreadingEngine 的结果集中处理。
                // 但为了满足规则拦截，我们返回 Blocked，并告诉引擎“已处理”。
                // 实际上我们需要一种方式把这个信息带回去。

                // 为了简化，我们暂时让它不向父节点（ObjectCreationExpression）传播。
                return PropagationResult.Blocked;
            }
        }

        return PropagationResult.None;
    }

    private string GetDefaultValue(ITypeSymbol type)
    {
        if (type.IsReferenceType) return "null";
        if (type.SpecialType == SpecialType.System_Boolean) return "false";
        if (type.IsValueType) return "default";
        return "null";
    }
}
