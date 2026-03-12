using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Analysis.Dome;
using TerrariaTools.Rules.Dome.Mark.ContextRules;
using System.Linq;

namespace TerrariaTools.Rules.Dome.Mark.StaticRules;

/// <summary>
/// P1: 净化规则 (常量赋值)
/// 如果赋值右侧是字面量或常量，则认为该点已“净化”，阻断污染。
/// </summary>
[SpreadingRule(1, SpreadingRuleType.NodeGuard, SyntaxKind.LocalDeclarationStatement, SyntaxKind.ExpressionStatement)]
public class SanitizationRule : ISpreadingRule
{
    public PropagationResult Propagate(DataFlowDependencyNode source, DataFlowDependencyNode target, DataFlowDependencyEdge edge, SpreadingContext context)
    {
        if (IsSanitizingAssignment(target.Syntax))
        {
            return PropagationResult.Blocked;
        }

        return PropagationResult.None;
    }

    public static bool IsSanitizingAssignment(SyntaxNode stmtSyntax)
    {
        ExpressionSyntax right = null;

        if (stmtSyntax is LocalDeclarationStatementSyntax localDecl)
        {
            var variable = localDecl.Declaration.Variables.FirstOrDefault();
            right = variable?.Initializer?.Value;
        }
        else if (stmtSyntax is ExpressionStatementSyntax exprStmt &&
                 exprStmt.Expression is AssignmentExpressionSyntax assign)
        {
            right = assign.Right;
        }

        if (right == null) return false;

        // 如果右侧是字面量 (e.g., 5, "hello", true, null)，则为净化
        if (right is LiteralExpressionSyntax) return true;

        // 如果右侧是不包含任何 Identifier 的表达式 (e.g., 1 + 2)，也算净化
        if (!right.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any()) return true;

        return false;
    }
}
