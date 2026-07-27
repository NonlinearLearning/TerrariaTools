using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Core.Analysis;

/// 查找删除规则可直接标记的最小原子表达式单元。
public sealed class AtomicExpressionAnalyzer
{
    // 扫描根节点下所有可单独判断的原子表达式，并剔除被更大原子宿主覆盖的子项。
    public IReadOnlyList<ExpressionSyntax> Analyze(SyntaxNode root)
    {
        return root
          .DescendantNodesAndSelf()
          .OfType<ExpressionSyntax>()
          .Where(IsAtomicExpression)
          .Where(expression => !HasAtomicExpressionAncestor(expression))
          .OrderBy(expression => expression.SpanStart)
          .ThenByDescending(expression => expression.Span.Length)
          .ToList();
    }

    // 判断一个表达式是否属于删除规则允许直接标记的原子表达式种类。
    public bool IsAtomicExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax => true,
            ThisExpressionSyntax => true,
            BaseExpressionSyntax => true,
            LiteralExpressionSyntax => true,
            MemberAccessExpressionSyntax => true,
            MemberBindingExpressionSyntax => true,
            ElementAccessExpressionSyntax => true,
            ConditionalAccessExpressionSyntax => true,
            InvocationExpressionSyntax => true,
            ObjectCreationExpressionSyntax => true,
            ImplicitObjectCreationExpressionSyntax => true,
            _ => false
        };
    }

    private static bool HasAtomicExpressionAncestor(ExpressionSyntax expression)
    {
        for (var current = expression.Parent as ExpressionSyntax;
             current is not null;
             current = current.Parent as ExpressionSyntax)
        {
            // 透明包装节点不改变原子边界，应继续向上看真实宿主。
            if (current is ParenthesizedExpressionSyntax ||
                current is PrefixUnaryExpressionSyntax ||
                current is PostfixUnaryExpressionSyntax ||
                current is CastExpressionSyntax ||
                current is AwaitExpressionSyntax)
            {
                continue;
            }

            // 条件访问链里的成员绑定由外层条件访问或调用承载，不在这里截断。
            if (expression is MemberBindingExpressionSyntax &&
                current is InvocationExpressionSyntax or ConditionalAccessExpressionSyntax)
            {
                continue;
            }

            if (current is MemberAccessExpressionSyntax or
                MemberBindingExpressionSyntax or
                ElementAccessExpressionSyntax or
                ConditionalAccessExpressionSyntax or
                InvocationExpressionSyntax or
                ObjectCreationExpressionSyntax or
                ImplicitObjectCreationExpressionSyntax)
            {
                return true;
            }
        }

        return false;
    }
}
