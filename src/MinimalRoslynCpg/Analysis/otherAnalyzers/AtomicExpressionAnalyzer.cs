using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MinimalRoslynCpg.Analysis;

/// <summary>
/// 查找删除规则可直接标记的最小原子表达式单元。
/// </summary>
public sealed class AtomicExpressionAnalyzer
{
  /// <summary>
  /// 从给定根节点中筛出所有不再向上折叠的原子表达式。
  /// </summary>
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

  /// <summary>
  /// 判断一个表达式节点是否属于当前允许直接标记的原子集合。
  /// </summary>
  public bool IsAtomicExpression(ExpressionSyntax expression)
  {
    return expression switch {
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

  /// <summary>
  /// 检查表达式是否已经处在更大的原子访问或调用结构内部。
  /// </summary>
  private static bool HasAtomicExpressionAncestor(ExpressionSyntax expression)
  {
    for (var current = expression.Parent as ExpressionSyntax;
         current is not null;
         current = current.Parent as ExpressionSyntax) {
      // 透明包装节点不改变原子边界，应继续向上看真实宿主。
      if (current is ParenthesizedExpressionSyntax ||
          current is PrefixUnaryExpressionSyntax ||
          current is PostfixUnaryExpressionSyntax ||
          current is CastExpressionSyntax ||
          current is AwaitExpressionSyntax) {
        continue;
      }

      // 条件访问链里的成员绑定由外层条件访问或调用承载，不在这里截断。
      if (expression is MemberBindingExpressionSyntax &&
          current is InvocationExpressionSyntax or ConditionalAccessExpressionSyntax) {
        continue;
      }

      if (current is MemberAccessExpressionSyntax or
          MemberBindingExpressionSyntax or
          ElementAccessExpressionSyntax or
          ConditionalAccessExpressionSyntax or
          InvocationExpressionSyntax or
          ObjectCreationExpressionSyntax or
          ImplicitObjectCreationExpressionSyntax) {
        return true;
      }
    }

    return false;
  }
}
