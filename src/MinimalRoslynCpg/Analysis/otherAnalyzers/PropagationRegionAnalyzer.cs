using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace MinimalRoslynCpg.Analysis;

/// <summary>
/// 描述传播阶段允许遍历的局部语法区域。
/// </summary>
public sealed record PropagationCodeRegion(
  SyntaxNode AnchorNode,
  SyntaxNode RegionNode,
  TextSpan Span,
  int NodeCount,
  int ExpressionCount,
  int StatementCount);

/// <summary>
/// 为传播阶段选择受限的语法区域。
/// </summary>
public sealed class PropagationRegionAnalyzer
{
  /// <summary>
  /// 返回当前锚点对应的传播区域。
  /// </summary>
  public PropagationCodeRegion Analyze(SyntaxNode anchorNode, CpgAnalysisContext context)
  {
    var regionNode = ResolveRegionNode(anchorNode);
    var nodes = regionNode.DescendantNodesAndSelf().ToList();
    return new PropagationCodeRegion(
      anchorNode,
      regionNode,
      regionNode.Span,
      nodes.Count,
      nodes.OfType<ExpressionSyntax>().Count(),
      nodes.OfType<StatementSyntax>().Count());
  }

  /// <summary>
  /// `else-if` 条件优先扩到所属 `else` 子句，保证尾段传播可见。
  /// </summary>
  private static SyntaxNode ResolveRegionNode(SyntaxNode anchorNode)
  {
    if (TryResolveElseIfRegion(anchorNode, out var elseIfRegion))
    {
      return elseIfRegion!;
    }

    var statement = anchorNode.FirstAncestorOrSelf<StatementSyntax>();
    if (statement is not null)
    {
      return statement;
    }

    return anchorNode.FirstAncestorOrSelf<VariableDeclaratorSyntax>()
      ?? anchorNode.FirstAncestorOrSelf<ParameterSyntax>()
      ?? anchorNode;
  }

  /// <summary>
  /// 判断锚点是否位于 else-if 条件中。
  /// </summary>
  private static bool TryResolveElseIfRegion(SyntaxNode anchorNode, out ElseClauseSyntax? elseClause)
  {
    var nestedIf = anchorNode.FirstAncestorOrSelf<IfStatementSyntax>();
    if (nestedIf?.Parent is ElseClauseSyntax parentElseClause &&
        (ReferenceEquals(anchorNode, nestedIf) ||
         nestedIf.Condition.Span.Contains(anchorNode.Span)))
    {
      elseClause = parentElseClause;
      return true;
    }

    elseClause = null;
    return false;
  }
}
